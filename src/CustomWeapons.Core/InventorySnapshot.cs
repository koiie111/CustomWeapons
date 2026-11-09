namespace CustomWeapons.Core;

// Compare the complete entity handles (including serials), never a lossy hash.
public sealed class InventorySnapshot
{
    private readonly List<uint> _previous = [];
    private bool _initialized;

    public bool Update(List<uint> current)
    {
        current.Sort();
        if (_initialized && current.Count == _previous.Count && current.SequenceEqual(_previous)) return false;
        _previous.Clear();
        _previous.AddRange(current);
        _initialized = true;
        return true;
    }
}
