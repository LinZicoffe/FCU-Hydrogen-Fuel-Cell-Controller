using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;
using FCU_氢燃料电池控.Utils;

namespace FCU_氢燃料电池控.Views
{
    /// <summary>
    /// LicenseWindow.xaml 的交互逻辑
    /// </summary>
    public partial class LicenseWindow : Window
    {
        #region 私有字段

        private readonly LicenseService _licenseService;
        private LicenseValidationResult? _validationResult;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public LicenseWindow()
        {
            InitializeComponent();
            _licenseService = new LicenseService();
            Loaded += LicenseWindow_Loaded;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 窗口加载事件
        /// </summary>
        private async void LicenseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshLicenseStatusAsync();
        }

        /// <summary>
        /// 复制硬件信息按钮点击
        /// </summary>
        private void BtnCopyHardwareInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hardwareInfo = _licenseService.ExportHardwareInfo();
                Clipboard.SetText(hardwareInfo);
                MessageBox.Show("硬件信息已复制到剪贴板", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 浏览许可证文件按钮点击
        /// </summary>
        private void BtnBrowseLicense_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "选择许可证文件",
                Filter = "许可证文件 (*.license)|*.license|所有文件 (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                txtLicensePath.Text = openFileDialog.FileName;
            }
        }

        /// <summary>
        /// 激活按钮点击
        /// </summary>
        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var licensePath = txtLicensePath.Text;
                if (string.IsNullOrEmpty(licensePath) || !File.Exists(licensePath))
                {
                    MessageBox.Show("请选择有效的许可证文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 禁用按钮，防止重复点击
                btnActivate.IsEnabled = false;
                btnActivate.Content = "激活中...";

                var (success, message) = await _licenseService.ImportLicenseFileAsync(licensePath);

                if (success)
                {
                    MessageBox.Show(message, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshLicenseStatusAsync();
                    txtLicensePath.Clear();
                }
                else
                {
                    MessageBox.Show(message, "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"激活失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnActivate.IsEnabled = true;
                btnActivate.Content = "激活许可证";
            }
        }

        /// <summary>
        /// 关闭按钮点击
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 刷新许可证状态
        /// </summary>
        private async System.Threading.Tasks.Task RefreshLicenseStatusAsync()
        {
            try
            {
                _validationResult = await _licenseService.ValidateLicenseAsync();

                // 更新状态显示
                UpdateStatusDisplay();

                // 更新硬件信息
                txtHardwareInfo.Text = _licenseService.ExportHardwareInfo();

                // 更新许可证详情
                UpdateLicenseDetails();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取许可证状态失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void UpdateStatusDisplay()
        {
            if (_validationResult == null) return;

            // 隐藏错误消息
            txtErrorMessage.Visibility = Visibility.Collapsed;

            switch (_validationResult.Status)
            {
                case LicenseStatus.NotActivated:
                    txtStatus.Text = "未激活";
                    txtStatus.Foreground = new SolidColorBrush(Colors.Orange);
                    txtRemainingDays.Text = $"您可以试用 {LicenseService.TrialDays} 天";
                    break;

                case LicenseStatus.TrialActive:
                    txtStatus.Text = "试用中";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    txtRemainingDays.Text = $"剩余 {_validationResult.RemainingDays} 天";

                    if (_validationResult.RemainingDays <= 7)
                    {
                        txtRemainingDays.Foreground = new SolidColorBrush(Colors.Orange);
                        txtErrorMessage.Text = "试用期即将结束，请尽快购买许可证";
                        txtErrorMessage.Visibility = Visibility.Visible;
                    }
                    break;

                case LicenseStatus.TrialExpired:
                    txtStatus.Text = "试用期已结束";
                    txtStatus.Foreground = new SolidColorBrush(Colors.Red);
                    txtRemainingDays.Text = "";
                    txtErrorMessage.Text = _validationResult.ErrorMessage;
                    txtErrorMessage.Visibility = Visibility.Visible;
                    break;

                case LicenseStatus.Activated:
                    txtStatus.Text = "已激活";
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    if (_validationResult.RemainingDays == -1)
                    {
                        txtRemainingDays.Text = "永久授权";
                    }
                    else
                    {
                        txtRemainingDays.Text = $"剩余 {_validationResult.RemainingDays} 天";
                    }
                    break;

                case LicenseStatus.LicenseExpired:
                    txtStatus.Text = "许可证已过期";
                    txtStatus.Foreground = new SolidColorBrush(Colors.Red);
                    txtRemainingDays.Text = "";
                    txtErrorMessage.Text = _validationResult.ErrorMessage;
                    txtErrorMessage.Visibility = Visibility.Visible;
                    break;

                default:
                    txtStatus.Text = "许可证状态异常";
                    txtStatus.Foreground = new SolidColorBrush(Colors.Red);
                    txtRemainingDays.Text = "";
                    txtErrorMessage.Text = _validationResult.ErrorMessage;
                    txtErrorMessage.Visibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// 更新许可证详情
        /// </summary>
        private void UpdateLicenseDetails()
        {
            licenseDetailsPanel.Children.Clear();

            if (_validationResult?.License == null)
            {
                var text = new TextBlock
                {
                    Text = "未激活许可证",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                licenseDetailsPanel.Children.Add(text);
                return;
            }

            var license = _validationResult.License;
            var details = new[]
            {
                ("客户名称", license.CustomerName),
                ("许可证类型", GetLicenseTypeName(license.LicenseType)),
                ("产品版本", license.ProductVersion),
                ("硬件指纹", license.HardwareId.Length > 32 ? license.HardwareId.Substring(0, 32) + "..." : license.HardwareId),
                ("颁发时间", DateTimeOffset.FromUnixTimeSeconds(license.IssuedAt).ToString("yyyy-MM-dd HH:mm:ss")),
                ("过期时间", license.ExpiresAt == 0 ? "永久授权" : DateTimeOffset.FromUnixTimeSeconds(license.ExpiresAt).ToString("yyyy-MM-dd HH:mm:ss")),
                ("许可证ID", license.LicenseId)
            };

            foreach (var (label, value) in details)
            {
                var grid = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());

                var labelControl = new TextBlock
                {
                    Text = label + ":",
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
                };
                Grid.SetColumn(labelControl, 0);

                var valueControl = new TextBlock
                {
                    Text = value,
                    Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102))
                };
                Grid.SetColumn(valueControl, 1);

                grid.Children.Add(labelControl);
                grid.Children.Add(valueControl);
                licenseDetailsPanel.Children.Add(grid);
            }
        }

        /// <summary>
        /// 获取许可证类型名称
        /// </summary>
        private string GetLicenseTypeName(LicenseType licenseType)
        {
            return licenseType switch
            {
                LicenseType.Trial => "试用版",
                LicenseType.Standard => "标准版",
                LicenseType.Professional => "专业版",
                LicenseType.Enterprise => "企业版",
                _ => "未知"
            };
        }

        #endregion
    }
}
