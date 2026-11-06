using System;
using System.Windows.Media;
using FCU_氢燃料电池控.Protocol;
using HC = HandyControl.Controls;

namespace FCU_氢燃料电池控
{
    public partial class MainWindow
    {
        private sealed record ManualControlRequest(byte Address, int Epoch, bool? FanEnable, byte FanDuty, string? LoadCommand);
        private volatile ManualControlRequest? _manualControlRequest;
        private long _manualControlQuietUntil;
        private byte[]? _ownedManualControlFrame;
        private bool ManualControlBusy => _pendingFanManualEnable.HasValue || _pendingLoadManualCmd != null;
        private bool ManualControlQuiet => Environment.TickCount64 < _manualControlQuietUntil;

        private bool CanStartManualControl()
        {
            if (ManualControlBusy || ManualControlQuiet || _blUpgradeMode || _upgradePrecheckMode ||
                FirmwareUpgradeView.IsBusy || _parameterWrite?.IsBusy == true || _parameterWrite?.MayHaveLateAck == true ||
                _standbyPeriod?.IsBusy == true || _standbyPeriod?.MayHaveLateAck == true ||
                _currentCalibration?.IsBusy == true || _currentCalibration?.MayHaveLateAck == true ||
                _ordinaryQueries?.IsBusy == true || _ordinaryQueries?.MayHaveLateReply == true ||
                _bmsCache?.Waiting == true || _bmsService?.IsBusy == true || _bmsBusExchangeInProgress ||
                _bmsReturnInFlight || _pendingSocWrite != null || _pendingSocReadDeviceAddress.HasValue ||
                _pendingSecondMosWrite != null || _diagProbePending || Environment.TickCount64 - _lastSharedSetAt < 3000)
            {
                HC.Growl.Warning("其他指令正在等待确认或迟到应答尚未排空，请稍候再发送手动控制");
                return false;
            }
            return true;
        }

        private bool BlocksManualControlFrame(byte[] frame)
        {
            if (ReferenceEquals(frame, _ownedManualControlFrame)) return false;
            bool appFrame = frame.Length >= 7 && frame[0] == 0xAA && frame[1] == 0x55;
            if (!ManualControlBusy)
                return ManualControlQuiet && appFrame && frame[3] is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x0B or 0x0E or 0x0F;
            // 安全停机及归还BMS轮询使用独立应答，不受普通控制独占限制。
            return !(appFrame && (frame[3] == 0x09 ||
                (frame.Length == 8 && frame[3] == 0x0D && frame[5] == 1)));
        }

        private bool SendManualControlFrame(byte[] frame)
        {
            // 一次发布完整请求，RX不能把旧请求的字段与新请求的编号拼在一起。
            _manualControlRequest = new ManualControlRequest(frame[2], _monitoringEpoch,
                _pendingFanManualEnable, _pendingFanManualDuty, _pendingLoadManualCmd);
            _ownedManualControlFrame = frame;
            try
            {
                if (SendFrame(frame)) return true;
                EndManualControlUnknown();
                return false;
            }
            finally { _ownedManualControlFrame = null; }
        }

        private bool IsCurrentManualResponse(byte[] frame, byte function, ManualControlRequest request) =>
            ReferenceEquals(request, _manualControlRequest) && IsCurrentTelemetry(frame, request.Epoch) &&
            FirmwareMainProtocol.IsResponse(frame, request.Address, function, 1);

        private void EndManualControlUnknown()
        {
            _manualControlQuietUntil = Environment.TickCount64 + 3000;
            _manualControlRequest = null;
            _pendingFanManualEnable = null;
            _pendingLoadManualCmd = null;
            StopFanManualResponseTimeout();
            StopLoadManualResponseTimeout();
        }

        private void ResetManualControls()
        {
            if (ManualControlBusy) EndManualControlUnknown();
            _manualControlRequest = null;
            _pendingFanManualEnable = null;
            _pendingLoadManualCmd = null;
            StopFanManualResponseTimeout();
            StopLoadManualResponseTimeout();
            tbk_FanManualMode.Text = "当前模式未知，等待当前设备控制确认";
            tbk_FanManualMode.Foreground = Brushes.Gray;
        }
    }
}
