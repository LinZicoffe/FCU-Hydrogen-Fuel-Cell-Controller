using System;
using System.Windows.Media;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.Services;
using HC = HandyControl.Controls;

namespace FCU_氢燃料电池控
{
    public partial class MainWindow
    {
        private ParameterWriteSession? _parameterWrite;
        private OrdinaryQueryTracker? _ordinaryQueries;
        private long _lastParameterReadAt = -3001;

        private bool CanSendParameterWrite() => !_awaitingStackInfo && _bmsCache?.Waiting != true &&
            _ordinaryQueries?.IsBusy != true && _ordinaryQueries?.MayHaveLateReply != true &&
            _bmsService?.IsBusy != true && Environment.TickCount64 - _lastParameterReadAt >= 350 &&
            Environment.TickCount64 - _lastSharedSetAt >= 3000;

        private bool BeginParameterWrite(byte function, byte[] data, string name)
        {
            if (_parameterWrite == null) return false;
            if (_parameterWrite.IsBusy)
            {
                HC.Growl.Warning("已有参数写入正在等待确认或回读，请等待当前结果");
                return false;
            }
            if (!_isSerialOpen || _blUpgradeMode || _upgradePrecheckMode || FirmwareUpgradeView.IsBusy)
                return RejectParameterWrite("参数未发送：请先打开串口并等待固件升级结束");
            if (_standbyPeriod?.IsBusy == true || _currentCalibration?.IsBusy == true ||
                _ordinaryQueries?.MayHaveLateReply == true ||
                _bmsBusExchangeInProgress || _bmsReturnInFlight || _pendingLoadManualCmd != null ||
                _pendingValveTimingSet.HasValue || _pendingSocWrite != null || _pendingSocReadDeviceAddress.HasValue ||
                _pendingFanManualEnable.HasValue || _pendingSecondMosWrite != null || _diagProbePending)
                return RejectParameterWrite("参数未发送：其他控制指令正在等待响应，请稍后重试");
            if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte address, showError: false))
                return RejectParameterWrite("参数未发送：设备地址无效");
            try
            {
                bool accepted = _parameterWrite.Write(address, function, data, name);
                RenderParameterWrite();
                UpdateStatus(_parameterWrite.Status);
                return accepted;
            }
            catch (Exception ex)
            {
                return RejectParameterWrite($"{name}未发送：{ex.Message}");
            }
        }

        private bool RejectParameterWrite(string message)
        {
            _parameterWrite?.ReportRejection(message);
            RenderParameterWrite();
            UpdateStatus(message);
            HC.Growl.Warning(message);
            return false;
        }

        private void RenderParameterWrite()
        {
            if (_parameterWrite == null || tbk_ParameterWriteStatus == null) return;
            tbk_ParameterWriteStatus.Text = "普通参数写入：" + _parameterWrite.Status;
            tbk_ParameterWriteStatus.Foreground = _parameterWrite.Verified ? Brushes.ForestGreen :
                _parameterWrite.IsBusy ? Brushes.RoyalBlue : Brushes.DarkOrange;
            ushort? id = _parameterWrite.IsBusy ? _parameterWrite.ParameterId : null;
            grp_PressureParameters.IsEnabled = id != 0x0101;
            grp_FanParameters.IsEnabled = id != 0x0401;
            grp_ValveParameters.IsEnabled = id is not (0x0201 or 0x0601);
            grp_DcdcParameters.IsEnabled = id != 0x0301;
            grp_LoadSequences.IsEnabled = grp_RecipeParameters.IsEnabled = id != 0x0501;
            grp_LoadControlParameters.IsEnabled = id is not (0x0502 or 0x0503);
            grp_GeneralParameters.IsEnabled = id != 0x0901;
        }

        private void TickOrdinaryQueries()
        {
            if (_ordinaryQueries?.Expire() != true) return;
            _awaitingStackInfo = false;
            _parameterWrite?.CancelIfDraining("在途查询响应超时，请检查链路后重试");
            RenderParameterWrite();
        }

        private void ReceiveParameterWrite(byte[] frame, int epoch)
        {
            if (_parameterWrite == null || _parameterWrite.Epoch != epoch) return;
            _ordinaryQueries?.Receive(frame);
            if (_parameterWrite.Receive(frame))
            {
                if (_parameterWrite.Verified && _parameterWrite.ParameterId == 0x0101)
                    _pressureParamsLoadedFromDevice = true;
                UpdateStatus(_parameterWrite.Status);
            }
            RenderParameterWrite();
        }
    }
}
