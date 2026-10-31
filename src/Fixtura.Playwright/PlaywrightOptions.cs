namespace Fixtura;

/// <summary>How a fixture's browser runs.</summary>
public sealed class PlaywrightOptions
{
    /// <summary>The browser; Chromium unless set.</summary>
    public PlaywrightBrowser Browser { get; init; } = PlaywrightBrowser.Chromium;

    /// <summary>Whether the browser runs without a window; set it to false to watch a test while debugging.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>
    /// An environment variable that, when "true" or "1", makes a browser that isn't installed fail the fixture instead of
    /// skipping its tests. Set it in CI.
    /// </summary>
    public string? RequiredWhenVariableIsTrue { get; init; }
}
