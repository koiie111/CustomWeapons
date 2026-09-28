using CounterStrikeSharp.API.Core;
using CustomWeapons.Core;

namespace CustomWeapons;

public sealed class PluginConfig : BasePluginConfig
{
    public uint ServerId { get; set; } = 1;
    public double CooldownSeconds { get; set; } = 1;
    public bool SaveSelections { get; set; } = true;
    public float RefreshIntervalSeconds { get; set; } = 60;
    public DatabaseOptions Database { get; set; } = new();
    public Dictionary<string, WeaponDefinition> Weapons { get; set; } = Defaults();

    private static SkinDefinition Skin(string name, string model) => new() { Name = name, Model = model };
    public static Dictionary<string, WeaponDefinition> Defaults() => new(StringComparer.Ordinal)
    {
        ["weapon_ak47"] = new() { Name = "AK-47", Skins = new()
        {
            ["daedalos"] = Skin("AK-47 - Дедал", "models/kolka/weapons/val_weapon2/daedalus/daedalos.vmdl"),
            ["ak_12"] = Skin("AK-47 - Светоснежка", "models/weapons/kolka/weapons_3/ak/ak_12.vmdl")
        } },
        ["weapon_m4a1_silencer"] = new() { Name = "M4A1-S", Skins = new()
        {
            ["m4a1s_daeldalus"] = Skin("M4A1-S - Дедал", "models/kolka/weapons/val_weapon2/daedalus_m4a1/m4a1s_daeldalus.vmdl")
        } },
        ["weapon_awp"] = new() { Name = "AWP", Skins = new()
        {
            ["awp_insane"] = Skin("AWP - Insane", "models/kolka/valorant/awp_isane/awp_insane.vmdl"),
            ["awp_animes"] = Skin("AWP - Перлика", "models/weapons/kolka/pak_4/awp/awp_animes.vmdl")
        } },
        ["weapon_ssg08"] = new() { Name = "SSG-08", Skins = new()
        {
            ["scout_anim"] = Skin("SSG-08 - Криоангел", "models/weapons/kolka/pak_4/scout/scout_anim.vmdl")
        } }
    };
}
