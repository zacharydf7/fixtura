namespace Fixtura;

/// <summary>
/// Typed swaps matched no registration in the app, usually because a service was renamed or removed. Building a host
/// reports every such swap at once.
/// </summary>
public sealed class NothingMatchedException : InvalidOperationException
{
    public NothingMatchedException(string call)
        : this([call])
    {
    }

    internal NothingMatchedException(IReadOnlyList<string> calls)
        : base(calls.Count == 1
            ? $"{calls[0]} matched no registration in the app. Was the service renamed or removed?"
            : $"{string.Join(", ", calls)} matched no registration in the app. Were the services renamed or removed?")
    {
        Calls = calls;
    }

    internal IReadOnlyList<string> Calls { get; }
}
