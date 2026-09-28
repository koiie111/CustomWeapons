using CustomWeapons.Core;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class WeaponTests
{
    [Fact]
    public void ShippedConfigurationContainsValidSixSkinCatalog()
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CustomWeapons.json")));
        var weapons = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, WeaponDefinition>>(
            document.RootElement.GetProperty("Weapons"))!;
        SkinRules.ValidateWeapons(weapons);
        Assert.Equal(6, weapons.Values.Sum(w => w.Skins.Count));
        Assert.Equal("", document.RootElement.GetProperty("Database").GetProperty("Password").GetString());
    }

    [Theory]
    [InlineData("weapon_m4a1", 60, "weapon_m4a1_silencer")]
    [InlineData("weapon_hkp2000", 61, "weapon_usp_silencer")]
    [InlineData("weapon_p250", 63, "weapon_cz75a")]
    [InlineData("weapon_deagle", 64, "weapon_revolver")]
    [InlineData("weapon_mp7", 23, "weapon_mp5sd")]
    [InlineData("weapon_ak47", 7, "weapon_ak47")]
    public void ItemDefinitionDisambiguatesVariants(string name, ushort definition, string expected) =>
        Assert.Equal(expected, WeaponNames.Normalize(name, definition));

    [Fact]
    public void KnifeExactCategoryWinsOtherwiseUsesGeneric()
    {
        Assert.Equal("weapon_knife_karambit", WeaponNames.Resolve("weapon_knife_karambit", 507, ["weapon_knife", "weapon_knife_karambit"]));
        Assert.Equal("weapon_knife", WeaponNames.Resolve("weapon_knife_karambit", 507, ["weapon_knife"]));
        Assert.Null(WeaponNames.Resolve("weapon_awp", 9, ["weapon_knife"]));
    }

    [Fact]
    public void GrenadeUsesActualInstanceModelAndExpires()
    {
        var tracker = new GrenadeTracker();
        tracker.Observe(100, "weapon_incgrenade", "models/transferred.vmdl", 10);
        Assert.Equal("models/transferred.vmdl", tracker.Find(100, WeaponNames.Projectiles["molotov_projectile"], 11));
        Assert.Null(tracker.Find(101, WeaponNames.Projectiles["molotov_projectile"], 11));
        Assert.Null(tracker.Find(100, WeaponNames.Projectiles["flashbang_projectile"], 11));
        Assert.Null(tracker.Find(100, WeaponNames.Projectiles["molotov_projectile"], 13));
        tracker.Observe(100, "weapon_incgrenade", null, 14);
        Assert.Null(tracker.Find(100, WeaponNames.Projectiles["molotov_projectile"], 14));
    }

    [Fact]
    public void RejectsDuplicateIdsAndInvalidResourcePaths()
    {
        var skin = new SkinDefinition { Model = "models/test.vmdl" };
        var weapons = new Dictionary<string, WeaponDefinition>
        {
            ["weapon_awp"] = new() { Skins = new() { ["duplicate"] = skin } },
            ["weapon_ak47"] = new() { Skins = new() { ["duplicate"] = skin } }
        };
        Assert.Throws<ArgumentException>(() => SkinRules.ValidateWeapons(weapons));
        weapons.Remove("weapon_ak47");
        SkinRules.ValidateWeapons(weapons);
        skin.Model = "models/../unsafe.vmdl";
        Assert.Throws<ArgumentException>(() => SkinRules.ValidateWeapons(weapons));
    }
}
