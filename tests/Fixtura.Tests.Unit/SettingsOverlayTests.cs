using Fixtura.Settings;
using Microsoft.Extensions.Logging;

namespace Fixtura.Tests.Unit;

public class SettingsOverlayTests
{
    [Fact]
    public void FromObject_ShouldSpellKeysLikeAppSettings_WhenGivenNestedObjectsAndArrays()
    {
        SettingsOverlay overlay = SettingsOverlay.FromObject(new
        {
            Features = new { Reports = true, Limit = 5, Missing = (string?)null, Level = LogLevel.Warning },
            Shipping = new { Carriers = new[] { new { Name = "Only" } } }
        });

        Assert.Equal(new Dictionary<string, string?>
        {
            ["Features:Reports"] = "true",
            ["Features:Limit"] = "5",
            ["Features:Missing"] = null,
            ["Features:Level"] = "Warning",
            ["Shipping:Carriers:0:Name"] = "Only"
        }, overlay.Values);
        Assert.Equal(["Shipping:Carriers"], overlay.ArrayPaths);
    }

    [Fact]
    public void Merge_ShouldReplaceAnEarlierArrayWhole_WhenTheLaterOverlayHasAShorterOne()
    {
        SettingsOverlay merged = SettingsOverlay.FromObject(new { Carriers = new[] { "A", "B" }, Limit = 1 });

        merged.Merge(SettingsOverlay.FromObject(new { Carriers = new[] { "C" } }));

        Assert.Equal(new Dictionary<string, string?> { ["Carriers:0"] = "C", ["Limit"] = "1" }, merged.Values);
    }

    [Fact]
    public void FromJson_ShouldAcceptAppSettingsSyntax_WhenGivenCommentsAndTrailingCommas()
    {
        SettingsOverlay overlay = SettingsOverlay.FromJson("""
            { "Features": { "Reports": true, }, // on for tests
            }
            """);

        Assert.Equal("true", overlay.Values["Features:Reports"]);
    }

    [Fact]
    public void FromJson_ShouldMatchFromObject_WhenBothDescribeTheSameSettings()
    {
        SettingsOverlay fromJson = SettingsOverlay.FromJson("""{ "Shipping": { "Carriers": [ { "Name": "Only" } ] } }""");
        SettingsOverlay fromObject = SettingsOverlay.FromObject(new { Shipping = new { Carriers = new[] { new { Name = "Only" } } } });

        Assert.Equal(fromObject.Values, fromJson.Values);
        Assert.Equal(fromObject.ArrayPaths, fromJson.ArrayPaths);
    }
}
