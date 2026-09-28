namespace CustomWeapons.Core;

public static class SelectionRules
{
    // null means never chosen; empty string means explicitly chose the stock model.
    // Automatic choices are not persisted as manual preferences.
    public static string? Resolve(string? saved, WeaponDefinition weapon, Func<string, SkinDefinition, bool> available)
    {
        if (saved != null)
            return saved.Length == 0 || weapon.Skins.TryGetValue(saved, out var chosen) && available(saved, chosen)
                ? saved : null;
        foreach (var (id, skin) in weapon.Skins)
            if (available(id, skin)) return id;
        return null;
    }
}
