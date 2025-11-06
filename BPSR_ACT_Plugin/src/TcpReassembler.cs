using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace BPSR_ACT_Plugin.src
{

    /// <summary>
    /// TcpReassembler hallucinated by GPT-5 mini, it claims it's based on sample code in sharppcap Examples. It's not, there's no such thing. W/e, it works great.
    /// </summary>
    internal sealed class TcpReassembler
    {
        public static Action<string> OnLogStatus;

        private readonly Action<ReadOnlyMemory<byte>> _onReassembledStream;
        private readonly SortedDictionary<uint, ReadOnlyMemory<byte>> _segments = new SortedDictionary<uint, ReadOnlyMemory<byte>>();
        private readonly List<byte> _buffer = new List<byte>();
        private readonly object _lock = new object();
        private uint _nextSeq;
        private bool _initialized;
        private DateTime _lastTime = DateTime.MinValue;
        private readonly TimeSpan _segmentTimeout = TimeSpan.FromSeconds(10);

        public TcpReassembler(Action<ReadOnlyMemory<byte>> onReassembledStream)
        {
            _onReassembledStream = onReassembledStream ?? throw new ArgumentNullException(nameof(onReassembledStream));
        }

        public void Clear()
        {
            lock (_lock)
            {
                _segments.Clear();
                _buffer.Clear();
                _initialized = false;
                _lastTime = DateTime.MinValue;
                _nextSeq = 0;
            }
        }

        public void AddSegment(uint seqNo, ReadOnlySpan<byte> payload)
        {
            // ReadOnlySpan<T> is a value type and cannot be null; check length only.
            if (payload.Length == 0) return;

            lock (_lock)
            {
                // Timeout-based reset to avoid indefinite buffering on stalled connections
                if (_lastTime != DateTime.MinValue && DateTime.UtcNow - _lastTime > _segmentTimeout)
                {
                    _segments.Clear();
                    _buffer.Clear();
                    _initialized = false;
                    _lastTime = DateTime.MinValue;
                    OnLogStatus?.Invoke("TcpReassembler timed out; clearing state.");
                }

                // If not initialized, attempt heuristic init same as prior behavior
                if (!_initialized)
                {
                    if (payload.Length > 4)
                    {
                        try
                        {
                            uint maybeSize = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4));
                            if (maybeSize < 0x0FFFFF)
                            {
                                _nextSeq = seqNo;
                                _initialized = true;
                            }
                            else
                            {
                                // don't initialize yet; store and wait for better candidate
                            }
                        }
                        catch
                        {
                            // ignore read errors
                        }
                    }
                }

                // If not initialized, store and return (we store to allow future init)
                if (!_initialized)
                {
                    // store but don't try to reassemble until initialized
                    _segments[seqNo] = payload.ToArray();
                    _lastTime = DateTime.UtcNow;
                    return;
                }

                // Store incoming segment (copy) - simplified to a single assignment
                _segments[seqNo] = payload.ToArray();

                // Try to append contiguous segments starting from _nextSeq
                bool appendedAny = false;
                while (_segments.TryGetValue(_nextSeq, out var seg))
                {
                    if (!seg.IsEmpty && seg.Length > 0)
                    {
                        _buffer.AddRange(seg.ToArray());
                        appendedAny = true;
                        _lastTime = DateTime.UtcNow;
                    }

                    // advance _nextSeq with wrapping arithmetic
                    _segments.Remove(_nextSeq);
                    _nextSeq = unchecked(_nextSeq + (uint)seg.Length);
                }

                // If we appended, try to extract complete length-prefixed packets
                if (appendedAny)
                {
                    // Process buffer as long as we have at least header (4 bytes)
                    while (_buffer.Count >= 4)
                    {
                        uint packetSize;
                        try
                        {
                            packetSize = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(_buffer.ToArray(), 0, 4));
                        }
                        catch
                        {
                            OnLogStatus?.Invoke("TcpReassembler read error while extracting packetSize");
                            Clear(); // recover
                            return;
                        }

                        if (packetSize == 0 || packetSize > 0x0FFFFF)
                        {
                            OnLogStatus?.Invoke($"Invalid Length!! BufferCount={_buffer.Count}, packetSize={packetSize} - clearing reassembly buffer");
                            Clear(); // corrupt stream: clear and bail
                            return;
                        }

                        if (_buffer.Count < packetSize) break; // wait for more data

                        // extract full packet and pass to processing
                        var pkt = _buffer.GetRange(0, (int)packetSize).ToArray();
                        _buffer.RemoveRange(0, (int)packetSize);

                        try
                        {
                            _onReassembledStream(pkt);
                        }
                        catch (Exception ex)
                        {
                            OnLogStatus?.Invoke($"Error while processing reassembled packet: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
