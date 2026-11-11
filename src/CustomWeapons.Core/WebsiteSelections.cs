using System.Text.RegularExpressions;
using MySqlConnector;

namespace CustomWeapons.Core;

public sealed partial class MySqlRepository
{
    private void ValidateWebsiteSelection(string steamId, int team, string weapon, string model)
    {
        if (!_websiteSelections) throw new InvalidOperationException("Website selections are disabled");
        if (team is not (0 or 2 or 3) || !ulong.TryParse(steamId, out var account) || account < 76561197960265728UL || steamId.Length != 17 ||
            !Regex.IsMatch(weapon, "^weapon_[a-z0-9_]{1,57}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(model, "^[a-z0-9_]{0,64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid website selection identity");
    }

    public async Task<long?> SaveSelectionAsync(string steamId, int team, string weapon, string model, long expectedRevision, CancellationToken cancellationToken)
    {
        ValidateWebsiteSelection(steamId, team, weapon, model);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeout * 3)); var ct = timeout.Token;
        await using var connection = new MySqlConnection(_connectionString); await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        long revision = 0; string? current = null;
        await using (var read = new MySqlCommand("SELECT model,revision FROM cw_site_selections WHERE steamid64=@steam AND weapon_team=@team AND weapon=@weapon FOR UPDATE", connection, transaction))
        {
            read.Parameters.AddWithValue("@steam", steamId); read.Parameters.AddWithValue("@weapon", weapon);
            read.Parameters.AddWithValue("@team", team);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) { current = reader.GetString(0); revision = reader.GetInt64(1); }
        }
        if (revision != expectedRevision) return null; // Dispose rolls back; website choice wins a stale menu.
        if (current == model) { await transaction.CommitAsync(ct); return revision; }
        await using var write = new MySqlCommand("INSERT INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES (@steam,@team,@weapon,@model,@revision) ON DUPLICATE KEY UPDATE model=VALUES(model),revision=VALUES(revision)", connection, transaction);
        write.Parameters.AddWithValue("@steam", steamId); write.Parameters.AddWithValue("@weapon", weapon);
        write.Parameters.AddWithValue("@model", model); write.Parameters.AddWithValue("@revision", revision + 1);
        write.Parameters.AddWithValue("@team", team);
        await write.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); return revision + 1;
    }

    public async Task ImportSelectionsAsync(IReadOnlyCollection<WebsiteSelection> selections, CancellationToken cancellationToken)
    {
        if (selections.Count == 0) return;
        if (selections.Count > 4096) throw new ArgumentException("Too many legacy selections");
        foreach (var selection in selections) ValidateWebsiteSelection(selection.SteamId, selection.Team, selection.Weapon, selection.Model);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeout * 3)); var ct = timeout.Token;
        await using var connection = new MySqlConnection(_connectionString); await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        var rows = selections.DistinctBy(row => (row.SteamId, row.Team, row.Weapon)).OrderBy(row => row.SteamId, StringComparer.Ordinal).ThenBy(row => row.Team).ThenBy(row => row.Weapon, StringComparer.Ordinal).ToArray();
        var values = new List<string>();
        for (var i = 0; i < rows.Length; i++)
        {
            values.Add($"(@steam{i},@team{i},@weapon{i},@model{i},1)");
            command.Parameters.AddWithValue("@steam" + i, rows[i].SteamId); command.Parameters.AddWithValue("@weapon" + i, rows[i].Weapon); command.Parameters.AddWithValue("@model" + i, rows[i].Model);
            command.Parameters.AddWithValue("@team" + i, rows[i].Team);
        }
        // Connected-player migration never replaces a choice already made on the website.
        command.CommandText = "INSERT IGNORE INTO cw_site_selections (steamid64,weapon_team,weapon,model,revision) VALUES " + string.Join(',', values);
        await command.ExecuteNonQueryAsync(ct);
    }
}
