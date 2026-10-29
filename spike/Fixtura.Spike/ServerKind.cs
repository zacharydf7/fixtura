namespace Fixtura.Spike;

// Which server the app runs on during tests.
public enum ServerKind
{
    // In memory: the fastest, and only clients from the fixture can reach it.
    TestServer,

    // Kestrel on a free port: real sockets, so a browser or another process can reach BaseAddress.
    Kestrel
}
