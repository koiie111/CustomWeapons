using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

public sealed class CWAcceptanceTrace : BasePlugin
{
    public override string ModuleName => "Temporary CustomWeapons projectile inspection";
    public override string ModuleVersion => "1.0.0";
    private readonly Queue<object> _samples = new();
    private bool _active;
    private int _map;
    private int _total;
    public override void Load(bool hotReload)
    {
        _active = true;
        RegisterListener<Listeners.OnMapEnd>(() => { _map++; _samples.Clear(); });
        RegisterListener<Listeners.OnEntitySpawned>(entity =>
        {
            if (!_active || !entity.IsValid || entity.DesignerName is not
                ("flashbang_projectile" or "hegrenade_projectile" or "smokegrenade_projectile" or
                 "decoy_projectile" or "molotov_projectile" or "snowball_projectile")) return;
            var raw = entity.EntityHandle.Raw;
            var generation = _map;
            // CustomWeapons registered earlier and applies its model on NextFrame.
            // A second frame observes the result without changing any entity.
            Server.NextFrame(() => Server.NextFrame(() =>
            {
                if (!_active || generation != _map) return;
                var projectile = new CHandle<CBaseCSGrenadeProjectile>(raw).Value;
                if (projectile is not { IsValid: true }) return;
                var sample = new { sequence = ++_total, utc = DateTimeOffset.UtcNow,
                    raw, name = projectile.DesignerName,
                    thrower = projectile.Thrower.Raw, original_thrower = projectile.OriginalThrower.Raw,
                    model = projectile.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName };
                if (_samples.Count == 32) _samples.Dequeue();
                _samples.Enqueue(sample);
            }));
        });
        AddCommand("css_cwqa_trace", "Console-only recent projectile model snapshots", (p,c) =>
        {
            if (p == null) c.ReplyToCommand(JsonSerializer.Serialize(new { total = _total, samples = _samples.ToArray() }));
        });
        AddCommand("css_cwqa_flash_pair", "Console-only two flashbangs for the single test client", (p,c) =>
        {
            if (p != null) return;
            var players = Utilities.GetPlayers().Where(x => x.IsValid && !x.IsBot && !x.IsHLTV).ToArray();
            if (players.Length != 1 || !players[0].PawnIsAlive) { c.ReplyToCommand("Rejected: require one alive human"); return; }
            players[0].GiveNamedItem("weapon_flashbang");
            players[0].GiveNamedItem("weapon_flashbang");
            c.ReplyToCommand("Flash pair grant invoked for single test client");
        });
    }
    public override void Unload(bool hotReload) { _active = false; _map++; _samples.Clear(); }
}
