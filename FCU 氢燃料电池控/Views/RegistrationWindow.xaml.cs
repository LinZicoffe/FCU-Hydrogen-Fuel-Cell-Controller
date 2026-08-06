using System.Windows;
using FCU_氢燃料电池控.Models;
using FCU_氢燃料电池控.Services;

namespace FCU_氢燃料电池控.Views
{
    public partial class RegistrationWindow : Window
    {
        private readonly RegistrationService _registrationService;

        public RegistrationData? Registration { get; private set; }

        public RegistrationWindow(RegistrationService registrationService)
        {
            InitializeComponent();
            _registrationService = registrationService;
            Loaded += (_, _) => CompanyNameTextBox.Focus();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var result = _registrationService.Register(CompanyNameTextBox.Text);
            if (!result.Success)
            {
                MessageBox.Show(result.Message, "注册提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                CompanyNameTextBox.Focus();
                return;
            }

            Registration = result.Registration;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
