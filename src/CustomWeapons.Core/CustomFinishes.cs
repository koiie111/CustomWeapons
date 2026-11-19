namespace CustomWeapons.Core;

public sealed record CustomFinish(string Key, string Weapon, int Definition, int Paint, string Name, string WeaponLabel)
{
    public static IReadOnlyList<CustomFinish> Catalog { get; } = Array.AsReadOnly<CustomFinish>([
        new("nebula_61", "weapon_usp_silencer", 61, 3, "Nebula", "USP-S"),
        new("m4a4_nebula", "weapon_m4a1", 16, 3, "Nebula", "M4A4"),
        new("nebula_7", "weapon_ak47", 7, 3, "Nebula", "AK-47"),
        new("nebula_60", "weapon_m4a1_silencer", 60, 3, "Nebula", "M4A1-S"),
        new("nebula_1", "weapon_deagle", 1, 3, "Nebula", "Desert Eagle"),
        new("nebula_500", "weapon_bayonet", 500, 3, "Nebula", "Bayonet"),
        new("nebula_503", "weapon_knife_css", 503, 3, "Nebula", "Classic Knife"),
        new("nebula_505", "weapon_knife_flip", 505, 3, "Nebula", "Flip Knife"),
        new("nebula_506", "weapon_knife_gut", 506, 3, "Nebula", "Gut Knife"),
        new("nebula_507", "weapon_knife_karambit", 507, 3, "Nebula", "Karambit"),
        new("nebula_508", "weapon_knife_m9_bayonet", 508, 3, "Nebula", "M9 Bayonet"),
        new("nebula_509", "weapon_knife_tactical", 509, 3, "Nebula", "Huntsman Knife"),
        new("nebula_512", "weapon_knife_falchion", 512, 3, "Nebula", "Falchion Knife"),
        new("nebula_514", "weapon_knife_survival_bowie", 514, 3, "Nebula", "Bowie Knife"),
        new("nebula_515", "weapon_knife_butterfly", 515, 3, "Nebula", "Butterfly Knife"),
        new("nebula_516", "weapon_knife_push", 516, 3, "Nebula", "Shadow Daggers"),
        new("nebula_517", "weapon_knife_cord", 517, 3, "Nebula", "Paracord Knife"),
        new("nebula_518", "weapon_knife_canis", 518, 3, "Nebula", "Survival Knife"),
        new("nebula_519", "weapon_knife_ursus", 519, 3, "Nebula", "Ursus Knife"),
        new("nebula_520", "weapon_knife_gypsy_jackknife", 520, 3, "Nebula", "Navaja Knife"),
        new("nebula_521", "weapon_knife_outdoor", 521, 3, "Nebula", "Nomad Knife"),
        new("nebula_522", "weapon_knife_stiletto", 522, 3, "Nebula", "Stiletto Knife"),
        new("nebula_523", "weapon_knife_widowmaker", 523, 3, "Nebula", "Talon Knife"),
        new("nebula_525", "weapon_knife_skeleton", 525, 3, "Nebula", "Skeleton Knife"),
        new("nebula_526", "weapon_knife_kukri", 526, 3, "Nebula", "Kukri Knife"),
        new("nebula_9", "weapon_awp", 9, 3, "Nebula", "AWP"),
        new("nebula_4", "weapon_glock", 4, 3, "Nebula", "Glock-18"),
        new("ak47_bluegem", "weapon_ak47", 7, 2, "Bluegem", "AK-47")
    ]);
}
