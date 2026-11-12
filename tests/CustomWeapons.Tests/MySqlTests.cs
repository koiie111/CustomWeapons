using CustomWeapons.Core;
using MySqlConnector;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class MySqlIntegrationFactAttribute : FactAttribute
{
    public MySqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CW_TEST_MYSQL")))
            Skip = "Set CW_TEST_MYSQL to a disposable cw_test_* database (CI runs this test).";
    }
}

public sealed class MySqlTests
{
    [Fact]
    public async Task UnreachableDatabaseFailsInsteadOfGrantingAccess()
    {
        var repository = new MySqlRepository(new()
        {
            Host = "127.0.0.1", Port = 1, Username = "invalid", PasswordEnvironmentVariable = "", TimeoutSeconds = 1
        });
        await Assert.ThrowsAnyAsync<Exception>(() => repository.ReadAsync(["765"], CancellationToken.None));
    }

    [MySqlIntegrationFact]
    public async Task ReadsExistingSchemaWithReadOnlyAccountAndParameterizedSteamIds()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CW_TEST_MYSQL")!);
        Assert.StartsWith("cw_test_", builder.Database);
        await using var setup = new MySqlConnection(builder.ConnectionString);
        await setup.OpenAsync();
        // Only the disposable test database is initialized. Legacy mode stays read-only.
        var readerUser = "cw_read_" + Guid.NewGuid().ToString("N")[..8];
        var writerUser = "cw_write_" + Guid.NewGuid().ToString("N")[..8];
        await using var create = setup.CreateCommand();
        create.CommandText = $$"""
            CREATE TABLE cw_access (id int NOT NULL AUTO_INCREMENT PRIMARY KEY, steamid64 varchar(64) NOT NULL,
              model varchar(64) NOT NULL, sid int unsigned NOT NULL DEFAULT 1, expires int unsigned NOT NULL DEFAULT 0)
              ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
            CREATE TABLE cw_skins (id int NOT NULL AUTO_INCREMENT PRIMARY KEY, name varchar(100) DEFAULT NULL,
              model varchar(255) DEFAULT NULL, price int NOT NULL DEFAULT 0, fake_price int NOT NULL DEFAULT 0,
              is_discount int NOT NULL DEFAULT 0, type varchar(100) DEFAULT NULL, video varchar(255) DEFAULT NULL,
              img varchar(255) DEFAULT NULL, active int NOT NULL DEFAULT 1) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3;
            INSERT INTO cw_skins (name,model) VALUES ('AWP - Перлика','awp_animes');
            INSERT INTO cw_access (steamid64,model,sid,expires) VALUES ('765','awp_animes',0,4294967295),('other','ak_12',1,0);
            CREATE USER '{{readerUser}}'@'%' IDENTIFIED BY 'test-only-password';
            """;
        await create.ExecuteNonQueryAsync();
        // Identifier is not user input: test databases are fixed by CI; validate before quoting.
        Assert.Matches("^cw_test_[a-z0-9_]+$", builder.Database);
        create.CommandText = $"GRANT SELECT ON `{builder.Database}`.* TO '{readerUser}'@'%'";
        await create.ExecuteNonQueryAsync();
        try {
        var repository = new MySqlRepository(new()
        {
            Host = builder.Server, Port = builder.Port, Database = builder.Database,
            Username = readerUser, Password = "test-only-password", PasswordEnvironmentVariable = "", SslMode = "Disabled"
        });
        var snapshot = await repository.ReadAsync(["765", "' OR 1=1 --"], CancellationToken.None);
        Assert.Equal("AWP - Перлика", snapshot.Catalog["awp_animes"].Name);
        Assert.Single(snapshot.Access);
        Assert.Equal(4294967295, Assert.Single(snapshot.Access["765"]).Expires);
        Assert.True(SkinRules.HasAccess(snapshot.Access["765"], "765", "awp_animes", 42, 1000));
        var noPlayers = await repository.ReadAsync([], CancellationToken.None);
        Assert.Empty(noPlayers.Access);
        Assert.Single(noPlayers.Catalog);
        create.CommandText = """
            CREATE TABLE cw_site_selections (
              steamid64 varchar(18) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
              weapon_team tinyint unsigned NOT NULL DEFAULT 0,
              weapon varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
              model varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '',
              revision bigint NOT NULL DEFAULT 1,
              updated_at timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
              PRIMARY KEY(steamid64,weapon_team,weapon),CHECK(weapon_team IN(0,2,3)),CHECK(revision>0)) ENGINE=InnoDB;
            """;
        await create.ExecuteNonQueryAsync();
        var options = new DatabaseOptions { Host=builder.Server, Port=builder.Port, Database=builder.Database,
            Username=readerUser, Password="test-only-password", PasswordEnvironmentVariable="", SslMode="Disabled" };
        var readOnly = new MySqlRepository(options, true);
        const string steam = "76561198000000001";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SaveSelectionAsync(steam,3,"weapon_ak47","gun",0,CancellationToken.None));
        await Assert.ThrowsAsync<MySqlException>(()=>readOnly.SaveSelectionAsync(steam,3,"weapon_ak47","gun",0,CancellationToken.None));
        create.CommandText = $"CREATE USER '{writerUser}'@'%' IDENTIFIED BY 'test-only-password'; GRANT SELECT ON `{builder.Database}`.* TO '{writerUser}'@'%'; GRANT INSERT,UPDATE ON `{builder.Database}`.cw_site_selections TO '{writerUser}'@'%'";
        await create.ExecuteNonQueryAsync();options.Username=writerUser;
        var website = new MySqlRepository(options,true);
        Assert.Equal(1,await website.SaveSelectionAsync(steam,3,"weapon_ak47","gun",0,CancellationToken.None));
        Assert.Equal(1,await website.SaveSelectionAsync(steam,3,"weapon_ak47","gun",1,CancellationToken.None));
        Assert.Null(await website.SaveSelectionAsync(steam,3,"weapon_ak47","other",0,CancellationToken.None));
        await website.ImportSelectionsAsync([new(steam,"weapon_ak47","legacy",1),new(steam,"weapon_ak47","ignored",1,3)],CancellationToken.None);
        var selected=await website.ReadAsync([steam,"' OR 1=1 --"],CancellationToken.None);
        Assert.Equal("gun",selected.WebsiteSelections[(steam,3,"weapon_ak47")].Model);
        Assert.Equal("legacy",selected.WebsiteSelections[(steam,0,"weapon_ak47")].Model);
        Assert.Empty((await repository.ReadAsync([steam],CancellationToken.None)).WebsiteSelections);
        Assert.Empty((await website.ReadAsync(["76561198000000002"],CancellationToken.None)).WebsiteSelections);
        Assert.Equal(1,await website.SaveSelectionAsync(steam,2,"weapon_ak47","terrorist",0,CancellationToken.None));
        var race=await Task.WhenAll(website.SaveSelectionAsync(steam,3,"weapon_ak47","race_a",1,CancellationToken.None),website.SaveSelectionAsync(steam,3,"weapon_ak47","race_b",1,CancellationToken.None));
        Assert.Single(race,value=>value==2);Assert.Single(race,value=>value==null);
        Assert.Equal(3,await website.SaveSelectionAsync(steam,3,"weapon_ak47","",2,CancellationToken.None));
        selected=await website.ReadAsync([steam],CancellationToken.None);
        Assert.Equal("",selected.WebsiteSelections[(steam,3,"weapon_ak47")].Model);
        Assert.Equal("terrorist",selected.WebsiteSelections[(steam,2,"weapon_ak47")].Model);
        foreach(var bad in new[]{"../bad","models/a.vmdl","' OR 1=1 --"})
            await Assert.ThrowsAsync<ArgumentException>(()=>website.SaveSelectionAsync(steam,3,"weapon_ak47",bad,3,CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(()=>website.SaveSelectionAsync(steam,1,"weapon_ak47","gun",0,CancellationToken.None));
        await website.ImportSelectionsAsync([new("not-a-steamid", "weapon_ak47", "bad", 1),
            new(steam, "weapon_awp", "AK-12", 1), new(steam, "weapon_ssg08", "../bad", 1)], CancellationToken.None);
        var imported = await website.ReadAsync([steam], CancellationToken.None);
        Assert.Equal("AK-12", imported.WebsiteSelections[(steam, 0, "weapon_awp")].Model);
        Assert.False(imported.WebsiteSelections.ContainsKey((steam, 0, "weapon_ssg08")));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>website.SaveSelectionAsync(steam,3,"weapon_ak47","gun",3,cancelled.Token));
        var restricted = new MySqlConnectionStringBuilder(builder.ConnectionString){UserID=writerUser,Password="test-only-password"};
        await using var writerConnection=new MySqlConnection(restricted.ConnectionString);await writerConnection.OpenAsync();
        await using var forbidden=writerConnection.CreateCommand();forbidden.CommandText="UPDATE cw_access SET expires=0";
        await Assert.ThrowsAsync<MySqlException>(()=>forbidden.ExecuteNonQueryAsync());
        } finally {
            create.CommandText=$"DROP USER IF EXISTS '{readerUser}'@'%', '{writerUser}'@'%'";await create.ExecuteNonQueryAsync();
        }
    }
}
