using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Timers;
using SharpPcap;
using ZstdSharp;

namespace BPSR_ACT_Plugin.src
{
    internal static class PacketCaptureHelper
    {
        public static Action<string> OnLogStatus;

        // Invoked when application-layer payload (TCP payload) is extracted.
        // Wire this to call the JS processor (e.g. via a Node bridge) or any .NET packet processor.
        // This is the place where you'd call: processor.processPacket(packet)
        public static Action<uint, byte[]> OnPayloadReady;

        // Simple fragment reassembly cache similar to the JS implementation.
        private class FragmentEntry
        {
            public List<byte[]> Fragments { get; } = new List<byte[]>();
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        }

        private static readonly Dictionary<string, FragmentEntry> _fragmentCache = new Dictionary<string, FragmentEntry>();
        private static readonly object _fragLock = new object();
        private static readonly TimeSpan FragmentTimeout = TimeSpan.FromSeconds(10);

        // Ported TCP reassembly / server detection state from server.js
        private static string _currentServer = string.Empty;
        private static readonly List<byte> _data = new List<byte>();
        private static uint _tcpNextSeq = 0;
        private static bool _tcpNextSeqInitialized = false;
        private static readonly Dictionary<uint, byte[]> _tcpCache = new Dictionary<uint, byte[]>();
        private static DateTime _tcpLastTime = DateTime.MinValue;
        private static readonly object _tcpLock = new object();

        static PacketCaptureHelper()
        {
            //TODO: Re-code a timer to handle "Cannot capture the next packet! Is the game closed or disconnected?"
        }

        private static void ClearTcpCacheInternal()
        {
            lock (_tcpLock)
            {
                _data.Clear();
                _tcpNextSeqInitialized = false;
                _tcpLastTime = DateTime.MinValue;
                _tcpCache.Clear();
            }
        }

        private static uint ReadUint32BE(IList<byte> buf, int offset)
        {
            return (uint)(buf[offset] << 24 | buf[offset + 1] << 16 | buf[offset + 2] << 8 | buf[offset + 3]);
        }

        // Small big-endian binary reader used to parse assembled packets similar to JS BinaryReader
        private sealed class BigEndianReader
        {
            private readonly byte[] _buf;
            private int _offset;

            public BigEndianReader(byte[] buf, int offset = 0)
            {
                _buf = buf ?? Array.Empty<byte>();
                _offset = offset;
            }

            public int Remaining() => Math.Max(0, _buf.Length - _offset);

            public uint PeekUInt32()
            {
                if (Remaining() < 4) throw new InvalidOperationException("Not enough bytes to peek UInt32");
                return (uint)(_buf[_offset] << 24 | _buf[_offset + 1] << 16 | _buf[_offset + 2] << 8 | _buf[_offset + 3]);
            }

            public uint ReadUInt32()
            {
                var v = PeekUInt32();
                _offset += 4;
                return v;
            }

            public ushort ReadUInt16()
            {
                if (Remaining() < 2) throw new InvalidOperationException("Not enough bytes to read UInt16");
                var v = (ushort)(_buf[_offset] << 8 | _buf[_offset + 1]);
                _offset += 2;
                return v;
            }

            public ulong ReadUInt64()
            {
                if (Remaining() < 8) throw new InvalidOperationException("Not enough bytes to read UInt64");
                ulong v = (ulong)_buf[_offset] << 56 |
                          (ulong)_buf[_offset + 1] << 48 |
                          (ulong)_buf[_offset + 2] << 40 |
                          (ulong)_buf[_offset + 3] << 32 |
                          (ulong)_buf[_offset + 4] << 24 |
                          (ulong)_buf[_offset + 5] << 16 |
                          (ulong)_buf[_offset + 6] << 8 |
                          _buf[_offset + 7];
                _offset += 8;
                return v;
            }

            public byte[] ReadBytes(int length)
            {
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
                if (Remaining() < length) throw new InvalidOperationException("Not enough bytes to read requested length");
                var arr = new byte[length];
                Array.Copy(_buf, _offset, arr, 0, length);
                _offset += length;
                return arr;
            }

            public byte[] ReadRemaining()
            {
                var arr = new byte[Remaining()];
                Array.Copy(_buf, _offset, arr, 0, arr.Length);
                _offset = _buf.Length;
                return arr;
            }
        }

