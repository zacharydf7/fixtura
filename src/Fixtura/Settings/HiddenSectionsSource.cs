using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Fixtura.Settings;

// Hiding the app's copy of a section lets an overlay's array replace it whole instead of merging with it by index.
internal sealed class HiddenSectionsSource(IConfigurationSource inner, IReadOnlyList<string> hidden) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new HiddenSectionsProvider(inner.Build(builder), hidden);
}

internal sealed class HiddenSectionsProvider(IConfigurationProvider inner, IReadOnlyList<string> hidden)
    : IConfigurationProvider, IDisposable
{
    public void Dispose() => (inner as IDisposable)?.Dispose();

    public bool TryGet(string key, out string? value)
    {
        value = null;
        return !IsHidden(key) && inner.TryGet(key, out value);
    }

    public void Set(string key, string? value) => inner.Set(key, value);

    public IChangeToken GetReloadToken() => inner.GetReloadToken();

    public void Load() => inner.Load();

    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        IEnumerable<string> visible = inner.GetChildKeys([], parentPath)
            .Where(key => !IsHidden(parentPath is null ? key : $"{parentPath}:{key}"));
        return earlierKeys.Concat(visible).Order(ConfigurationKeyComparer.Instance);
    }

    private bool IsHidden(string key) => hidden.Any(section => SettingsOverlay.IsUnder(key, section));
}
