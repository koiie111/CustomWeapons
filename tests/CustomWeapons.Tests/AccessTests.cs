using CustomWeapons.Core;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class AccessTests
{
    [Theory]
    [InlineData(1, 1, 0, true)]
    [InlineData(1, 0, 0, true)]
    [InlineData(0, 42, 0, true)]
    [InlineData(1, 2, 0, false)]
    [InlineData(1, 1, 1001, true)]
    [InlineData(1, 1, 1000, false)]
    [InlineData(1, 1, 999, false)]
    [InlineData(0, 0, 999, false)]
    [InlineData(1, 1, 4294967295, true)]
    public void ServerAndExpiryRules(uint server, uint sid, long expires, bool expected) =>
        Assert.Equal(expected, SkinRules.HasAccess([new("765", "skin", sid, expires)], "765", "skin", server, 1000));

    [Fact]
    public void OtherPlayerOrSkinNeverGrantsAccess()
    {
        AccessRow[] rows = [new("other", "skin", 0, 0), new("765", "other", 0, 0)];
        Assert.False(SkinRules.HasAccess(rows, "765", "skin", 0, 1));
    }

    [Fact]
    public void ValidDuplicateWinsOverExpiredDuplicate() =>
        Assert.True(SkinRules.HasAccess([new("765", "skin", 1, 1), new("765", "skin", 1, 0)], "765", "skin", 1, 1000));

    [Fact]
    public void MissingCatalogRowAllowsConfiguredPrivateSkinWithGrant()
    {
        var snapshot = new DatabaseSnapshot([], [new("765", "skin", 0, 0)]);
        Assert.True(SkinRules.CanApply(new(), "skin", "765", 1, 1000, true, snapshot));
    }

    [Fact]
    public void DatabaseFailureDeniesNewPrivateApplicationsButRetainsCatalog()
    {
        var state = new AccessState();
        state.Succeed(new([new("disabled", "Name", 0)], [new("765", "skin", 0, 0)]));
        Assert.True(SkinRules.CanApply(new(), "skin", "765", 1, 1000, state.Ready, state.Snapshot));
        state.Fail();
        Assert.False(SkinRules.CanApply(new(), "skin", "765", 1, 1000, state.Ready, state.Snapshot));
        Assert.False(SkinRules.CanApply(new() { Private = false }, "disabled", "765", 1, 1000, state.Ready, state.Snapshot));
        Assert.True(SkinRules.CanApply(new() { Private = false }, "public", "765", 1, 1000, state.Ready, state.Snapshot));
        state.Succeed(new([], []));
        Assert.True(state.Ready);
        Assert.False(SkinRules.CanApply(new(), "skin", "765", 1, 1000, state.Ready, state.Snapshot));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void NonActiveCatalogEntryDeniesEvenPublicSkins(int active) =>
        Assert.False(SkinRules.CanApply(new() { Private = false }, "skin", "765", 0, 1000, true,
            new([new("skin", "Name", active)], [])));

    [Fact]
    public void ConflictingDuplicateCatalogRowsAreDisabled()
    {
        var snapshot = new DatabaseSnapshot([new("skin", "First", 1), new("skin", "Second", 1)], []);
        Assert.Equal(["skin"], snapshot.Conflicts);
        Assert.False(snapshot.Catalog["skin"].Active);
        var identical = new DatabaseSnapshot([new("skin", "First", 1), new("skin", "First", 1)], []);
        Assert.True(identical.Catalog["skin"].Active);
    }

    [Theory]
    [InlineData("Database name", "Config name", "Database name")]
    [InlineData(null, "Config name", "Config name")]
    [InlineData(" ", "", "skin")]
    public void DisplayNamesHaveDefinedFallbacks(string? dbName, string configName, string expected) =>
        Assert.Equal(expected, SkinRules.DisplayName(new() { Name = configName }, "skin", new([new("skin", dbName, 1)], [])));
}
