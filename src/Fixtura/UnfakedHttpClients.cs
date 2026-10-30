namespace Fixtura;

/// <summary>What happens to a request through one of the app's HTTP clients that has no fake.</summary>
public enum UnfakedHttpClients
{
    /// <summary>It reaches the network, as in production.</summary>
    Allow,

    /// <summary>It throws, naming the client, so a test can't depend on a real service by accident. The default.</summary>
    Block
}
