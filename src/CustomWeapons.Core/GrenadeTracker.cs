namespace CustomWeapons.Core;

// Capture the actual held instance, not the owner's saved preference. A transferred grenade
// may have a model that the thrower cannot select. Pawn handles include the entity serial.
public sealed class GrenadeTracker
{
    private sealed record Sample(string Weapon, string? Model, double Time);
    private readonly Dictionary<uint, Sample> _samples = new();
    public void Observe(uint pawn, string weapon, string? model, double now) => _samples[pawn] = new(weapon, model, now);
    public string? Find(uint pawn, IEnumerable<string> candidates, double now)
    {
        return _samples.TryGetValue(pawn, out var sample) && now - sample.Time is >= 0 and <= 2 &&
            candidates.Contains(sample.Weapon) ? sample.Model : null;
    }
    public void Prune(double now)
    {
        foreach (var key in _samples.Where(x => now - x.Value.Time > 2).Select(x => x.Key).ToArray()) _samples.Remove(key);
    }
    public void Clear() => _samples.Clear();
}
