namespace Fixtura.Spike;

public sealed class NothingMatchedException(string call)
    : InvalidOperationException($"{call} matched no registration in the app. Was the service renamed or removed?");
