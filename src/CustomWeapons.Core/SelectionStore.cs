using System.Text.Json;
using System.Threading.Channels;

namespace CustomWeapons.Core;

public sealed class SelectionStore : IAsyncDisposable
{
    private readonly Dictionary<string, Dictionary<string, string>> _players;
    private readonly string _path;
    private readonly bool _persist;
    private readonly Action<Exception> _onError;
    private readonly Channel<string> _writes = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private readonly Task _writer;
    private readonly object _gate = new();
    private bool _disposed;

    public SelectionStore(string path, bool persist, Action<Exception>? onError = null)
    {
        _path = path;
        _persist = persist;
        _onError = onError ?? (_ => { });
        _players = new(StringComparer.Ordinal);
        if (persist && File.Exists(path))
        {
            try
            {
                _players = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path))
                    ?? throw new JsonException("Selection file contains null");
                if (_players.Any(p => p.Value == null || p.Value.Any(s => s.Value == null)))
                    throw new JsonException("Invalid null selection");
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                _onError(e);
                // Preserve unreadable/corrupt data before allowing new writes; failure aborts loading.
                File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                _players = new(StringComparer.Ordinal);
            }
        }
        _writer = WriteLoopAsync();
    }

    public string? Get(string steamId, string weapon)
    {
        lock (_gate) return _players.TryGetValue(steamId, out var items) && items.TryGetValue(weapon, out var id) ? id : null;
    }

    public void Set(string steamId, string weapon, string? skinId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_players.TryGetValue(steamId, out var items)) _players[steamId] = items = new(StringComparer.Ordinal);
            // An explicit default is retained, so picking up another player's model can still be reset.
            items[weapon] = skinId ?? "";
            if (_persist) _writes.Writer.TryWrite(JsonSerializer.Serialize(_players));
        }
    }

    public void Disconnect(string steamId)
    {
        if (!_persist) lock (_gate) _players.Remove(steamId);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var json in _writes.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                await File.WriteAllTextAsync(temporary, json);
                File.Move(temporary, _path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _onError(e); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { _disposed = true; _writes.Writer.TryComplete(); }
        await _writer;
    }
}
