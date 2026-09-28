namespace CustomWeapons.Core;

// Cold Load runs before CS2 globals and its deferred event hooks are ready.
// Keep all game-dependent initialization behind the map-start boundary.
public sealed class RuntimeLifecycle(Action initialize, Action refreshPlayers)
{
    private bool _initialized;
    private bool _stopped;
    public bool MapReady { get; private set; }

    public void Load(bool hotReload, Action<Action> defer)
    {
        if (hotReload) defer(MapStarted);
    }

    public void MapStarted()
    {
        if (_stopped) return;
        if (!_initialized)
        {
            try { initialize(); _initialized = true; }
            catch { Stop(); throw; } // Never duplicate partially registered native hooks.
        }
        MapReady = true;
        refreshPlayers();
    }

    public void MapEnded() => MapReady = false;
    public void Stop() { _stopped = true; MapReady = false; }
}
