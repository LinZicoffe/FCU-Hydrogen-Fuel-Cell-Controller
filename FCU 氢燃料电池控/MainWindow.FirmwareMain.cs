using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using FCU_氢燃料电池控.Helpers;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;
using HC = HandyControl.Controls;

namespace FCU_氢燃料电池控
{
    public partial class MainWindow
    {
        private BmsCacheSession? _bmsCache;
        private DispatcherTimer? _firmwareMainTimer;
        private bool _cacheAutoRefresh;
        private long _nextCacheRead;
        private bool _changingBmsMode;
        private bool _bmsBusMayBeOwned;
        private byte _bmsBusOwnerAddress;
        private string? _bmsBusOwnerPort;
        private byte _bmsExchangeAddress;
        private int _bmsExchangeGeneration;
        private StandbyPeriodSession? _standbyPeriod;
        private long _lastSharedSetAt = -3001;
        private bool DirectBmsMode => chk_DirectBmsMode.IsChecked == true;

        private void CancelBmsTakeover()
        {
            _bmsExchangeGeneration++;
            _bmsTakeoverConfirmed = false;
        }

        private void InitializeFirmwareMain()
        {
            _bmsCache = new BmsCacheSession(SendFrame);
            _standbyPeriod = new StandbyPeriodSession(SendFrame);
            _viewModel!.GetSerialPortManager().SendBlocked = frame => _standbyPeriod.Blocks(frame);
            _firmwareMainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _firmwareMainTimer.Tick += (_, _) =>
            {
                _bmsCache.Tick();
                _standbyPeriod.Tick();
                RenderStandbyPeriod();
                if (!DirectBmsMode)
                {
                    if (_cacheAutoRefresh && Environment.TickCount64 >= _nextCacheRead && CanReadBmsCache())
                    {
                        RequestBmsCache();
                        _nextCacheRead = Environment.TickCount64 + 2000;
                    }
                    RenderBmsCache();
                }
            };
            _firmwareMainTimer.Start();
        }

        private bool CanReadBmsCache() => _isSerialOpen && !_blUpgradeMode && !FirmwareUpgradeView.IsBusy &&
            !DirectBmsMode && !_bmsBusExchangeInProgress && !_bmsReturnInFlight && !_awaitingStackInfo &&
            _bmsService?.IsBusy != true && _bmsCache?.Waiting != true && _standbyPeriod?.IsBusy != true;

        private void RequestBmsCache()
        {
            if (!CanReadBmsCache()) return;
            if (SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte address, showError: false))
                _bmsCache?.Request(address);
            RenderBmsCache();
        }

