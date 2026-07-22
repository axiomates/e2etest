using E2ETest.Cli;

namespace E2ETest.Core.Tests;

public sealed class DataRootResolverTests
{
    [Fact]
    public void ExplicitRootAlwaysWins()
    {
        using var directories = new TemporaryDirectories();
        File.WriteAllText(Path.Combine(directories.Current, "config.json"), "{}");
        File.WriteAllText(Path.Combine(directories.Executable, "config.json"), "{}");

        string actual = DataRootResolver.Resolve(directories.Explicit, directories.Current, directories.Executable);

        Assert.Equal(Path.GetFullPath(directories.Explicit), actual);
    }

    [Fact]
    public void CurrentDirectoryConfigWinsOverExecutableDirectoryConfig()
    {
        using var directories = new TemporaryDirectories();
        File.WriteAllText(Path.Combine(directories.Current, "config.json"), "{}");
        File.WriteAllText(Path.Combine(directories.Executable, "config.json"), "{}");

        string actual = DataRootResolver.Resolve(null, directories.Current, directories.Executable);

        Assert.Equal(Path.GetFullPath(directories.Current), actual);
    }

    [Fact]
    public void FallsBackToExecutableDirectoryWhenOnlyItContainsConfig()
    {
        using var directories = new TemporaryDirectories();
        File.WriteAllText(Path.Combine(directories.Executable, "config.json"), "{}");

        string actual = DataRootResolver.Resolve(null, directories.Current, directories.Executable);

        Assert.Equal(Path.GetFullPath(directories.Executable), actual);
    }

    [Fact]
    public void KeepsCurrentDirectoryWhenNeitherDirectoryContainsConfig()
    {
        using var directories = new TemporaryDirectories();

        string actual = DataRootResolver.Resolve(null, directories.Current, directories.Executable);

        Assert.Equal(Path.GetFullPath(directories.Current), actual);
    }

    private sealed class TemporaryDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "e2etest-root-resolver-" + Guid.NewGuid().ToString("N"));
        public string Explicit => Path.Combine(_root, "explicit");
        public string Current => Path.Combine(_root, "current");
        public string Executable => Path.Combine(_root, "executable");

        public TemporaryDirectories()
        {
            Directory.CreateDirectory(Explicit);
            Directory.CreateDirectory(Current);
            Directory.CreateDirectory(Executable);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
