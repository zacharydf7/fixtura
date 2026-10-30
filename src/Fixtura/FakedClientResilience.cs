namespace Fixtura;

/// <summary>
/// What happens to the resilience handlers, such as <c>AddStandardResilienceHandler</c>'s retries, timeouts and circuit
/// breaker, on a client that has a fake.
/// </summary>
public enum FakedClientResilience
{
    /// <summary>They run, as in production, so a test can check how the app retries. The default.</summary>
    Keep,

    /// <summary>
    /// They're removed, so each request reaches the fake once and its error response reaches the app at once, with no
    /// circuit breaker state carried from one test to the next.
    /// </summary>
    Remove
}
