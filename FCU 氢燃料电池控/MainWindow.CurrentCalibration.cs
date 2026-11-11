using System;
using System.Globalization;
using System.Windows;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;

namespace FCU_氢燃料电池控
{
    public partial class MainWindow
    {
        private CurrentCalibrationSession? _currentCalibration;

        private void BeginCurrentCalibration(bool write)
        {
            if (_currentCalibration == null) return;
            void Reject(string reason) { _currentCalibration.ReportRejection(reason); RenderCurrentCalibration(); }
            if (!_isSerialOpen || _blUpgradeMode || _upgradePrecheckMode || FirmwareUpgradeView.IsBusy)
            { Reject("请先连接FCU并等待固件升级操作结束"); return; }
            if (_currentCalibration.IsBusy || _standbyPeriod?.IsBusy == true || _parameterWrite?.IsBusy == true ||
                (write && _currentCalibration.MayHaveLateAck) || _standbyPeriod?.MayHaveLateAck == true ||
                _parameterWrite?.MayHaveLateAck == true || _bmsCache?.Waiting == true ||
                _ordinaryQueries?.IsBusy == true || _ordinaryQueries?.MayHaveLateReply == true ||
                _awaitingStackInfo || _bmsService?.IsBusy == true || _bmsBusExchangeInProgress || _bmsReturnInFlight ||
                ManualControlBusy || ManualControlQuiet || _pendingSocWrite != null ||
                _pendingSocReadDeviceAddress.HasValue || _pendingFanManualEnable.HasValue || _pendingSecondMosWrite != null ||
                _diagProbePending || Environment.TickCount64 - _lastSharedSetAt < 3000)
            { Reject("其他指令正在等待响应，请稍候再操作输入拉载标定"); return; }
            if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte address, showError: false))
            { Reject("FCU设备地址无效"); return; }
            CurrentCalibration? values = null;
            if (write)
            {
                if (!float.TryParse(tbx_CurrentCalibrationK.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float k) ||
                    !float.TryParse(tbx_CurrentCalibrationB.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float b) ||
                    !float.IsFinite(k) || k <= 0 || !float.IsFinite(b))
                { Reject("k须为有限正数，b须为有限数（单位A）；请用小数点输入，支持科学计数法"); return; }
                values = new CurrentCalibration(k, b);
            }
            // 标定期间暂停周期Modbus任务；底层租约同时拦截其他普通发送。
            _bmsService?.StopPolling();
            btn_StartBMSPolling.IsEnabled = true;
            btn_StopBMSPolling.IsEnabled = false;
            cbx_BMSPollingInterval.IsEnabled = true;
            if (write) _currentCalibration.Write(address, values!);
            else _currentCalibration.Read(address);
            RenderCurrentCalibration();
        }

        private void btn_ReadCurrentCalibration_Click(object sender, RoutedEventArgs e) => BeginCurrentCalibration(false);
        private void btn_WriteCurrentCalibration_Click(object sender, RoutedEventArgs e) => BeginCurrentCalibration(true);

        private void RenderCurrentCalibration()
        {
            if (_currentCalibration == null) return;
            tbk_CurrentCalibrationStatus.Text = _currentCalibration.Status;
            bool editable = !_currentCalibration.IsBusy;
            btn_ReadCurrentCalibration.IsEnabled = editable;
            btn_WriteCurrentCalibration.IsEnabled = editable;
            tbx_CurrentCalibrationK.IsEnabled = editable;
            tbx_CurrentCalibrationB.IsEnabled = editable;
        }

        private void ReceiveCurrentCalibration(byte[] frame, int epoch)
        {
            if (_currentCalibration == null || _currentCalibration.Epoch != epoch) return;
            if (_currentCalibration.Receive(frame) && _currentCalibration.Actual is { } values)
            {
                // G9保证float32回读后再写不会因显示精度降低而改变系数。
                tbx_CurrentCalibrationK.Text = values.K.ToString("G9", CultureInfo.InvariantCulture);
                tbx_CurrentCalibrationB.Text = values.B.ToString("G9", CultureInfo.InvariantCulture);
            }
            RenderCurrentCalibration();
        }
    }
}
