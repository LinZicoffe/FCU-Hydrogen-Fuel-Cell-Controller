using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>跟踪真实发出的0x01/0x07/0x0A查询；RX线程可完成请求，UI线程负责超时和清理。</summary>
    public sealed class OrdinaryQueryTracker
    {
        private readonly ConcurrentDictionary<(byte Address, byte Function, ushort Id, byte SubIndex), long> _pending = new();
        private readonly Func<long> _clock;
        private long _quietUntil;
        public bool IsBusy => !_pending.IsEmpty;
        public bool MayHaveLateReply => _clock() < Interlocked.Read(ref _quietUntil);

        public OrdinaryQueryTracker(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

        private static bool TryKey(byte[] frame, out (byte Address, byte Function, ushort Id, byte SubIndex) key)
        {
            key = default;
            if (frame.Length < 7 || frame[0] != 0xAA || frame[1] != 0x55) return false;
            if (FirmwareMainProtocol.IsResponse(frame, frame[2], 1, 0))
            { key = (frame[2], 1, 0, 0); return true; }
            if (FirmwareMainProtocol.IsResponse(frame, frame[2], 7, 0))
            { key = (frame[2], 7, 0, 0); return true; }
            if (FirmwareMainProtocol.IsResponse(frame, frame[2], 10, 3))
            { key = (frame[2], 10, (ushort)((frame[5] << 8) | frame[6]), frame[7]); return true; }
            return false;
        }

        public bool Blocks(byte[] frame) => TryKey(frame, out var key) && _pending.ContainsKey(key);

        public void Sent(byte[] frame)
        {
            if (TryKey(frame, out var key)) _pending.TryAdd(key, _clock());
        }

        public bool Receive(byte[] frame)
        {
            if (frame.Length < 7) return false;
            if (FirmwareMainProtocol.IsResponse(frame, frame[2], 0x81, 12))
                return _pending.TryRemove((frame[2], 1, 0, 0), out _);
            if (ResponseParser.TryParseStackInfo(frame, out _))
                return _pending.TryRemove((frame[2], 7, 0, 0), out _);
            else if (frame.Length >= 10 && frame[3] == 0x8A &&
                FirmwareMainProtocol.IsResponse(frame, frame[2], 0x8A, frame[4]))
            {
                ushort id = (ushort)((frame[5] << 8) | frame[6]);
                return _pending.TryRemove((frame[2], 10, id, frame[7]), out _);
            }
            return false;
        }

        public bool Expire()
        {
            bool expired = false;
            foreach (var item in _pending.ToArray())
            {
                if (_clock() - item.Value >= 3000 && _pending.TryRemove(item.Key, out _)) expired = true;
            }
            if (expired) Interlocked.Exchange(ref _quietUntil, _clock() + 3000);
            return expired;
        }

        public void Reset()
        {
            if (IsBusy) Interlocked.Exchange(ref _quietUntil, _clock() + 3000);
            _pending.Clear();
        }
    }
}
