using Xunit.v3;

namespace Fixtura.Spike;

// xUnit v3 reports any exception whose message starts with this token as a skip, so no assertion library is needed.
public sealed class TestSkippedException(string reason) : Exception(DynamicSkipToken.Value + reason);
