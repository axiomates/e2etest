using E2ETest.Core.Comparing;

namespace E2ETest.Core.Tests;

/// <summary>AI 网络故障策略：仅重试短暂故障，绝不吞掉取消或鉴权等确定性故障。</summary>
public sealed class AiRetryPolicyTests
{
    [Fact]
    public async Task RetriesTransientFailureThenReturnsResponse()
    {
        int attempts = 0;
        string result = await AiRetryPolicy.ExecuteAsync(async (_, _) =>
        {
            attempts++;
            if (attempts < 3) throw new AiRequestFailureException("HTTP 503", retryable: true);
            await Task.Yield();
            return "ok";
        }, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task DoesNotRetryPermanentFailure()
    {
        int attempts = 0;
        await Assert.ThrowsAsync<AiRequestFailureException>(() => AiRetryPolicy.ExecuteAsync<string>((_, _) =>
        {
            attempts++;
            throw new AiRequestFailureException("HTTP 401", retryable: false);
        }, maxAttempts: 3, retryDelay: TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task StopsAfterConfiguredAttemptLimit()
    {
        int attempts = 0;
        await Assert.ThrowsAsync<AiRequestFailureException>(() => AiRetryPolicy.ExecuteAsync<string>((_, _) =>
        {
            attempts++;
            throw new AiRequestFailureException("HTTP 429", retryable: true);
        }, maxAttempts: 3, retryDelay: TimeSpan.Zero));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task CallerCancellationIsNotRetried()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AiRetryPolicy.ExecuteAsync<string>((_, token) =>
        {
            attempts++;
            return Task.FromCanceled<string>(token);
        }, maxAttempts: 3, retryDelay: TimeSpan.Zero, cancellation.Token));

        Assert.Equal(0, attempts);
    }
}
