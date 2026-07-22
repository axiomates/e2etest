using E2ETest.Cli;

namespace E2ETest.Core.Tests;

public sealed class CliArgsTests
{
    private static readonly string[] Values = ["round", "name", "root"];
    private static readonly string[] Flags = ["ai"];

    [Fact]
    public void RejectsUnknownOption()
    {
        var args = CliArgs.Parse(["--nmae", "case-1"]);

        Assert.Throws<ArgumentException>(() => args.Validate(Values, Flags));
    }

    [Fact]
    public void RejectsMissingOptionValue()
    {
        var args = CliArgs.Parse(["--name", "--ai"]);

        Assert.Throws<ArgumentException>(() => args.Validate(Values, Flags));
    }

    [Fact]
    public void RejectsValueAttachedToFlag()
    {
        var args = CliArgs.Parse(["--ai", "false"]);

        Assert.Throws<ArgumentException>(() => args.Validate(Values, Flags));
    }

    [Fact]
    public void RejectsDuplicateOption()
    {
        var args = CliArgs.Parse(["--name", "first", "--name", "second"]);

        Assert.Throws<ArgumentException>(() => args.Validate(Values, Flags));
    }

    [Fact]
    public void AcceptsKnownOptionsWithExpectedShapes()
    {
        var args = CliArgs.Parse(["--round", "r1", "--name", "case-1", "--root", ".", "--ai"]);

        args.Validate(Values, Flags);
    }
}
