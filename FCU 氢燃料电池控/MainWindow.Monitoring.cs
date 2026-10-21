using System;
using System.Windows.Media;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.Protocol;

namespace FCU_氢燃料电池控
{
    public partial class MainWindow
    {
        private volatile int _monitoringEpoch;
        private long? _lastStackInfoAt;
        private long? _lastLoadStatusAt;
        private string? _stackInfoError;
        private string? _loadStatusError;
        private long StackDataStaleAfterMs => Math.Max(3000, (long)(_pollingTimer?.Interval.TotalMilliseconds ?? 500) * 3);

        private bool IsCurrentTelemetry(byte[] frame, int epoch) =>
            epoch == _monitoringEpoch && _isSerialOpen && !_blUpgradeMode && !_upgradePrecheckMode &&
            SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte address, showError: false) &&
            frame.Length >= 7 && frame[2] == address;

        private void ResetMonitoring()
        {
            _monitoringEpoch++;
            _lastStackInfoAt = _lastLoadStatusAt = null;
            _stackInfoError = _loadStatusError = null;
            if (_viewModel != null)
            {
                _viewModel.StackState = _viewModel.HighPressure = _viewModel.LowPressure = "--";
                _viewModel.HydrogenValveState = _viewModel.DrainValveState = "--";
                _viewModel.DCDCInputVoltage = _viewModel.DCDCInputCurrent = "--";
                _viewModel.DCDCOutputVoltage = _viewModel.DCDCOutputCurrent = "--";
                _viewModel.FanSpeed = _viewModel.Fan2EstimatedSpeed = "--";
                _viewModel.StackTemperature = _viewModel.FCUTemperature = "--";
                _viewModel.RunTime = _viewModel.FaultCode = "--";
            }
            _currentInputVoltage = _currentInputCurrent = _currentOutputVoltage = _currentOutputCurrent = 0;
            tbk_LoadState.Text = tbk_LoadCurrentSequence.Text = tbk_LoadCurrentCurrent.Text = "--";
            tbk_LoadTargetCurrent.Text = tbk_LoadCurrentVoltage.Text = tbk_LoadCurrentPower.Text = "--";
            tbk_LoadMaxPower.Text = tbk_LoadLockCurrent.Text = "--";
            tbk_LoadState.Foreground = Brushes.Gray;
            RenderMonitoringStatus();
        }

        private void RenderMonitoringStatus()
        {
            if (tbk_StackDataStatus == null || tbk_LoadDataStatus == null) return;
            long now = Environment.TickCount64;
            string Describe(long? at, string? error, long staleAfter)
            {
                if (!_isSerialOpen) return "未连接，等待设备数据";
                if (error != null) return error + "；保留最近有效读数";
                if (!at.HasValue) return "尚未收到当前设备的有效数据";
                long age = Math.Max(0, now - at.Value);
                return age > staleAfter
                    ? $"数据已过期（{age / 1000.0:F1}秒未更新），当前为上次读数"
                    : $"最近有效数据：{age / 1000.0:F1}秒前";
            }
            long stackLimit = StackDataStaleAfterMs;
            tbk_StackDataStatus.Text = Describe(_lastStackInfoAt, _stackInfoError, stackLimit);
            tbk_LoadDataStatus.Text = Describe(_lastLoadStatusAt, _loadStatusError, 3000);
            tbk_StackDataStatus.Foreground = _stackInfoError != null ||
                (_lastStackInfoAt.HasValue && now - _lastStackInfoAt.Value > stackLimit) ? Brushes.DarkOrange : Brushes.Gray;
            tbk_LoadDataStatus.Foreground = _loadStatusError != null ||
                (_lastLoadStatusAt.HasValue && now - _lastLoadStatusAt.Value > 3000) ? Brushes.DarkOrange : Brushes.Gray;
        }
    }
}
