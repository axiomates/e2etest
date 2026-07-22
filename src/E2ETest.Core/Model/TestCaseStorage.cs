namespace E2ETest.Core.Model;

/// <summary>持有测试用例读租约的一致快照。</summary>
public sealed class TestCaseSnapshot : IDisposable
{
    public required string Directory { get; init; }
    public required TestCaseManifest Manifest { get; init; }
    internal IDisposable? ReadLease { get; init; }
    public void Dispose() => ReadLease?.Dispose();
}
