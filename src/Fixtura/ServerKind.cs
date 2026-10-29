namespace Fixtura;

/// <summary>Which server the app runs on during tests.</summary>
public enum ServerKind
{
    /// <summary>In memory: the fastest, and only clients from the fixture can reach it.</summary>
    TestServer,

    /// <summary>Kestrel on a free port: real sockets, so a browser or another process can reach the base address.</summary>
    Kestrel
}
