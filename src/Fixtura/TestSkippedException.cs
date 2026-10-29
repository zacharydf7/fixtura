using Xunit.v3;

namespace Fixtura;

/// <summary>
/// Skips the running test. xUnit v3 reports any exception whose message starts with its dynamic-skip token as a skip.
/// </summary>
public sealed class TestSkippedException(string reason) : Exception(DynamicSkipToken.Value + reason);
