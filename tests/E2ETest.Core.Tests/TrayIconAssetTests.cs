using E2ETest.Cli;

namespace E2ETest.Core.Tests;

public sealed class TrayIconAssetTests
{
    [Fact]
    public void CliEmbedsEveryRecordingStateIcon()
    {
        string[] expected =
        [
            "tray-recording-empty.ico",
            "tray-recording.ico",
            "tray-capturing.ico",
            "tray-saving.ico",
            "tray-success.ico",
            "tray-error.ico",
        ];
        string[] resources = typeof(CliArgs).Assembly.GetManifestResourceNames();

        foreach (string fileName in expected)
        {
            string resource = Assert.Single(resources, name =>
                name.EndsWith(".Assets.Icons." + fileName, StringComparison.OrdinalIgnoreCase));
            using Stream? stream = typeof(CliArgs).Assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0);
        }
    }
}
