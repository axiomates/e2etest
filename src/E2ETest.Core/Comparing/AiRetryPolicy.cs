namespace E2ETest.Core.Comparing;

/// <summary>AI 请求的有限重试：只处理明确标记为瞬时的失败。</summary>
public static class AiRetryPolicy
{
    public static async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (retryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(attempt, cancellationToken);
            }
            catch (AiRequestFailureException failure) when (failure.Retryable && attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay = ExponentialDelay(retryDelay, attempt);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static TimeSpan ExponentialDelay(TimeSpan initial, int failedAttempt)
    {
        double milliseconds = initial.TotalMilliseconds * Math.Pow(2, failedAttempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, 10000));
    }
}

/// <summary>由调用方根据 HTTP/连接错误分类的 AI 请求失败。</summary>
public sealed class AiRequestFailureException(string message, bool retryable, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool Retryable { get; } = retryable;
}
