namespace Fixtura;

/// <summary>A typed swap matched no registration in the app, usually because a service was renamed or removed.</summary>
public sealed class NothingMatchedException(string call)
    : InvalidOperationException($"{call} matched no registration in the app. Was the service renamed or removed?");
