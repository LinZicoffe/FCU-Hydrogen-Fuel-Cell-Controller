using System;
using System.Windows;
using System.Windows.Controls;

namespace FCU_氢燃料电池控.Helpers
{
    /// <summary>
    /// UI更新辅助类 - 统一处理跨线程UI更新
    /// 创建日期: 2026-02-03
    /// 用途: 消除重复的Dispatcher.Invoke调用，提供简洁的UI更新API
    /// </summary>
    public static class UIUpdater
    {
        #region TextBox更新

        /// <summary>
        /// 安全更新TextBox文本（自动处理跨线程）
        /// </summary>
        /// <param name="textBox">目标TextBox控件</param>
        /// <param name="value">要显示的值</param>
        /// <param name="defaultValue">默认值（当value为空时显示）</param>
        public static void UpdateTextBox(TextBox? textBox, string value, string defaultValue = "--")
        {
            if (textBox == null) return;

            string displayValue = string.IsNullOrEmpty(value) ? defaultValue : value;

            if (textBox.CheckAccess())
            {
                textBox.Text = displayValue;
            }
            else
            {
                textBox.Dispatcher.Invoke(() => textBox.Text = displayValue);
            }
        }

        /// <summary>
        /// 安全更新TextBox文本（整数值）
        /// </summary>
        /// <param name="textBox">目标TextBox控件</param>
        /// <param name="value">要显示的整数值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBox(TextBox? textBox, int value, string defaultValue = "--")
        {
            UpdateTextBox(textBox, value.ToString(), defaultValue);
        }

        /// <summary>
        /// 安全更新TextBox文本（无符号整数值）
        /// </summary>
        /// <param name="textBox">目标TextBox控件</param>
        /// <param name="value">要显示的无符号整数值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBox(TextBox? textBox, ushort value, string defaultValue = "--")
        {
            UpdateTextBox(textBox, value.ToString(), defaultValue);
        }

        /// <summary>
        /// 安全更新TextBox文本（双精度浮点数）
        /// </summary>
        /// <param name="textBox">目标TextBox控件</param>
        /// <param name="value">要显示的双精度浮点数</param>
        /// <param name="format">格式化字符串（如"F1"表示保留1位小数）</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBox(TextBox? textBox, double value, string format = "F1", string defaultValue = "--")
        {
            UpdateTextBox(textBox, value.ToString(format), defaultValue);
        }

        #endregion

        #region TextBlock更新

        /// <summary>
        /// 安全更新TextBlock文本（自动处理跨线程）
        /// </summary>
        /// <param name="textBlock">目标TextBlock控件</param>
        /// <param name="value">要显示的值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBlock(TextBlock? textBlock, string value, string defaultValue = "--")
        {
            if (textBlock == null) return;

            string displayValue = string.IsNullOrEmpty(value) ? defaultValue : value;

            if (textBlock.CheckAccess())
            {
                textBlock.Text = displayValue;
            }
            else
            {
                textBlock.Dispatcher.Invoke(() => textBlock.Text = displayValue);
            }
        }

        /// <summary>
        /// 安全更新TextBlock文本（整数值）
        /// </summary>
        /// <param name="textBlock">目标TextBlock控件</param>
        /// <param name="value">要显示的整数值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBlock(TextBlock? textBlock, int value, string defaultValue = "--")
        {
            UpdateTextBlock(textBlock, value.ToString(), defaultValue);
        }

        /// <summary>
        /// 安全更新TextBlock文本（无符号整数值）
        /// </summary>
        /// <param name="textBlock">目标TextBlock控件</param>
        /// <param name="value">要显示的无符号整数值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBlock(TextBlock? textBlock, ushort value, string defaultValue = "--")
        {
            UpdateTextBlock(textBlock, value.ToString(), defaultValue);
        }

        /// <summary>
        /// 安全更新TextBlock文本（双精度浮点数）
        /// </summary>
        /// <param name="textBlock">目标TextBlock控件</param>
        /// <param name="value">要显示的双精度浮点数</param>
        /// <param name="format">格式化字符串（如"F1"表示保留1位小数）</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateTextBlock(TextBlock? textBlock, double value, string format = "F1", string defaultValue = "--")
        {
            UpdateTextBlock(textBlock, value.ToString(format), defaultValue);
        }

        #endregion

        #region Label更新

