using MySqlConnector;

namespace CustomWeapons.Core;

public interface IAccessRepository
{
    Task<DatabaseSnapshot> ReadAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken);
}

public sealed class MySqlRepository : IAccessRepository
{
    private readonly string _connectionString;
    private readonly uint _timeout;

    public MySqlRepository(DatabaseOptions options)
    {
        if (!Enum.TryParse<MySqlSslMode>(options.SslMode, true, out var sslMode))
            throw new ArgumentException("Unknown Database.SslMode");
        _timeout = Math.Clamp(options.TimeoutSeconds, 1, 60);
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
        var catalog = new List<CatalogRow>();
        await using (var command = new MySqlCommand("SELECT model, name, active FROM cw_skins WHERE model IS NOT NULL", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                catalog.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2)));

        var access = new List<AccessRow>();
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
        return new(catalog, access);
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
