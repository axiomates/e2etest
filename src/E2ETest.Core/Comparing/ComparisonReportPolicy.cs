namespace E2ETest.Core.Comparing;

public static class ComparisonReportPolicy
{
    public static bool ShouldWriteRoundSummary(string? requestedName) => requestedName is null;
}