        // Process assembled length-prefixed packet stream similar to JS processPacket, but stop before Notify method switch.
        private static void ProcessPacketStream(byte[] packets)
        {
            try
            {
                var reader = new BigEndianReader(packets);

                do
                {
                    if (reader.Remaining() < 4) break;
                    uint packetSize = reader.PeekUInt32();
                    if (packetSize < 6)
                    {
                        OnLogStatus?.Invoke("Received invalid packet (size < 6)");
                        return;
                    }

                    var packetReader = new BigEndianReader(reader.ReadBytes((int)packetSize));
                    packetReader.ReadUInt32(); // advance past size
                    ushort packetType = packetReader.ReadUInt16();
                    bool isZstdCompressed = (packetType & 0x8000) != 0;
                    ushort msgTypeId = (ushort)(packetType & 0x7fff);

                    const ushort MessageType_Notify = 2;
                    const ushort MessageType_Return = 3;
                    const ushort MessageType_FrameDown = 6;

                    switch (msgTypeId)
                    {
                        case MessageType_Notify:
                            // Process Notify up to before the switch inside _processNotifyMsg
                            ProcessNotifyMsgPartial(packetReader, isZstdCompressed);
                            break;
                        case MessageType_Return:
                            // Nothing implemented (mirror JS)
                            break;
                        case MessageType_FrameDown:
                            // serverSequenceId - can be read but not used here
                            if (packetReader.Remaining() < 4) break;
                            packetReader.ReadUInt32();
                            if (packetReader.Remaining() == 0) break;
                            var nestedPacket = packetReader.ReadRemaining();
                            if (isZstdCompressed)
                            {
                                using (var decompressor = new Decompressor())
                                    nestedPacket = decompressor.Unwrap(nestedPacket).ToArray();
                            }
                            // Recursively process nested packet stream
                            ProcessPacketStream(nestedPacket);
                            break;
                        default:
                            // ignore other types
                            break;
                    }
                } while (reader.Remaining() > 0);
            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Fail while parsing assembled packet: {ex.Message}");
            }
        }

