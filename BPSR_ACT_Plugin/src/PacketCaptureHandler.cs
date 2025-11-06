using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using PacketDotNet;
using SharpPcap;
using ZstdSharp;

namespace BPSR_ACT_Plugin.src
{
    /// <summary>
    /// Receives raw network packets from SharpPcap (PacketArrival) and processes them to extract application-layer payloads (OnPayloadReady.)
    /// </summary>
    internal static class PacketCaptureHandler
    {
        public static Action<string> OnLogStatus;
        public static Action<uint, ReadOnlyMemory<byte>> OnPayloadReady;

        // Ported server detection state from SRDC's server.js
        private static string _currentServer = string.Empty;

        private static readonly object _tcpLock = new object();

        // New reassembler (adapted from SharpPcap Tcp reassembly sample logic).
        private static readonly TcpReassembler _tcpReassembler = new TcpReassembler(ProcessPacket);

        // Inactivity tracking
        private static DateTime _lastPacketReceived = DateTime.MinValue;
        private static readonly Timer _inactivityTimer;
        private const int InactivityTimeoutSeconds = 30;
        private static readonly TimeSpan _inactivityCheckInterval = TimeSpan.FromSeconds(5);

        // Reusable signature arrays to avoid repeated allocations
        private static readonly byte[] s_frameDownSignature = new byte[] { 0x00, 0x63, 0x33, 0x53, 0x42, 0x00 }; // c3SB??
        private static readonly byte[] s_frameUpSignature = new byte[] { 0x00, 0x06, 0x26, 0xad, 0x66, 0x00 };
        private static readonly byte[] s_loginSignature = new byte[]
        {
            0x00,0x00,0x00,0x62,
            0x00,0x03,
            0x00,0x00,0x00,0x01,
            0x00,0x11,0x45,0x14,
            0x00,0x00,0x00,0x00,
            0x0a,0x4e,0x08,0x01,0x22,0x24
        };

        static PacketCaptureHandler()
        {
            // Start periodic inactivity check.
            _inactivityTimer = new Timer(CheckInactivity, null, _inactivityCheckInterval, _inactivityCheckInterval);
        }

        private static void CheckInactivity(object state)
        {
            try
            {
                lock (_tcpLock)
                {
                    if (_lastPacketReceived == DateTime.MinValue) return;

                    var idle = DateTime.UtcNow - _lastPacketReceived;
                    if (idle.TotalSeconds >= InactivityTimeoutSeconds)
                    {
                        if (!string.IsNullOrEmpty(_currentServer))
                        {
                            _currentServer = string.Empty;
                        }

                        ClearTcpCacheInternal();
                        _lastPacketReceived = DateTime.MinValue;
                        OnLogStatus?.Invoke($"No packets received in {InactivityTimeoutSeconds}s — cleared current server and TCP cache");
                    }
                }
            }
            catch (Exception ex)
            {
                // Keep timer resilient
                OnLogStatus?.Invoke($"Inactivity check error: {ex.Message}");
            }
        }

