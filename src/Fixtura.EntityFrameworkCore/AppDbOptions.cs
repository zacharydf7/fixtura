namespace Fixtura;

/// <summary>What a DbContext swap does with the options the app set next to its provider, such as interceptors.</summary>
public enum AppDbOptions
{
    /// <summary>
    /// The replacement configures the context from scratch, provider options such as the migrations assembly included.
    /// </summary>
    Drop,

    /// <summary>
    /// The app's own configuration still runs; only its database provider is replaced. On the app's own provider, the
    /// app's migrations assembly and history table, command timeout and retries fill whatever the replacement leaves
    /// unset. Fails if that configuration throws, for example on a missing connection string.
    /// </summary>
    Keep
}
