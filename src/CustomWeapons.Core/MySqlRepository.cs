using MySqlConnector;

namespace CustomWeapons.Core;

public interface IAccessRepository
{
    Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken);
}
public interface IWebsiteSelectionRepository
{
    Task<long?> SaveSelectionAsync(string steamId, int team, string weapon, string model, long expectedRevision, CancellationToken cancellationToken);
    Task ImportSelectionsAsync(IReadOnlyCollection<WebsiteSelection> selections, CancellationToken cancellationToken);
}

public sealed partial class MySqlRepository : IAccessRepository, IWebsiteSelectionRepository
{
    private readonly string _connectionString;
    private readonly uint _timeout;
    private readonly bool _websiteSelections;
    private readonly bool _readWeaponFinishes;

    public MySqlRepository(DatabaseOptions options, bool websiteSelections = false, bool readWeaponFinishes = false)
    {
        if (!Enum.TryParse<MySqlSslMode>(options.SslMode, true, out var sslMode))
            throw new ArgumentException("Unknown Database.SslMode");
        _timeout = Math.Clamp(options.TimeoutSeconds, 1, 60);
        _websiteSelections = websiteSelections;
        _readWeaponFinishes = websiteSelections && readWeaponFinishes;
        var secret = string.IsNullOrWhiteSpace(options.PasswordEnvironmentVariable)
            ? null : Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = options.Host, Port = options.Port, Database = options.Database,
            UserID = options.Username, Password = secret ?? options.Password, SslMode = sslMode,
            ConnectionTimeout = _timeout, DefaultCommandTimeout = _timeout,
            Pooling = true, MaximumPoolSize = 3
        }.ConnectionString;
    }

    public async Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeout * 3));
        var ct = timeout.Token;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        string? serverUuid = null, databaseName = null;
        // Metadata must come from the same live connection as the snapshot.
        // MariaDB and some proxies do not expose @@server_uuid: leave identity
        // unavailable without disabling the existing CW functionality.
        try
        {
            await using var identity = new MySqlCommand("SELECT @@server_uuid, DATABASE()", connection);
            await using var reader = await identity.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && !reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                var raw = reader.GetString(0);
                if (Guid.TryParse(raw, out var uuid) && uuid != Guid.Empty)
                { serverUuid = uuid.ToString("D"); databaseName = reader.GetString(1); }
            }
        }
        catch (MySqlException) { /* Unknown or inaccessible server UUID: HUD interop fails closed. */ }
        var catalog = new List<CatalogRow>();
        await using (var command = new MySqlCommand("SELECT model, name, active FROM cw_skins WHERE model IS NOT NULL", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                catalog.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2)));

        var access = new List<AccessRow>();
        var selections = new List<WebsiteSelection>();
        var finishes = new List<SavedWeaponFinish>();
        if (steamIds.Count > 0)
        {
            await using var command = connection.CreateCommand();
            var parameters = steamIds.Distinct(StringComparer.Ordinal).Select((id, index) =>
            {
                var name = "@steam" + index;
                command.Parameters.AddWithValue(name, id);
                return name;
            }).ToArray();
            command.CommandText = $"SELECT steamid64, model, sid, expires FROM cw_access WHERE steamid64 IN ({string.Join(',', parameters)})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                access.Add(new(reader.GetString(0), reader.GetString(1), reader.GetUInt32(2), reader.GetInt64(3)));
        }
        if (_websiteSelections && steamIds.Count > 0)
        {
            await using var command = connection.CreateCommand();
            var parameters = steamIds.Distinct(StringComparer.Ordinal).Select((id, index) =>
            {
                var name = "@steam" + index; command.Parameters.AddWithValue(name, id); return name;
            }).ToArray();
            command.CommandText = $"SELECT steamid64,weapon,model,revision,weapon_team FROM cw_site_selections WHERE steamid64 IN ({string.Join(',', parameters)})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) selections.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt32(4)));
        }
        if (_readWeaponFinishes && steamIds.Count > 0)
        {
            await using var command = connection.CreateCommand();
            var parameters = steamIds.Distinct(StringComparer.Ordinal).Select((id, index) =>
            {
                var name = "@steam" + index;
                command.Parameters.AddWithValue(name, id);
                return name;
            }).ToArray();
            var definitions = string.Join(',', CustomFinish.Catalog.Select(finish => finish.Definition).Distinct());
            command.CommandText = $"SELECT steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_nametag FROM wp_player_skins WHERE steamid IN ({string.Join(',', parameters)}) AND weapon_team IN (0,2,3) AND weapon_defindex IN ({definitions})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                finishes.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        return new(catalog, access, selections, serverUuid, databaseName, finishes);
    }
}

// A failed refresh never turns an old private grant into permission for a new application.
public sealed class AccessState
{
    public DatabaseSnapshot? Snapshot { get; private set; }
    public bool Ready { get; private set; }
    public void Succeed(DatabaseSnapshot snapshot) { Snapshot = snapshot; Ready = true; }
    public void Fail() => Ready = false;
}