        public static void PacketArrival(object s, PacketCapture e)
        {
            try
            {
                var raw = e.GetPacket() as RawCapture;
                if (raw == null) return;

                // Parse packet with PacketDotNet
                var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                var ipPacket = packet.Extract<IPv4Packet>();
                if (ipPacket == null) return;

                // Grab raw IP bytes to compute flags/offset reliably (PacketDotNet's API surface varies by version).
                var ipBytes = ipPacket.Bytes;
                if (ipBytes == null || ipBytes.Length < 20) return;

                // skip anything that's not TCP
                int protocol = (int)ipPacket.Protocol;
                const int IP_PROTOCOL_TCP = 6;
                if (protocol != IP_PROTOCOL_TCP) return;

                // Extract flags+fragment offset from the raw IP header bytes (same as original manual parsing)
                int flagsFrag = (ipBytes[6] << 8) | ipBytes[7];
                bool moreFragments = (flagsFrag & 0x2000) != 0;
                int fragOffset = (flagsFrag & 0x1FFF) * 8;

                // If packet is fragmented we skip it — fallback/manual TCP parsing is removed to reduce code size.
                if (moreFragments || fragOffset > 0)
                    return;

                var src = ipPacket.SourceAddress;
                var dst = ipPacket.DestinationAddress;

                // Rely solely on PacketDotNet's TcpPacket extraction.
                var tcpPacket = packet.Extract<TcpPacket>();
                if (tcpPacket == null)
                {
                    // log once in case capture/parsing differences cause this frequently
                    OnLogStatus?.Invoke("PacketDotNet failed to extract TcpPacket — skipping");
                    return;
                }

                ushort srcPort = tcpPacket.SourcePort;
                ushort dstPort = tcpPacket.DestinationPort;
                uint seqNo = tcpPacket.SequenceNumber;
                var appPayload = tcpPacket.PayloadData ?? Array.Empty<byte>();

                if (appPayload.Length == 0) return;

                // Mirror the server.js behavior: detect scene server first, then reassemble TCP stream and emit full packets.
                string srcServer = $"{src}:{srcPort} -> {dst}:{dstPort}";
                string srcServerRe = $"{dst}:{dstPort} -> {src}:{srcPort}";

                // first, do detection under lock and possibly clear state
                bool detected = false;
                lock (_tcpLock)
                {
                    // If current_server unknown, attempt detection with small packets (FrameDown Notify signature / Login return / FrameUp)
                    if (string.IsNullOrEmpty(_currentServer) || _currentServer != srcServer && _currentServer != srcServerRe)
                    {
                        DetectSceneServerFromPayload(appPayload.AsSpan(), srcServer, srcServerRe);

                        // If we've just detected server we return (mirror JS branch that returns early)
                        if (!string.IsNullOrEmpty(_currentServer))
                        {
                            detected = true;
                        }
                    } // end current_server detection block

                    // mark last-received time for inactivity detection
                    _lastPacketReceived = DateTime.UtcNow;
                } // end tcp lock
                if (detected) return;

                try
                {
                    _tcpReassembler.AddSegment(seqNo, appPayload.AsSpan());
                }
                catch (Exception ex)
                {
                    OnLogStatus?.Invoke($"ProcessTcpSegment error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Error processing packet: {ex.Message}");
            }
        }

        private static void DetectSceneServerFromPayload(ReadOnlySpan<byte> appPayload, string srcServer, string srcServerRe)
        {
            try
            {
                // case: buf[4] == 0 && buf[5] == 6 (FrameDown Notify detection)
                if (appPayload.Length > 6 && appPayload[4] == 0 && appPayload[5] == 6)
                {
                    // use ReadOnlySpan to avoid allocations    
                    var data = appPayload.Slice(10);
                    if (data.Length > 0)
                    {
                        int idx = 0;
                        while (idx + 4 <= data.Length)
                        {
                            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(idx, 4));
                            idx += 4;
                            if (len - 4 <= 0 || idx + (len - 4) > data.Length) break;
                            var data1 = data.Slice(idx, len - 4);
                            idx += len - 4;

                            if (data1.Length < 5 + s_frameDownSignature.Length) break;
                            bool eq = data1.Slice(5, s_frameDownSignature.Length).SequenceEqual(s_frameDownSignature);
                            if (!eq) break;
                            if (_currentServer != srcServer)
                            {
                                _currentServer = srcServer;
                                ClearTcpCacheInternal();
                                // server change hook (no-op here, mirror JS)
                                OnLogStatus?.Invoke($"Got Scene Server Address by FrameDown Notify Packet: {_currentServer}");
                            }
                        }
                    }
                }
            }
            catch { /* ignore detection exceptions */ }

            try
            {
                // login return detection (buf.length == 0x62)
                if (appPayload.Length == 0x62)
                {
                    bool check1 = appPayload.Slice(0, 10).SequenceEqual(s_loginSignature.AsSpan(0, 10));
                    bool check2 = appPayload.Slice(14, 6).SequenceEqual(s_loginSignature.AsSpan(14, 6));
                    if (check1 && check2)
                    {
                        if (_currentServer != srcServer)
                        {
                            _currentServer = srcServer;
                            ClearTcpCacheInternal();
                            OnLogStatus?.Invoke($"Got Scene Server Address by Login Return Packet: {_currentServer}");
                        }
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                // FrameUp detection: buf[4] == 0 && buf[5] == 5
                if (appPayload.Length > 6 && appPayload[4] == 0 && appPayload[5] == 5)
                {
                    var data = appPayload.Slice(10);
                    if (data.Length > 0)
                    {
                        int idx = 0;
                        while (idx + 4 <= data.Length)
                        {
                            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(idx, 4));
                            idx += 4;
                            if (len - 4 <= 0 || idx + (len - 4) > data.Length) break;
                            var data1 = data.Slice(idx, len - 4);
                            idx += len - 4;

                            if (data1.Length < 5 + s_frameUpSignature.Length) break;
                            bool eq = data1.Slice(5, s_frameUpSignature.Length).SequenceEqual(s_frameUpSignature);
                            if (!eq) break;
                            if (_currentServer != srcServerRe)
                            {
                                _currentServer = srcServerRe;
                                ClearTcpCacheInternal();
                                OnLogStatus?.Invoke($"Got Scene Server Address by FrameUp Notify Packet: {_currentServer}");
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static void ClearTcpCacheInternal()
        {
            _tcpReassembler.Clear();
            _lastPacketReceived = DateTime.MinValue;
        }

        private static void ProcessPacket(ReadOnlyMemory<byte> memoryPackets)
        {
            ProcessPacket(memoryPackets.Span);
        }

        private static void ProcessPacket(ReadOnlySpan<byte> packets)
        {
            try
            {
                if (packets.Length == 0) return;
                int offset = 0;
                int total = packets.Length;

                while (offset + 4 <= total)
                {
                    // Peek size (big-endian)
                    uint packetSize = BinaryPrimitives.ReadUInt32BigEndian(packets.Slice(offset, 4));
                    if (packetSize < 6)
                    {
                        OnLogStatus?.Invoke("Received invalid packet (size < 6)");
                        return;
                    }

                    if (offset + packetSize > total)
                    {
                        // Not enough bytes yet for the full packet — stop parsing.
                        break;
                    }

                    int pktStart = offset;
                    int inner = pktStart + 4; // skip size field

                    // Read packetType (uint16)
                    if (inner + 2 > pktStart + (int)packetSize)
                    {
                        // malformed packet, stop
                        break;
                    }
                    ushort packetType = BinaryPrimitives.ReadUInt16BigEndian(packets.Slice(inner, 2));
                    inner += 2;

                    bool isZstdCompressed = (packetType & 0x8000) != 0;
                    ushort msgTypeId = (ushort)(packetType & 0x7fff);

                    const ushort MessageType_Notify = 2;
                    const ushort MessageType_Return = 3;
                    const ushort MessageType_FrameDown = 6;

                    switch (msgTypeId)
                    {
                        case MessageType_Notify:
                            {
                                int payloadOffset = inner;
                                int payloadLength = pktStart + (int)packetSize - payloadOffset;
                                if (payloadLength > 0)
                                {
                                    // Process notify up to (but not dispatching by methodId)
                                    ProcessNotifyMsg(packets.Slice(payloadOffset, payloadLength), isZstdCompressed);
                                }
                                break;
                            }
                        case MessageType_Return:
                            // Nothing implemented (mirror JS)
                            break;
                        case MessageType_FrameDown:
                            {
                                // serverSequenceId - can be read but not used here
                                if (inner + 4 > pktStart + (int)packetSize) break;
                                inner += 4;
                                int rem = pktStart + (int)packetSize - inner;
                                if (rem <= 0) break;

                                // If not compressed, avoid the intermediate allocation and recursively process the nested slice.
                                var nestedSpan = packets.Slice(inner, rem);
                                if (isZstdCompressed)
                                {
                                    using (var decompressor = new Decompressor())
                                        nestedSpan = decompressor.Unwrap(nestedSpan);
                                    ProcessPacket(nestedSpan);
                                }
                                else
                                {
                                    // no allocation required
                                    ProcessPacket(nestedSpan);
                                }

                                break;
                            }
                        default:
                            // ignore other types
                            break;
                    }

                    offset += (int)packetSize;
                }
            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Fail while parsing assembled packet: {ex.Message}");
            }
        }

        // Read serviceUuid, stubId, methodId, check serviceUuid and extract msgPayload.
        // Stop before handling methodId (no switch here). If serviceUuid matches, call OnPayloadReady with msgPayload.
        // Now receives a ReadOnlySpan<byte> instead of buffer+offset+length.
        private static void ProcessNotifyMsg(ReadOnlySpan<byte> buffer, bool isZstdCompressed)
        {
            try
            {
                // need at least 8 + 4 + 4 = 16 bytes for serviceUuid + stubId + methodId
                if (buffer.Length < 16)
                {
                    OnLogStatus?.Invoke("NotifyMsg too short");
                    return;
                }

                int pos = 0;
                ulong serviceUuid = BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(pos, 8));
                pos += 8;
                uint stubId = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(pos, 4));
                pos += 4;
                uint methodId = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(pos, 4));
                pos += 4;

                // Expected service UUID from JS: 0x0000000063335342
                const ulong ExpectedServiceUuid = 0x0000000063335342UL;
                if (serviceUuid != ExpectedServiceUuid)
                {
                    OnLogStatus?.Invoke($"Skipping NotifyMsg with serviceId {serviceUuid:X}");
                    return;
                }

                int remaining = buffer.Length - pos;
                var payloadSpan = buffer.Slice(pos, Math.Max(0, remaining));

                ReadOnlySpan<byte> msgPayload;
                if (isZstdCompressed && payloadSpan.Length > 0)
                {
                    using (var decompressor = new Decompressor())
                        msgPayload = decompressor.Unwrap(payloadSpan);
                }
                else
                {
                    // make single copy to a heap array for OnPayloadReady
                    msgPayload = payloadSpan;
                }

                // Onwards to BPSRPacketHandler!
                try
                {
                    OnPayloadReady?.Invoke(methodId, msgPayload.ToArray());
                }
                catch (Exception ex)
                {
                    OnLogStatus?.Invoke($"Error invoking OnPayloadReady: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Error in ProcessNotifyMsgPartial: {ex.Message}");
            }
        }
    }
}
