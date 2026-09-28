namespace CustomWeapons.Core;

public sealed class WeaponDefinition
{
    public string Name { get; set; } = "";
    public Dictionary<string, SkinDefinition> Skins { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SkinDefinition
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Private { get; set; } = true;
    public bool Hide { get; set; } = true;
}

public sealed class DatabaseOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public uint Port { get; set; } = 3306;
    public string Database { get; set; } = "customweapons";
    public string Username { get; set; } = "customweapons";
    public string Password { get; set; } = "";
    public string PasswordEnvironmentVariable { get; set; } = "CW_DB_PASSWORD";
    public string SslMode { get; set; } = "Preferred";
    public uint TimeoutSeconds { get; set; } = 10;
}

public sealed record AccessRow(string SteamId, string Model, uint Sid, long Expires);
public sealed record CatalogRow(string Model, string? Name, int Active);
public sealed record CatalogEntry(string? Name, bool Active);

public sealed class DatabaseSnapshot
{
    public IReadOnlyDictionary<string, CatalogEntry> Catalog { get; }
    public IReadOnlyDictionary<string, AccessRow[]> Access { get; }
    public string[] Conflicts { get; }

    public DatabaseSnapshot(IEnumerable<CatalogRow> catalog, IEnumerable<AccessRow> access)
    {
        var groups = catalog.GroupBy(x => x.Model, StringComparer.Ordinal).ToArray();
        Conflicts = groups.Where(g => g.Select(x => (x.Name, x.Active)).Distinct().Count() > 1)
            .Select(g => g.Key).ToArray();
        Catalog = groups.ToDictionary(g => g.Key,
            g => new CatalogEntry(g.First().Name, !Conflicts.Contains(g.Key) && g.First().Active == 1),
            StringComparer.Ordinal);
        Access = access.GroupBy(x => x.SteamId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }
}

public static class SkinRules
{
    public static bool HasAccess(IEnumerable<AccessRow> rows, string steamId, string skinId, uint serverId, long now) =>
        rows.Any(x => x.SteamId == steamId && x.Model == skinId &&
            (serverId == 0 || x.Sid == 0 || x.Sid == serverId) && (x.Expires == 0 || x.Expires > now));

    public static bool CanApply(SkinDefinition skin, string id, string steamId, uint serverId, long now,
        bool databaseReady, DatabaseSnapshot? snapshot)
    {
        if (snapshot?.Catalog.TryGetValue(id, out var row) == true && !row.Active) return false;
        if (!skin.Private) return true;
        return databaseReady && snapshot != null && snapshot.Access.TryGetValue(steamId, out var rows) &&
            HasAccess(rows, steamId, id, serverId, now);
    }

    public static string DisplayName(SkinDefinition skin, string id, DatabaseSnapshot? snapshot) =>
        snapshot?.Catalog.TryGetValue(id, out var row) == true && !string.IsNullOrWhiteSpace(row.Name)
            ? row.Name : string.IsNullOrWhiteSpace(skin.Name) ? id : skin.Name;

    public static void ValidateWeapons(Dictionary<string, WeaponDefinition> weapons)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (weapon, definition) in weapons)
        {
            if (!weapon.StartsWith("weapon_", StringComparison.Ordinal) || weapon != weapon.ToLowerInvariant())
                throw new ArgumentException($"Invalid weapon class: {weapon}");
            if (definition?.Skins == null) throw new ArgumentException($"Missing Skins for {weapon}");
            foreach (var (id, skin) in definition.Skins)
            {
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
                    throw new ArgumentException($"Skin identifiers must be unique and 1..64 characters: {id}");
                if (skin == null || !skin.Model.StartsWith("models/", StringComparison.Ordinal) ||
                    !skin.Model.EndsWith(".vmdl", StringComparison.Ordinal) || skin.Model.Contains("..") ||
                    skin.Model.Contains('\\') || skin.Model.Any(char.IsControl))
                    throw new ArgumentException($"Invalid .vmdl resource path for {id}");
            }
        }
    }
}

public static class WeaponNames
{
    public static string Normalize(string designerName, ushort itemDefinition) => itemDefinition switch
    {
        60 => "weapon_m4a1_silencer",
        61 => "weapon_usp_silencer",
        63 => "weapon_cz75a",
        64 => "weapon_revolver",
        23 => "weapon_mp5sd",
        _ => designerName.ToLowerInvariant()
    };

    public static bool IsKnife(string name, ushort itemDefinition) =>
        name.StartsWith("weapon_knife", StringComparison.Ordinal) || itemDefinition is 42 or 59 or >= 500 and <= 526;

    public static string? Resolve(string designerName, ushort itemDefinition, IEnumerable<string> configured)
    {
        var names = configured as ICollection<string> ?? configured.ToArray();
        var exact = Normalize(designerName, itemDefinition);
        return names.Contains(exact) ? exact : IsKnife(exact, itemDefinition) && names.Contains("weapon_knife") ? "weapon_knife" : null;
    }

    public static readonly IReadOnlyDictionary<string, string[]> Projectiles = new Dictionary<string, string[]>
    {
        ["hegrenade_projectile"] = ["weapon_hegrenade"],
        ["flashbang_projectile"] = ["weapon_flashbang"],
        ["smokegrenade_projectile"] = ["weapon_smokegrenade"],
        ["decoy_projectile"] = ["weapon_decoy"],
        ["molotov_projectile"] = ["weapon_molotov", "weapon_incgrenade"],
        ["snowball_projectile"] = ["weapon_snowball"]
    };
}
