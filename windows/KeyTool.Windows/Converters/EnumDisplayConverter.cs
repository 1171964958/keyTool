using System;
using System.Globalization;
using System.Windows.Data;
using KeyTool.Windows.Models;

namespace KeyTool.Windows.Converters;

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value switch
        {
            ScheduleMode.Manual => "手动",
            ScheduleMode.Interval => "按间隔",
            ScheduleMode.Daily => "每天定时",
            MouseButtonKind.Left => "左键",
            MouseButtonKind.Right => "右键",
            MouseButtonKind.Middle => "中键",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
