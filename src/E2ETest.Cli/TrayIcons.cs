using System.Drawing;
using System.Reflection;

namespace E2ETest.Cli;

internal static class TrayIcons
{
    public static Icon RecordingEmpty { get; } = Load("tray-recording-empty.ico");
    public static Icon Recording { get; } = Load("tray-recording.ico");
    public static Icon Capturing { get; } = Load("tray-capturing.ico");
    public static Icon Saving { get; } = Load("tray-saving.ico");
    public static Icon Success { get; } = Load("tray-success.ico");
    public static Icon Error { get; } = Load("tray-error.ico");

    private static Icon Load(string fileName)
    {
        Assembly assembly = typeof(TrayIcons).Assembly;
        string suffix = ".Assets.Icons." + fileName;
        string resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"缺少托盘图标资源: {fileName}");
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"托盘图标资源无法读取: {fileName}");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