        private void btn_ReadBmsCache_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSerialOpen) { HC.Growl.Warning("请先打开串口"); return; }
            RequestBmsCache();
        }

        private void chk_BmsCacheAuto_Changed(object sender, RoutedEventArgs e)
        {
            _cacheAutoRefresh = chk_BmsCacheAuto.IsChecked == true;
            _nextCacheRead = 0;
        }

        private void ResetFirmwareMainSession()
        {
            _cacheAutoRefresh = false;
            chk_BmsCacheAuto.IsChecked = false;
            _bmsCache?.Reset();
            _standbyPeriod?.Reset();
            _nextCacheRead = 0;
            RenderBmsCache();
            RenderStandbyPeriod();
        }

        private void RenderStandbyPeriod()
        {
            if (_standbyPeriod == null) return;
            tbk_StandbyPeriodStatus.Text = _standbyPeriod.Status;
            btn_ReadStandbyPeriod.IsEnabled = !_standbyPeriod.IsBusy;
            btn_WriteStandbyPeriod.IsEnabled = !_standbyPeriod.IsBusy;
        }

        private void BeginStandbyPeriod(bool write)
        {
            if (!_isSerialOpen || _blUpgradeMode || FirmwareUpgradeView.IsBusy)
            { ReportStandbyError("请先连接FCU并等待固件升级操作结束"); return; }
            if (_standbyPeriod == null || _standbyPeriod.IsBusy || _bmsCache?.Waiting == true || _awaitingStackInfo ||
                _bmsService?.IsBusy == true || _bmsBusExchangeInProgress || _bmsReturnInFlight ||
                _pendingLoadManualCmd != null || _pendingValveTimingSet.HasValue || _pendingSocWrite != null ||
                _pendingSocReadDeviceAddress.HasValue || _pendingFanManualEnable.HasValue || _pendingSecondMosWrite != null ||
                Environment.TickCount64 - _lastSharedSetAt < 3000)
            { ReportStandbyError("其他指令正在等待响应，请稍候再操作待机周期"); return; }
            if (!SerialPortHelper.TryParseDeviceAddress(GetDeviceAddress(), out byte address, showError: false))
            { ReportStandbyError("FCU设备地址无效"); return; }
            ushort seconds = 0;
            if (write && (!ushort.TryParse(tbx_StandbyPeriod.Text.Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out seconds) || seconds == 0))
            { ReportStandbyError("待机采样周期必须为1～65535的整数（秒），0不能关闭采样"); return; }
            // 直接诊断的周期任务也需暂停，防下一轮在事务期间插入Modbus请求。
            _bmsService?.StopPolling();
            btn_StartBMSPolling.IsEnabled = true;
            btn_StopBMSPolling.IsEnabled = false;
            cbx_BMSPollingInterval.IsEnabled = true;
            if (write) _standbyPeriod.Write(address, seconds);
            else _standbyPeriod.Read(address);
            RenderStandbyPeriod();
        }

        private void btn_ReadStandbyPeriod_Click(object sender, RoutedEventArgs e) => BeginStandbyPeriod(false);
        private void btn_WriteStandbyPeriod_Click(object sender, RoutedEventArgs e) => BeginStandbyPeriod(true);

        private void ReportStandbyError(string reason)
        {
            _standbyPeriod?.ReportRejection(reason);
            tbk_StandbyPeriodStatus.Text = reason;
        }

        private void RenderBmsCache()
        {
            if (_bmsCache == null || DirectBmsMode) return;
            var cache = _bmsCache.Snapshot;
            bool online = _bmsCache.Online;
            tbk_BmsCacheStatus.Text = _isSerialOpen ? _bmsCache.Status : "未连接";
            tbk_BmsCacheSoc.Text = _bmsCache.SocValid ? $"{cache!.Soc}%" : "--（无效）";
            tbk_OverviewBmsSoc.Text = _bmsCache.SocValid ? cache!.Soc.ToString() : "--";
            tbk_BmsCacheVoltage.Text = online ? $"{cache!.Voltage:F2} V" : "--";
            tbk_BmsCacheCurrent.Text = online ? $"{cache!.Current:F2} A（充电正 / 放电负）" : "--";
            tbk_BmsCacheAge.Text = cache == null ? "--" : cache.AgeMs == uint.MaxValue
                ? "从未成功采集" : $"{_bmsCache.EffectiveAgeMs} ms";
            tbk_BmsCacheFlags.Text = cache == null ? "--" :
                $"告警 0x{cache.Alarm:X4} / 保护 0x{cache.Protect:X4} / 故障 0x{cache.Fault:X4} / 系统 0x{cache.SystemStatus:X4}" +
                (online ? "" : "（旧值，仅诊断）");
            tbk_BmsCacheDetails.Text = cache == null ? "--" :
                $"告警：{BmsBitDecoder.DecodeAlarm(cache.Alarm)}\n保护：{BmsBitDecoder.DecodeProtect(cache.Protect)}\n" +
                $"故障：{BmsBitDecoder.DecodeFault(cache.Fault)}\n工作状态：{GetBmsWorkStateText(cache.SystemStatus)}";
            if (!online)
            {
                tbk_LoadBlockReason.Text = "BMS缓存不可用，无法从上位机确认FCU当前的SOC门控和BMS保护状态；请检查固件轮询与链路。";
                bd_LoadBlockBanner.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateLoadBlockBanner(new BmsData { Online = true, Soc = _bmsCache.SocValid ? cache!.Soc : (ushort)0,
                    ProtectStatus = cache!.Protect, SystemStatus = cache.SystemStatus });
                if (!_bmsCache.SocValid)
                {
                    tbk_LoadBlockReason.Text = "BMS在线但SOC无效，SOC门控状态不可确认。 " + tbk_LoadBlockReason.Text;
                    bd_LoadBlockBanner.Visibility = Visibility.Visible;
                }
            }
        }

        private void chk_DirectBmsMode_Changed(object sender, RoutedEventArgs e)
        {
            _bmsExchangeGeneration++; // 即使DoEvents重入被挡，也永久撤销外层接管。
            if (_changingBmsMode || _bmsCache == null) return;
            _changingBmsMode = true;
            try
            {
                ResetFirmwareMainSession();
                _bmsService?.StopPolling();
                ResetBmsDisplay();
                if (_isSerialOpen)
                {
                    if (DirectBmsMode) TakeOverBmsBusWithConfirm();
                    else if (_bmsBusMayBeOwned) OnBeforeSerialClose(); // 复用带确认的归还，不关闭串口
                }
                if (_isSerialOpen && (!DirectBmsMode || !_bmsTakeoverConfirmed))
                {
                    if (_bmsBusMayBeOwned) OnBeforeSerialClose();
                    chk_DirectBmsMode.IsChecked = false;
                }
                pnl_DirectBms.Visibility = DirectBmsMode ? Visibility.Visible : Visibility.Collapsed;
                pnl_BmsCache.Visibility = DirectBmsMode ? Visibility.Collapsed : Visibility.Visible;
                btn_StartBMSPolling.IsEnabled = true;
                btn_StopBMSPolling.IsEnabled = false;
                cbx_BMSPollingInterval.IsEnabled = true;
            }
            finally { _changingBmsMode = false; }
            RenderBmsCache();
        }
    }
}
