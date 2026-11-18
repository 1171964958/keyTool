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
            KeyboardInputMode.Adaptive => "自适应（前台输入 / 受控后台）",
            KeyboardInputMode.BackgroundActivation => "旧后台伪激活（兼容迁移）",
            KeyboardInputMode.ForegroundCompatibility => "前台兼容（最稳定）",
            KeyboardInputMode.BackgroundMessages => "纯后台消息",
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
