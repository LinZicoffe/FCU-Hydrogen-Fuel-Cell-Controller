using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.Views;

namespace FCU_氢燃料电池控
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        #region 私有字段

        private LicenseService? _licenseService;

        #endregion

        #region 应用程序启动

        /// <summary>
        /// 应用程序启动事件
        /// </summary>
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // 初始化许可证服务
                _licenseService = new LicenseService();

                // 验证许可证
                var validationResult = await _licenseService.ValidateLicenseAsync();

                // 根据验证结果决定启动流程
                if (!HandleLicenseValidation(validationResult))
                {
                    // 许可证验证失败，退出应用
                    Shutdown();
                    return;
                }

                // 更新最后运行时间
                _licenseService.UpdateLastRunTime();

                // 启动主窗口
                var mainWindow = new MainWindow(_licenseService);
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用程序启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 处理许可证验证结果
        /// </summary>
        private bool HandleLicenseValidation(LicenseValidationResult validationResult)
        {
            if (_licenseService == null)
                return false;

            switch (validationResult.Status)
            {
                case LicenseStatus.NotActivated:
                    // 首次运行，初始化试用期并显示欢迎窗口
                    _licenseService.InitializeTrial();
                    return ShowWelcomeWindow();

                case LicenseStatus.TrialActive:
                    // 试用期内，检查是否需要警告
                    if (validationResult.RemainingDays <= 7)
                    {
                        ShowTrialWarning(validationResult.RemainingDays);
                    }
                    return true;

                case LicenseStatus.TrialExpired:
                    // 试用期结束，显示激活窗口
                    ShowLicenseActivationWindow("试用期已结束，请购买许可证激活软件");
                    return false;

                case LicenseStatus.Activated:
                    // 已激活，正常启动
                    return true;

                case LicenseStatus.LicenseExpired:
                    // 许可证过期
                    ShowLicenseActivationWindow("许可证已过期，请续费");
                    return false;

                case LicenseStatus.HardwareMismatch:
                    // 硬件不匹配
                    MessageBox.Show("许可证与当前硬件不匹配，请联系技术支持", "许可证错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;

                case LicenseStatus.InvalidSignature:
                    // 签名无效
                    MessageBox.Show("许可证签名无效，可能是被篡改，请重新获取许可证", "许可证错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;

                case LicenseStatus.TimeRollbackDetected:
                    // 时间回退检测
                    MessageBox.Show("检测到系统时间被回退，试用已失效，请联系技术支持", "安全警告", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 显示欢迎窗口
        /// </summary>
        private bool ShowWelcomeWindow()
        {
            var message = $"欢迎使用 FCU 氢燃料电池控制系统！\n\n" +
                         $"您有 {LicenseService.TrialDays} 天的免费试用期。\n" +
                         $"试用期内您可以完整体验所有功能。\n\n" +
                         $"点击\"确定\"开始试用，或点击\"取消\"激活许可证。";

            var result = MessageBox.Show(message, "欢迎使用", MessageBoxButton.OKCancel, MessageBoxImage.Information);

            if (result == MessageBoxResult.Cancel)
            {
                ShowLicenseActivationWindow();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 显示试用警告
        /// </summary>
        private void ShowTrialWarning(int remainingDays)
        {
            var message = remainingDays == 1
                ? "试用期将于明天结束，请尽快购买许可证"
                : $"试用期还剩 {remainingDays} 天，请尽快购买许可证";

            MessageBox.Show(message, "试用提醒", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 显示许可证激活窗口
        /// </summary>
        private void ShowLicenseActivationWindow(string? message = null)
        {
            var licenseWindow = new LicenseWindow();

            if (!string.IsNullOrEmpty(message))
            {
                MessageBox.Show(message, "许可证提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            licenseWindow.ShowDialog();
        }

        #endregion
    }
}
