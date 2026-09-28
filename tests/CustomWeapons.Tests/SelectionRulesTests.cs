using CustomWeapons.Core;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class SelectionRulesTests
{
    private static WeaponDefinition Weapon() => new() { Skins = new()
    {
        ["first"] = new() { Model = "models/first.vmdl" },
        ["second"] = new() { Model = "models/second.vmdl" }
    } };

    [Fact]
    public void NewPlayerGetsFirstAvailableSkinInConfigurationOrder()
    {
        Assert.Equal("first", SelectionRules.Resolve(null, Weapon(), (_, _) => true));
        Assert.Equal("second", SelectionRules.Resolve(null, Weapon(), (id, _) => id == "second"));
        Assert.Null(SelectionRules.Resolve(null, Weapon(), (_, _) => false));
    }

    [Fact]
    public void ManualSkinAndExplicitStockModelTakePrecedenceOverAutomaticSelection()
    {
        Assert.Equal("second", SelectionRules.Resolve("second", Weapon(), (_, _) => true));
        Assert.Equal("", SelectionRules.Resolve("", Weapon(), (_, _) => throw new Exception("Explicit stock must stay stock")));
    }

    [Fact]
    public void UnavailableManualChoiceIsNotSilentlyReplacedByAnotherSkin()
    {
        Assert.Null(SelectionRules.Resolve("first", Weapon(), (id, _) => id == "second"));
        Assert.Null(SelectionRules.Resolve("removed", Weapon(), (_, _) => true));
    }

    [Theory]
    [InlineData(false, 1, 0, 1, null)]
    [InlineData(true, 2, 0, 1, null)]
    [InlineData(true, 1, 100, 1, null)]
    [InlineData(true, 1, 101, 0, null)]
    [InlineData(true, 1, 101, 1, "second")]
    [InlineData(true, 0, 0, 1, "second")]
    public void AutomaticSelectionUsesDatabaseReadinessServerExpiryAndCatalogRules(bool ready, uint sid, long expires, int active, string? expected)
    {
        var snapshot = new DatabaseSnapshot([new("second", null, active)], [new("765", "second", sid, expires)]);
        Assert.Equal(expected, SelectionRules.Resolve(null, Weapon(),
            (id, skin) => SkinRules.CanApply(skin, id, "765", 1, 100, ready, snapshot)));
    }

    [Fact]
    public void DisabledConflictingCatalogAndUnprecachedModelsAreNotAutoSelected()
    {
        var snapshot = new DatabaseSnapshot([new("first", "A", 1), new("first", "B", 1)],
            [new("765", "first", 0, 0), new("765", "second", 0, 0)]);
        var precached = new HashSet<string> { "models/first.vmdl" };
        Assert.Null(SelectionRules.Resolve(null, Weapon(), (id, skin) =>
            SkinRules.CanApply(skin, id, "765", 1, 100, true, snapshot) && precached.Contains(skin.Model)));
        precached.Add("models/second.vmdl");
        Assert.Equal("second", SelectionRules.Resolve(null, Weapon(), (id, skin) =>
            SkinRules.CanApply(skin, id, "765", 1, 100, true, snapshot) && precached.Contains(skin.Model)));
    }

    [Fact]
    public async Task AutomaticChoiceDoesNotOverwriteSavedPreferencesAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cw-selection-rules-" + Guid.NewGuid());
        var path = Path.Combine(directory, "selections.json");
        try
        {
            await using (var store = new SelectionStore(path, true))
            {
                Assert.Equal("first", SelectionRules.Resolve(store.Get("new", "weapon_awp"), Weapon(), (_, _) => true));
                Assert.Null(store.Get("new", "weapon_awp"));
                store.Set("manual", "weapon_awp", "second");
                store.Set("stock", "weapon_awp", null);
            }
            await using var restored = new SelectionStore(path, true);
            Assert.Equal("second", SelectionRules.Resolve(restored.Get("manual", "weapon_awp"), Weapon(), (_, _) => true));
            Assert.Equal("", SelectionRules.Resolve(restored.Get("stock", "weapon_awp"), Weapon(), (_, _) => true));
            Assert.Equal("second", SelectionRules.Resolve(restored.Get("new", "weapon_awp"), Weapon(), (id, _) => id == "second"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
