using System;
using System.Windows;

namespace FCU_氢燃料电池控.Helpers
{
    /// <summary>
    /// 串口操作辅助类 - 统一处理串口相关的常见操作
    /// 创建日期: 2026-02-03
    /// 用途: 消除重复的串口检查和错误提示代码
    /// </summary>
    public static class SerialPortHelper
    {
        #region 串口状态检查

        /// <summary>
        /// 检查串口是否打开，如果未打开则显示提示消息
        /// </summary>
        /// <param name="isSerialOpen">串口是否打开的状态</param>
        /// <param name="showMessage">是否显示提示消息（默认为true）</param>
        /// <returns>如果串口已打开返回true，否则返回false</returns>
        public static bool CheckSerialPortOpen(bool isSerialOpen, bool showMessage = true)
        {
            if (!isSerialOpen && showMessage)
            {
                MessageBox.Show(
                    "请先打开串口", 
                    "提示", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
            return isSerialOpen;
        }

        /// <summary>
        /// 检查串口是否打开（不显示消息，仅返回结果）
        /// </summary>
        /// <param name="isSerialOpen">串口是否打开的状态</param>
        /// <returns>如果串口已打开返回true，否则返回false</returns>
        public static bool IsSerialPortOpen(bool isSerialOpen)
        {
            return CheckSerialPortOpen(isSerialOpen, showMessage: false);
        }

        #endregion

        #region 错误处理

        /// <summary>
        /// 显示串口操作错误消息
        /// </summary>
        /// <param name="operation">操作名称（如"打开串口"、"发送数据"）</param>
        /// <param name="ex">异常对象</param>
        public static void ShowSerialPortError(string operation, Exception ex)
        {
            MessageBox.Show(
                $"{operation}失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// 显示参数设置错误消息
        /// </summary>
        /// <param name="parameterName">参数名称</param>
        /// <param name="ex">异常对象</param>
        public static void ShowParameterSetError(string parameterName, Exception ex)
        {
            MessageBox.Show(
                $"设置{parameterName}失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// 显示参数读取错误消息
        /// </summary>
        /// <param name="parameterName">参数名称</param>
        /// <param name="ex">异常对象</param>
        public static void ShowParameterReadError(string parameterName, Exception ex)
        {
            MessageBox.Show(
                $"读取{parameterName}失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// 显示通用错误消息
        /// </summary>
        /// <param name="message">错误消息</param>
        public static void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// 显示通用警告消息
        /// </summary>
        /// <param name="message">警告消息</param>
        public static void ShowWarning(string message)
        {
            MessageBox.Show(
                message,
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        /// <summary>
        /// 显示通用提示消息
        /// </summary>
        /// <param name="message">提示消息</param>
        public static void ShowInfo(string message)
        {
            MessageBox.Show(
                message,
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #endregion

        #region 成功反馈

        /// <summary>
        /// 显示操作成功消息（可选）
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="showMessage">是否显示消息框（默认为false，建议通过状态栏提示）</param>
        public static void ShowSuccess(string operation, bool showMessage = false)
        {
            if (showMessage)
            {
                MessageBox.Show(
                    $"{operation}成功",
                    "成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        #endregion

        #region 确认对话框

        /// <summary>
        /// 显示确认对话框
        /// </summary>
        /// <param name="message">确认消息</param>
        /// <param name="title">对话框标题（默认为"确认"）</param>
        /// <returns>如果用户点击"是"返回true，否则返回false</returns>
        public static bool ShowConfirmation(string message, string title = "确认")
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// 显示带警告图标的确认对话框
        /// </summary>
        /// <param name="message">确认消息</param>
        /// <param name="title">对话框标题（默认为"警告"）</param>
        /// <returns>如果用户点击"是"返回true，否则返回false</returns>
        public static bool ShowWarningConfirmation(string message, string title = "警告")
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        #endregion

        #region 数据验证辅助

        /// <summary>
        /// 尝试解析设备地址
        /// </summary>
        /// <param name="addressText">地址文本</param>
        /// <param name="address">解析结果</param>
        /// <param name="showError">解析失败时是否显示错误消息</param>
        /// <returns>解析成功返回true，否则返回false</returns>
        public static bool TryParseDeviceAddress(string addressText, out byte address, bool showError = true)
        {
            if (byte.TryParse(addressText, out address))
            {
                return true;
            }

            if (showError)
            {
                ShowError("设备地址格式不正确，请输入0-255之间的数字");
            }
            return false;
        }

        /// <summary>
        /// 尝试解析无符号16位整数参数
        /// </summary>
        /// <param name="valueText">参数文本</param>
        /// <param name="value">解析结果</param>
        /// <param name="parameterName">参数名称（用于错误提示）</param>
        /// <param name="showError">解析失败时是否显示错误消息</param>
        /// <returns>解析成功返回true，否则返回false</returns>
        public static bool TryParseUInt16(string valueText, out ushort value, string parameterName = "参数", bool showError = true)
        {
            if (ushort.TryParse(valueText, out value))
            {
                return true;
            }

            if (showError)
            {
                ShowError($"{parameterName}格式不正确，请输入0-65535之间的整数");
            }
            return false;
        }

        /// <summary>
        /// 尝试解析整数参数
        /// </summary>
        /// <param name="valueText">参数文本</param>
        /// <param name="value">解析结果</param>
        /// <param name="parameterName">参数名称（用于错误提示）</param>
        /// <param name="showError">解析失败时是否显示错误消息</param>
        /// <returns>解析成功返回true，否则返回false</returns>
        public static bool TryParseInt32(string valueText, out int value, string parameterName = "参数", bool showError = true)
        {
            if (int.TryParse(valueText, out value))
            {
                return true;
            }

            if (showError)
            {
                ShowError($"{parameterName}格式不正确，请输入有效的整数");
            }
            return false;
        }

        #endregion
    }
}