        // Read serviceUuid, stubId, methodId, check serviceUuid and extract msgPayload.
        // Stop before handling methodId (no switch here). If serviceUuid matches, call OnPayloadReady with msgPayload.
        private static void ProcessNotifyMsgPartial(BigEndianReader reader, bool isZstdCompressed)
        {
            try
            {
                // Read values in big-endian to match JS readBigUInt64BE/readUInt32BE
                ulong serviceUuid = reader.ReadUInt64();
                uint stubId = reader.ReadUInt32();
                uint methodId = reader.ReadUInt32();

                // Expected service UUID from JS: 0x0000000063335342
                const ulong ExpectedServiceUuid = 0x0000000063335342UL;
                if (serviceUuid != ExpectedServiceUuid)
                {
                    OnLogStatus?.Invoke($"Skipping NotifyMsg with serviceId {serviceUuid:X}");
                    return;
                }

                var msgPayload = reader.ReadRemaining();
                if (isZstdCompressed)
                {
                    using (var decompressor = new Decompressor())
                        msgPayload = decompressor.Unwrap(msgPayload).ToArray();
                }

                // STOP: do not dispatch by methodId here. Instead hand off the payload to OnPayloadReady
                try
                {
                    OnPayloadReady?.Invoke(methodId, msgPayload);
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

        public static void Device_OnPacketArrival(object s, PacketCapture e)
        {
            try
            {
                // Try to retrieve raw bytes from the event. SharpPcap APIs vary between versions:
                // e.GetPacket() may return byte[] or a RawCapture. Handle both common cases.
                byte[] frame;
                var raw = e.GetPacket();
                if (raw is RawCapture rc)
                {
                    frame = rc.Data;
                }
                else
                {
                    OnLogStatus?.Invoke($"Unsupported packet object type: {raw?.GetType().FullName}");
                    return;
                }

                if (frame == null || frame.Length < 34) // bare minimum for Ethernet + IPv4 + TCP
                {
                    return;
                }

                // Assume Ethernet by default; adjust if you need to support other link types.
                const int ethOffset = 14;
                if (frame.Length <= ethOffset + 20) return;

                int ipStart = ethOffset;
                // IPv4 version + IHL
                byte versionIhl = frame[ipStart];
                int version = versionIhl >> 4 & 0xF;
                if (version != 4)
                {
                    // not IPv4
                    return;
                }

                int ihl = (versionIhl & 0x0F) * 4;
                if (ihl < 20 || frame.Length < ipStart + ihl) return;

                // total length (network byte order)
                int totalLen = frame[ipStart + 2] << 8 | frame[ipStart + 3];
                if (totalLen < ihl) return;

                // identification
                int id = frame[ipStart + 4] << 8 | frame[ipStart + 5];

                // flags + frag offset
                int flagsFrag = frame[ipStart + 6] << 8 | frame[ipStart + 7];
                bool moreFragments = (flagsFrag & 0x2000) != 0;
                int fragOffset = (flagsFrag & 0x1FFF) * 8; // in bytes

                // protocol
                int protocol = frame[ipStart + 9];

                // source/dest addresses (for cache key)
                var src = new IPAddress(frame.Skip(ipStart + 12).Take(4).ToArray());
                var dst = new IPAddress(frame.Skip(ipStart + 16).Take(4).ToArray());

                // Application wants only TCP streams
                const int IP_PROTOCOL_TCP = 6;
                if (protocol != IP_PROTOCOL_TCP) return;

                // Helper to extract ip-payload (bytes following the IP header)
                Func<byte[]> extractIpPayload = () =>
                {
                    int payloadLen = totalLen - ihl;
                    int payloadStart = ipStart + ihl;
                    if (payloadLen <= 0 || payloadStart + payloadLen > frame.Length) return null;
                    var buf = new byte[payloadLen];
                    Array.Copy(frame, payloadStart, buf, 0, payloadLen);
                    return buf;
                };

                byte[] ipPayload = null;

                if (moreFragments || fragOffset > 0)
                {
                    // Handle fragments: store the fragment (starting at IP header) for reassembly.
                    // We'll store the IP-subarray (starting at ipStart) to mimic the JS logic.
                    byte[] ipPacket = frame.Skip(ipStart).Take(Math.Min(frame.Length - ipStart, totalLen)).ToArray();

                    string key = $"{id}-{src}-{dst}-{protocol}";

                    lock (_fragLock)
                    {
                        // Purge stale entries
                        var stale = _fragmentCache.Where(kv => DateTime.UtcNow - kv.Value.Timestamp > FragmentTimeout)
                                                  .Select(kv => kv.Key)
                                                  .ToList();
                        foreach (var k in stale) _fragmentCache.Remove(k);

                        if (!_fragmentCache.TryGetValue(key, out var entry))
                        {
                            entry = new FragmentEntry();
                            _fragmentCache[key] = entry;
                        }

                        entry.Fragments.Add(ipPacket);
                        entry.Timestamp = DateTime.UtcNow;

                        // If this fragment indicates there are more fragments to come, wait.
                        if (moreFragments)
                        {
                            return;
                        }

                        // Last fragment received, try reassembly.
                        // Determine total payload size by looking through fragments
                        int totalPayloadLength = 0;
                        var fragmentData = new List<(int offset, byte[] payload)>();

                        foreach (var frag in entry.Fragments)
                        {
                            if (frag.Length < 20) continue;
                            // frag is an IP packet that starts at IP header
                            byte vIhl = frag[0];
                            int fIhl = (vIhl & 0x0F) * 4;
                            int fTotalLen = frag[2] << 8 | frag[3];
                            int fFlagsFrag = frag[6] << 8 | frag[7];
                            int fFragOffset = (fFlagsFrag & 0x1FFF) * 8;
                            int fPayloadLen = fTotalLen - fIhl;
                            if (fPayloadLen < 0) continue;
                            if (fIhl + fPayloadLen > frag.Length) fPayloadLen = Math.Max(0, frag.Length - fIhl);

                            var payload = new byte[fPayloadLen];
                            Array.Copy(frag, fIhl, payload, 0, fPayloadLen);
                            fragmentData.Add((fFragOffset, payload));

                            int endOffset = fFragOffset + fPayloadLen;
                            if (endOffset > totalPayloadLength) totalPayloadLength = endOffset;
                        }

                        if (totalPayloadLength == 0)
                        {
                            _fragmentCache.Remove(key);
                            return;
                        }

                        var fullPayload = new byte[totalPayloadLength];
                        foreach (var fd in fragmentData)
                        {
                            // bounds-checking to avoid exceptions if fragments inconsistent
                            if (fd.offset < 0 || fd.offset >= totalPayloadLength) continue;
                            int copyLen = Math.Min(fd.payload.Length, totalPayloadLength - fd.offset);
                            Array.Copy(fd.payload, 0, fullPayload, fd.offset, copyLen);
                        }

                        // Clear cache for this key
                        _fragmentCache.Remove(key);

                        ipPayload = fullPayload; // ipPayload here is the IP payload (starts with TCP header)
                    }
                }
                else
                {
                    // Not fragmented: extract ip-payload directly
                    ipPayload = extractIpPayload();
                }

                if (ipPayload == null || ipPayload.Length < 20)
                {
                    // nothing to process or no TCP header
                    return;
                }

                // Parse TCP header inside ipPayload to find TCP header length and application payload.
                // TCP header layout: srcport(2), dstport(2), seq(4), ack(4), dataoff/reserved/flags...
                if (ipPayload.Length < 20) return;

                ushort srcPort = (ushort)(ipPayload[0] << 8 | ipPayload[1]);
                ushort dstPort = (ushort)(ipPayload[2] << 8 | ipPayload[3]);
                uint seqNo = ReadUint32BE(ipPayload, 4);
                uint ackNo = ReadUint32BE(ipPayload, 8);

                int dataOffsetAndFlagsIndex = 12;
                if (dataOffsetAndFlagsIndex >= ipPayload.Length) return;
                int tcpHeaderLen = (ipPayload[dataOffsetAndFlagsIndex] >> 4 & 0x0F) * 4;
                if (tcpHeaderLen < 20) tcpHeaderLen = 20;
                if (tcpHeaderLen >= ipPayload.Length) return;

                int appPayloadLen = ipPayload.Length - tcpHeaderLen;
                if (appPayloadLen <= 0) return;

                var appPayload = new byte[appPayloadLen];
                Array.Copy(ipPayload, tcpHeaderLen, appPayload, 0, appPayloadLen);

                // Mirror the server.js behavior: detect scene server first, then reassemble TCP stream and emit full packets.
                string srcServer = $"{src}:{srcPort} -> {dst}:{dstPort}";
                string srcServerRe = $"{dst}:{dstPort} -> {src}:{srcPort}";

                lock (_tcpLock)
                {
                    // If current_server unknown, attempt detection with small packets (FrameDown Notify signature / Login return / FrameUp)
                    if (string.IsNullOrEmpty(_currentServer) || _currentServer != srcServer && _currentServer != srcServerRe)
                    {
                        try
                        {
                            // case: buf[4] == 0 && buf[5] == 6 (FrameDown Notify detection)
                            if (appPayload.Length > 6 && appPayload[4] == 0 && appPayload[5] == 6)
                            {
                                var data = appPayload.Skip(10).ToArray();
                                if (data.Length > 0)
                                {
                                    int idx = 0;
                                    while (idx + 4 <= data.Length)
                                    {
                                        var len = (int)ReadUint32BE(data, idx);
                                        idx += 4;
                                        if (len - 4 <= 0 || idx + (len - 4) > data.Length) break;
                                        var data1 = data.Skip(idx).Take(len - 4).ToArray();
                                        idx += len - 4;

                                        var signature = new byte[] { 0x00, 0x63, 0x33, 0x53, 0x42, 0x00 }; // c3SB??
                                        if (data1.Length < 5 + signature.Length) break;
                                        bool eq = data1.Skip(5).Take(signature.Length).SequenceEqual(signature);
                                        if (!eq) break;
                                        if (_currentServer != srcServer)
                                        {
                                            _currentServer = srcServer;
                                            ClearTcpCacheInternal();
                                            _tcpNextSeq = unchecked(seqNo + (uint)appPayload.Length);
                                            _tcpNextSeqInitialized = true;
                                            _tcpLastTime = DateTime.UtcNow;
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
                                var signature = new byte[]
                                {
                                    0x00,0x00,0x00,0x62,
                                    0x00,0x03,
                                    0x00,0x00,0x00,0x01,
                                    0x00,0x11,0x45,0x14,
                                    0x00,0x00,0x00,0x00,
                                    0x0a,0x4e,0x08,0x01,0x22,0x24
                                };
                                bool check1 = appPayload.Take(10).SequenceEqual(signature.Take(10));
                                bool check2 = appPayload.Skip(14).Take(6).SequenceEqual(signature.Skip(14).Take(6));
                                if (check1 && check2)
                                {
                                    if (_currentServer != srcServer)
                                    {
                                        _currentServer = srcServer;
                                        ClearTcpCacheInternal();
                                        _tcpNextSeq = unchecked(seqNo + (uint)appPayload.Length);
                                        _tcpNextSeqInitialized = true;
                                        _tcpLastTime = DateTime.UtcNow;
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
                                var data = appPayload.Skip(10).ToArray();
                                if (data.Length > 0)
                                {
                                    int idx = 0;
                                    while (idx + 4 <= data.Length)
                                    {
                                        var len = (int)ReadUint32BE(data, idx);
                                        idx += 4;
                                        if (len - 4 <= 0 || idx + (len - 4) > data.Length) break;
                                        var data1 = data.Skip(idx).Take(len - 4).ToArray();
                                        idx += len - 4;

                                        var signature = new byte[] { 0x00, 0x06, 0x26, 0xad, 0x66, 0x00 };
                                        if (data1.Length < 5 + signature.Length) break;
                                        bool eq = data1.Skip(5).Take(signature.Length).SequenceEqual(signature);
                                        if (!eq) break;
                                        if (_currentServer != srcServerRe)
                                        {
                                            _currentServer = srcServerRe;
                                            ClearTcpCacheInternal();
                                            _tcpNextSeq = ackNo; // note: server.js uses ackno here
                                            _tcpNextSeqInitialized = true;
                                            _tcpLastTime = DateTime.UtcNow;
                                            OnLogStatus?.Invoke($"Got Scene Server Address by FrameUp Notify Packet: {_currentServer}");
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* ignore */ }

                        // If we've just detected server we return (mirror JS branch that returns early)
                        if (!string.IsNullOrEmpty(_currentServer))
                        {
                            return;
                        }
                    } // end current_server detection block

                    // At this point we are processing packets for the known server (or haven't detected but continue)
                    // If tcp_next_seq uninitialized, attempt to set it heuristically (mirrors JS behavior)
                    if (!_tcpNextSeqInitialized)
                    {
                        if (appPayloadLen > 4)
                        {
                            uint maybeSize = ReadUint32BE(appPayload, 0);
                            if (maybeSize < 0x0FFFFF)
                            {
                                _tcpNextSeq = seqNo;
                                _tcpNextSeqInitialized = true;
                            }
                            else
                            {
                                OnLogStatus?.Invoke("Unexpected TCP capture: packet size header invalid and tcp_next_seq not initialized");
                            }
                        }
                        else
                        {
                            // can't init yet
                        }
                    }

                    // store chunk in tcp cache if seq is >= expected next seq or next seq not initialized
                    if (!_tcpNextSeqInitialized || seqNo >= _tcpNextSeq)
                    {
                        // store the application payload bytes (buf)
                        _tcpCache[seqNo] = appPayload;
                    }

                    // try to append contiguous fragments from cache
                    while (_tcpNextSeqInitialized && _tcpCache.TryGetValue(_tcpNextSeq, out var cachedTcpData))
                    {
                        if (cachedTcpData != null && cachedTcpData.Length > 0)
                        {
                            _data.AddRange(cachedTcpData);
                            _tcpLastTime = DateTime.UtcNow;
                        }
                        // advance next seq with wrapping (uint32)
                        _tcpCache.Remove(_tcpNextSeq);
                        _tcpNextSeq = unchecked(_tcpNextSeq + (uint)(cachedTcpData?.Length ?? 0));
                    }

                    // Now try to extract complete length-prefixed packets from _data (mirror server.js logic)
                    while (_data.Count > 4)
                    {
                        uint packetSize = ReadUint32BE(_data, 0);
                        if (packetSize == 0 || packetSize > 0x0FFFFF)
                        {
                            OnLogStatus?.Invoke($"Invalid Length!! _data.Count={_data.Count}, packetSize={packetSize}");
                            // For safety, clear and bail (server.js process.exit in extreme cases)
                            ClearTcpCacheInternal();
                            return;
                        }
                        if (_data.Count < packetSize) break;

                        var pkt = _data.GetRange(0, (int)packetSize).ToArray();
                        _data.RemoveRange(0, (int)packetSize);

                        // Instead of directly invoking OnPayloadReady with the assembled length-prefixed packet,
                        // further process it like JS processPacket and stop right before _processNotifyMsg's switch.
                        ProcessPacketStream(pkt);
                    }
                } // end tcp lock
            }
            catch (Exception ex)
            {
                OnLogStatus?.Invoke($"Error processing packet: {ex.Message}");
            }
        }
    }
}