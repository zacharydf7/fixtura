using Microsoft.Extensions.Configuration;

namespace Fixtura.Settings;

// The app's last configuration source: the fixture's settings, with a test's changes on top.
internal sealed class TestSettingsSource(TestSettingsProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

// Reloads on each change, so IConfiguration and IOptionsMonitor see it while the host runs.
internal sealed class TestSettingsProvider : ConfigurationProvider
{
    private readonly SettingsOverlay _fixtureSettings;
    private SettingsOverlay _shown;

    public TestSettingsProvider(SettingsOverlay fixtureSettings)
    {
        _fixtureSettings = fixtureSettings;
        _shown = fixtureSettings;
        Show(fixtureSettings);
    }

    // Only the app's copies of the fixture's own arrays are hidden, from the start, so a test can replace only those.
    public void Change(SettingsOverlay change)
    {
        string? unhidden = change.ArrayPaths.FirstOrDefault(array =>
            !_fixtureSettings.ArrayPaths.Any(hidden => SettingsOverlay.IsUnder(array, hidden)));
        if (unhidden is not null)
        {
            throw new InvalidOperationException($"ChangeSettingsAsync can only replace an array the fixture's settings "
                + $"set, and they don't set {unhidden}. Set it with host.Settings in Configure, or use WithOverrides.");
        }

        var next = new SettingsOverlay();
        next.Merge(_shown);
        next.Merge(change);
        Show(next);
        OnReload();
    }

    public void Reset()
    {
        if (_shown != _fixtureSettings)
        {
            Show(_fixtureSettings);
            OnReload();
        }
    }

    // A copy, because ConfigurationProvider.Set writes into Data and the fixture's settings must survive it.
    private void Show(SettingsOverlay settings)
    {
        _shown = settings;
        Data = new Dictionary<string, string?>(settings.Values, StringComparer.OrdinalIgnoreCase);
    }
}
