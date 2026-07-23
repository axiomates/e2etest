using E2ETest.RecordLauncher;

namespace E2ETest.Core.Tests;

public sealed class RecordLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, $"launcher-{Guid.NewGuid():N}");
    private readonly string _cliPath;

    public RecordLauncherTests()
    {
        Directory.CreateDirectory(_directory);
        _cliPath = Path.Combine(_directory, "e2etest.exe");
        File.WriteAllBytes(_cliPath, []);
    }

    [Fact]
    public void BlankOptionalGuidanceIsNotPassedToCli()
    {
        var startInfo = RecordProcessStartInfoFactory.Create(
            _cliPath, _directory, "创建立柱", "  ", null);

        Assert.Equal(
            ["record", "--name", "创建立柱", "--root", Path.GetFullPath(_directory)],
            startInfo.ArgumentList);
    }

    [Fact]
    public void GuidanceIsPreservedAsIndividualArguments()
    {
        const string focus = "  先看到了什么\r\n再判断 \"立柱\"  ";
        const string criteria = " 没有错误提示时通过 ";

        var startInfo = RecordProcessStartInfoFactory.Create(
            _cliPath, _directory, "case 1", focus, criteria);

        Assert.Equal("--focus", startInfo.ArgumentList[5]);
        Assert.Equal("先看到了什么\r\n再判断 \"立柱\"", startInfo.ArgumentList[6]);
        Assert.Equal("--criteria", startInfo.ArgumentList[7]);
        Assert.Equal("没有错误提示时通过", startInfo.ArgumentList[8]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
