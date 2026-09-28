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
        // Only the disposable test database is initialized. Production repository contains SELECT only.
        await using var create = setup.CreateCommand();
        create.CommandText = """
            CREATE TABLE cw_access (id int NOT NULL AUTO_INCREMENT PRIMARY KEY, steamid64 varchar(64) NOT NULL,
              model varchar(64) NOT NULL, sid int unsigned NOT NULL DEFAULT 1, expires int unsigned NOT NULL DEFAULT 0)
              ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
            CREATE TABLE cw_skins (id int NOT NULL AUTO_INCREMENT PRIMARY KEY, name varchar(100) DEFAULT NULL,
              model varchar(255) DEFAULT NULL, price int NOT NULL DEFAULT 0, fake_price int NOT NULL DEFAULT 0,
              is_discount int NOT NULL DEFAULT 0, type varchar(100) DEFAULT NULL, video varchar(255) DEFAULT NULL,
              img varchar(255) DEFAULT NULL, active int NOT NULL DEFAULT 1) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3;
            INSERT INTO cw_skins (name,model) VALUES ('AWP - Перлика','awp_animes');
            INSERT INTO cw_access (steamid64,model,sid,expires) VALUES ('765','awp_animes',0,4294967295),('other','ak_12',1,0);
            CREATE USER 'cw_reader'@'%' IDENTIFIED BY 'test-only-password';
            """;
        await create.ExecuteNonQueryAsync();
        // Identifier is not user input: test databases are fixed by CI; validate before quoting.
        Assert.Matches("^cw_test_[a-z0-9_]+$", builder.Database);
        create.CommandText = $"GRANT SELECT ON `{builder.Database}`.* TO 'cw_reader'@'%'";
        await create.ExecuteNonQueryAsync();
        var repository = new MySqlRepository(new()
        {
            Host = builder.Server, Port = builder.Port, Database = builder.Database,
            Username = "cw_reader", Password = "test-only-password", PasswordEnvironmentVariable = "", SslMode = "Disabled"
        });
        var snapshot = await repository.ReadAsync(["765", "' OR 1=1 --"], CancellationToken.None);
        Assert.Equal("AWP - Перлика", snapshot.Catalog["awp_animes"].Name);
        Assert.Single(snapshot.Access);
        Assert.Equal(4294967295, Assert.Single(snapshot.Access["765"]).Expires);
        Assert.True(SkinRules.HasAccess(snapshot.Access["765"], "765", "awp_animes", 42, 1000));
        var noPlayers = await repository.ReadAsync([], CancellationToken.None);
        Assert.Empty(noPlayers.Access);
        Assert.Single(noPlayers.Catalog);
    }
}
