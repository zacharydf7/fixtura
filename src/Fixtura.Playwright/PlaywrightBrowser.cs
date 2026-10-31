namespace Fixtura;

/// <summary>Which browser Playwright runs the fixture's pages in.</summary>
public enum PlaywrightBrowser
{
    /// <summary>Chromium, as Chrome and Edge are built on.</summary>
    Chromium,

    /// <summary>Firefox.</summary>
    Firefox,

    /// <summary>WebKit, as Safari is built on.</summary>
    WebKit
}
