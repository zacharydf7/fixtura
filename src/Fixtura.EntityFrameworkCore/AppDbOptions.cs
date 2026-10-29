namespace Fixtura;

/// <summary>What a DbContext swap does with the options the app set next to its provider, such as interceptors.</summary>
public enum AppDbOptions
{
    /// <summary>The replacement configures the context from scratch.</summary>
    Drop,

    /// <summary>
    /// The app's own configuration still runs; only its database provider is replaced. Fails if that configuration throws,
    /// for example on a missing connection string.
    /// </summary>
    Keep
}
