using Xunit;

namespace Fixtura;

/// <summary>
/// The messages a hub sent one client method, which <c>connection.Record&lt;TMessage&gt;(method)</c> keeps.
/// </summary>
public sealed class HubMessages<TMessage>
{
    private readonly Lock _gate = new();
    private readonly List<TMessage> _received = [];
    private readonly string _method;

    // Completed by each message, so a wait looks again only when there's something new.
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal HubMessages(string method)
    {
        _method = method;
    }

    /// <summary>The messages received so far, oldest first.</summary>
    public IReadOnlyList<TMessage> Received
    {
        get
        {
            lock (_gate)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>
    /// Waits until the hub has sent a message that <paramref name="match"/> accepts, any message unless given, and
    /// returns the first one. Fails after <paramref name="timeout"/>, five seconds unless given.
    /// </summary>
    public async Task<TMessage> WaitForAsync(Func<TMessage, bool>? match = null, TimeSpan? timeout = null)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        waiting.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            Task arrived;
            lock (_gate)
            {
                foreach (TMessage message in _received)
                {
                    if (match?.Invoke(message) != false)
                    {
                        return message;
                    }
                }

                arrived = _arrived.Task;
            }

            try
            {
                await arrived.WaitAsync(waiting.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"The hub sent no matching {_method} message.");
            }
        }
    }

    internal void Add(TMessage message)
    {
        TaskCompletionSource arrived;
        lock (_gate)
        {
            _received.Add(message);
            arrived = _arrived;
            _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        arrived.SetResult();
    }
}
