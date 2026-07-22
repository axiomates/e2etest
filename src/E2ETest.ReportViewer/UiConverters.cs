using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace E2ETest.ReportViewer;

public sealed class VerdictTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() switch
    {
        "failed" => "失败",
        "needs_review" or "uncertain" => "待确认",
        "passed" => "已通过",
        "cancelled" => "已取消",
        "skipped" => "已跳过",
        _ => "未知",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class VerdictBrushConverter : IValueConverter
{
    private static readonly Brush Failed = Brush("#B42318");
    private static readonly Brush Review = Brush("#B54708");
    private static readonly Brush Passed = Brush("#067647");
    private static readonly Brush Muted = Brush("#667085");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() switch
    {
        "failed" => Failed,
        "needs_review" or "uncertain" => Review,
        "passed" => Passed,
        _ => Muted,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    private static SolidColorBrush Brush(string value) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }
}

public sealed class RoleTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() switch
    {
        "first" => "流程开始",
        "last" => "流程结束",
        _ => "中间步骤",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