        /// <summary>
        /// 安全更新Label内容（自动处理跨线程）
        /// </summary>
        /// <param name="label">目标Label控件</param>
        /// <param name="value">要显示的值</param>
        /// <param name="defaultValue">默认值</param>
        public static void UpdateLabel(Label? label, string value, string defaultValue = "--")
        {
            if (label == null) return;

            string displayValue = string.IsNullOrEmpty(value) ? defaultValue : value;

            if (label.CheckAccess())
            {
                label.Content = displayValue;
            }
            else
            {
                label.Dispatcher.Invoke(() => label.Content = displayValue);
            }
        }

        #endregion

        #region 状态栏更新

        /// <summary>
        /// 安全更新状态栏文本（自动处理跨线程）
        /// </summary>
        /// <param name="statusBar">状态栏控件</param>
        /// <param name="message">状态消息</param>
        public static void UpdateStatusBar(TextBlock? statusBar, string message)
        {
            if (statusBar == null) return;

            if (statusBar.CheckAccess())
            {
                statusBar.Text = message;
            }
            else
            {
                statusBar.Dispatcher.Invoke(() => statusBar.Text = message);
            }
        }

        #endregion

        #region 控件启用/禁用

        /// <summary>
        /// 安全设置控件启用状态（自动处理跨线程）
        /// </summary>
        /// <param name="control">目标控件</param>
        /// <param name="isEnabled">是否启用</param>
        public static void SetControlEnabled(UIElement? control, bool isEnabled)
        {
            if (control == null) return;

            if (control.CheckAccess())
            {
                control.IsEnabled = isEnabled;
            }
            else
            {
                control.Dispatcher.Invoke(() => control.IsEnabled = isEnabled);
            }
        }

        /// <summary>
        /// 安全设置多个控件的启用状态（自动处理跨线程）
        /// </summary>
        /// <param name="isEnabled">是否启用</param>
        /// <param name="controls">控件数组</param>
        public static void SetControlsEnabled(bool isEnabled, params UIElement?[] controls)
        {
            foreach (var control in controls)
            {
                SetControlEnabled(control, isEnabled);
            }
        }

        #endregion

        #region 条件格式化辅助方法

        /// <summary>
        /// 根据条件更新TextBlock文本和颜色
        /// </summary>
        /// <param name="textBlock">目标TextBlock控件</param>
        /// <param name="value">要显示的值</param>
        /// <param name="isWarning">是否为警告状态</param>
        /// <param name="warningColor">警告颜色（默认为橙色）</param>
        /// <param name="normalColor">正常颜色（默认为黑色）</param>
        public static void UpdateTextBlockWithColor(
            TextBlock? textBlock, 
            string value, 
            bool isWarning,
            System.Windows.Media.Brush? warningColor = null,
            System.Windows.Media.Brush? normalColor = null)
        {
            if (textBlock == null) return;

            warningColor ??= System.Windows.Media.Brushes.Orange;
            normalColor ??= System.Windows.Media.Brushes.Black;

            if (textBlock.CheckAccess())
            {
                textBlock.Text = value;
                textBlock.Foreground = isWarning ? warningColor : normalColor;
            }
            else
            {
                textBlock.Dispatcher.Invoke(() =>
                {
                    textBlock.Text = value;
                    textBlock.Foreground = isWarning ? warningColor : normalColor;
                });
            }
        }

        #endregion

        #region 批量清空

        /// <summary>
        /// 批量清空TextBox内容（设置为默认值）
        /// </summary>
        /// <param name="defaultValue">默认值</param>
        /// <param name="textBoxes">TextBox数组</param>
        public static void ClearTextBoxes(string defaultValue = "--", params TextBox?[] textBoxes)
        {
            foreach (var textBox in textBoxes)
            {
                UpdateTextBox(textBox, defaultValue);
            }
        }

        /// <summary>
        /// 批量清空TextBlock内容（设置为默认值）
        /// </summary>
        /// <param name="defaultValue">默认值</param>
        /// <param name="textBlocks">TextBlock数组</param>
        public static void ClearTextBlocks(string defaultValue = "--", params TextBlock?[] textBlocks)
        {
            foreach (var textBlock in textBlocks)
            {
                UpdateTextBlock(textBlock, defaultValue);
            }
        }

        #endregion
    }
}
