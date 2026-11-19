using MySqlConnector;
using System.Security.Cryptography;

namespace CustomWeapons.Core;

public sealed partial class MySqlRepository
{
    public async Task<bool> EquipFinishAsync(string steamId, int team, CustomFinish finish, uint serverId,
        bool pilot, CancellationToken cancellationToken)
    {
        ValidateWebsiteSelection(steamId, team, finish.Weapon, "");
        if (team is not (2 or 3) || !CustomFinish.Catalog.Contains(finish)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_timeout * 3));
        var ct = timeout.Token;
        await using var db = new MySqlConnection(_connectionString);
        await db.OpenAsync(ct);
        var lockName = "wp_player_skins:" + steamId;
        await using var gate = new MySqlCommand("SELECT GET_LOCK(@name,2)", db);
        gate.Parameters.AddWithValue("@name", lockName);
        if (Convert.ToInt32(await gate.ExecuteScalarAsync(ct)) != 1) return false;
        try
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            async Task<int> Write(string sql)
            {
                await using var command = new MySqlCommand(sql, db, tx);
                command.Parameters.AddWithValue("@steam", steamId);
                command.Parameters.AddWithValue("@team", team);
                command.Parameters.AddWithValue("@weapon", finish.Weapon);
                command.Parameters.AddWithValue("@def", finish.Definition);
                command.Parameters.AddWithValue("@key", finish.Key);
                command.Parameters.AddWithValue("@paint", finish.Paint);
                command.Parameters.AddWithValue("@label", finish.Name);
                command.Parameters.AddWithValue("@sid", serverId);
                command.Parameters.AddWithValue("@pilot", pilot);
                command.Parameters.AddWithValue("@revision", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
                return await command.ExecuteNonQueryAsync(ct);
            }
            var changed = await Write("""
                INSERT INTO wp_player_skins(steamid,weapon_team,weapon_defindex,weapon_paint_id,weapon_seed,weapon_wear,weapon_nametag)
                SELECT @steam,@team,@def,@paint,0,0.001,@label
                WHERE (SELECT COUNT(*) FROM cw_skins WHERE BINARY model=@key AND (active=1 OR @pilot=1))=1
                AND EXISTS (SELECT 1 FROM cw_access WHERE steamid64=@steam AND BINARY model=@key
                    AND sid IN (0,1,@sid) AND (expires=0 OR expires>UNIX_TIMESTAMP()))
                ON DUPLICATE KEY UPDATE weapon_paint_id=VALUES(weapon_paint_id),weapon_seed=VALUES(weapon_seed),
                    weapon_wear=VALUES(weapon_wear),weapon_nametag=VALUES(weapon_nametag)
                """);
            if (changed == 0) return false;
            await Write("""
                INSERT INTO cw_site_selections(steamid64,weapon_team,weapon,model,revision)
                VALUES(@steam,@team,@weapon,'',1)
                ON DUPLICATE KEY UPDATE revision=revision+IF(model<>'',1,0),model=''
                """);
            if (finish.Definition is >= 500 and < 600)
                await Write("""
                    INSERT INTO wp_player_knife(steamid,weapon_team,knife) VALUES(@steam,@team,@weapon)
                    ON DUPLICATE KEY UPDATE knife=VALUES(knife)
                    """);
            await Write("""
                INSERT INTO skin_refresh_queue(steamid,revision,first_requested_at,updated_at,due_at,expires_at)
                VALUES(@steam,@revision,UNIX_TIMESTAMP(),UNIX_TIMESTAMP(),UNIX_TIMESTAMP(),UNIX_TIMESTAMP()+300)
                ON DUPLICATE KEY UPDATE revision=VALUES(revision),first_requested_at=VALUES(first_requested_at),
                    updated_at=VALUES(updated_at),due_at=VALUES(due_at),expires_at=VALUES(expires_at),
                    attempts=0,state='pending',last_result='',request_id='',server_id=NULL,queued_at=0,poll_failures=0
                """);
            await tx.CommitAsync(ct);
            return true;
        }
        finally
        {
            await using var release = new MySqlCommand("SELECT RELEASE_LOCK(@name)", db);
            release.Parameters.AddWithValue("@name", lockName);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }
}
