using System.Diagnostics;
using Xunit;

namespace Fixtura;

// Retries an attempt for work the app finishes after a request returns, instead of a test sleeping for it.
internal static class Waiting
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    // Once the time is up, or giveUp says there's no point, the attempt's own failure, such as an assertion's, is the test's.
    public static async Task<T> UntilAsync<T>(Func<Task<T>> attempt, TimeSpan? timeout, Func<bool>? giveUp = null)
    {
        TimeSpan limit = timeout ?? DefaultTimeout;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return await attempt();
            }
            catch (Exception) when (elapsed.Elapsed < limit && giveUp?.Invoke() != true)
            {
                await Task.Delay(Interval, TestContext.Current.CancellationToken);
            }
        }
    }
}
