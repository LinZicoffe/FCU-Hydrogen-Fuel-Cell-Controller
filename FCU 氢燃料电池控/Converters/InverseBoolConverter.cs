using System;
using System.Globalization;
using System.Windows.Data;

namespace FCU_氢燃料电池控.Converters
{
    /// <summary>
    /// 反向布尔值转换器
    /// True → False, False → True
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
