using E2ETest.Core.Storage;
using Serilog;
using Serilog.Events;

namespace E2ETest.Cli;

public static class LoggingBootstrap
{
    public static void Configure(string root)
    {
        var repo = new TestCaseRepository(root);
        var config = ConfigStore.Load(repo.ConfigPath);
        if (!Enum.TryParse<LogEventLevel>(config.Logging.MinimumLevel, true, out var level))
            level = LogEventLevel.Information;

        string logDirectory = Path.GetFullPath(Path.Combine(repo.Root, config.Logging.Directory));
        Directory.CreateDirectory(logDirectory);
        string logPath = Path.Combine(logDirectory, "e2etest-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Clamp(config.Logging.RetainedFileCount, 1, 200),
                retainedFileTimeLimit: TimeSpan.FromDays(Math.Max(1, config.Logging.RetainedDays)),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();
    }
}
