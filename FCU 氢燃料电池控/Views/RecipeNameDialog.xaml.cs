using System.Windows;

namespace FCU_氢燃料电池控.Views
{
    public partial class RecipeNameDialog : HandyControl.Controls.Window
    {
        /// <summary>
        /// 用户输入的文本
        /// </summary>
        public string InputText => tbx_Input.Text.Trim();

        public RecipeNameDialog(string prompt, string defaultValue = "")
        {
            InitializeComponent();
            tbk_Prompt.Text = prompt;
            tbx_Input.Text = defaultValue;
            tbx_Input.Focus();
            tbx_Input.SelectAll();
        }

        private void Btn_OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbx_Input.Text))
            {
                HandyControl.Controls.Growl.Warning("名称不能为空");
                return;
            }
            DialogResult = true;
        }

        private void Btn_Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
