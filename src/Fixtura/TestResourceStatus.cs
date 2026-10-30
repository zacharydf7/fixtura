namespace Fixtura;

/// <summary>Whether a resource provisioned, and if not, why the fixture's tests are skipped.</summary>
public sealed class TestResourceStatus
{
    private TestResourceStatus(string? unavailableReason)
    {
        UnavailableReason = unavailableReason;
    }

    /// <summary>The resource is ready for the fixture's tests.</summary>
    public static TestResourceStatus Ready { get; } = new(null);

    /// <summary>The reason the resource is unavailable; null when it is ready.</summary>
    public string? UnavailableReason { get; }

    /// <summary>The resource is unavailable, so the fixture's tests are skipped with <paramref name="reason"/>.</summary>
    public static TestResourceStatus Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new TestResourceStatus(reason);
    }
}
