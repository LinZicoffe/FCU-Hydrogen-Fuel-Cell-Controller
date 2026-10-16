using System;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控.Services
{
    /// <summary>UI线程使用；时钟/发送可注入，缓存随本机经过时间失效，停止后不接收迟到应答。</summary>
    public sealed class BmsCacheSession
    {
        private readonly Func<byte[], bool> _send;
        private readonly Func<long> _clock;
        private long _requestedAt;
        private long _receivedAt;
        private byte _address;
        public bool Waiting { get; private set; }
        public int Epoch { get; private set; }
        public BmsCacheSnapshot? Snapshot { get; private set; }
        public string Status { get; private set; } = "未读取";
        public ulong EffectiveAgeMs => Snapshot == null ? ulong.MaxValue :
            (ulong)Snapshot.AgeMs + (ulong)Math.Max(0, _clock() - _receivedAt);
        public bool Online => Snapshot?.Online == true && EffectiveAgeMs <= 6000;
        public bool SocValid => Online && Snapshot?.SocValid == true;

        public BmsCacheSession(Func<byte[], bool> send, Func<long>? clock = null)
        {
            _send = send;
            _clock = clock ?? (() => Environment.TickCount64);
        }

        public bool Request(byte address)
        {
            if (Waiting) return false;
            Epoch++;
            _address = address;
            _requestedAt = _clock();
            Waiting = true;
            Status = "等待FCU缓存响应";
            if (_send(FirmwareMainProtocol.ReadBmsCache(address))) return true;
            Waiting = false;
            Status = "缓存查询发送失败";
            return false;
        }

        public bool Receive(byte[] frame)
        {
            if (!Waiting || !FirmwareMainProtocol.TryParseBmsCache(frame, _address, out var snapshot)) return false;
            Waiting = false;
            Snapshot = snapshot;
            _receivedAt = _clock();
            Status = Online ? (SocValid ? "BMS缓存有效" : "BMS在线，SOC无效") : "BMS缓存无效/离线";
            return true;
        }

        public void Tick()
        {
            if (Waiting && _clock() - _requestedAt >= 2000)
            {
                Waiting = false;
                Snapshot = null;
                Status = "缓存查询超时：检查固件支持、地址及链路；旧固件可选直接诊断";
            }
            else if (Snapshot != null && !Online)
                Status = "BMS缓存已失效，旧值仅供诊断";
        }

        public void Reset()
        {
            Epoch++;
            Waiting = false;
            Snapshot = null;
            Status = "未读取";
        }
    }
}
