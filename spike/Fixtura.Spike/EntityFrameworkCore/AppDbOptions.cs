namespace Fixtura.Spike.EntityFrameworkCore;

// What a DbContext swap does with the options the app set next to its provider, such as interceptors.
public enum AppDbOptions
{
    // The replacement configures the context from scratch.
    Drop,

    // The app's own configuration still runs; only its database provider is replaced.
    Keep
}
