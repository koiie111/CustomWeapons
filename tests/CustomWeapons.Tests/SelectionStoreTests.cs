using CustomWeapons.Core;
using Xunit;

namespace CustomWeapons.Tests;

public sealed class SelectionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cw-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(_directory, "selections.json");

    [Fact]
    public async Task SelectionsSurviveRestartAndRemainIsolated()
    {
        await using (var store = new SelectionStore(FilePath, true))
        {
            store.Set("765", "weapon_awp", "awp_animes");
            store.Set("765", "weapon_ak47", "ak_12");
            store.Set("other", "weapon_awp", "awp_insane");
            store.Disconnect("765");
        }
        await using var restored = new SelectionStore(FilePath, true);
        Assert.Equal("awp_animes", restored.Get("765", "weapon_awp"));
        Assert.Equal("ak_12", restored.Get("765", "weapon_ak47"));
        Assert.Equal("awp_insane", restored.Get("other", "weapon_awp"));
        Assert.Null(restored.Get("unknown", "weapon_awp"));
    }

    [Fact]
    public async Task LastChangeAndExplicitDefaultArePersisted()
    {
        await using (var store = new SelectionStore(FilePath, true))
        {
            for (var i = 0; i < 100; i++) store.Set("765", "weapon_awp", "skin-" + i);
            store.Set("765", "weapon_awp", null);
        }
        await using var restored = new SelectionStore(FilePath, true);
        Assert.Equal("", restored.Get("765", "weapon_awp"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public async Task SessionOnlyDoesNotReadOrWriteDisk()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "{\"765\":{\"weapon_awp\":\"persisted\"}}");
        await using (var store = new SelectionStore(FilePath, false))
        {
            Assert.Null(store.Get("765", "weapon_awp"));
            store.Set("765", "weapon_awp", "temporary");
            store.Disconnect("765");
            Assert.Null(store.Get("765", "weapon_awp"));
        }
        Assert.Contains("persisted", await File.ReadAllTextAsync(FilePath));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{\"765\":null}")]
    public async Task CorruptFileIsBackedUpBeforeReplacement(string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, content);
        var errors = new List<Exception>();
        await using (var store = new SelectionStore(FilePath, true, errors.Add)) store.Set("765", "weapon_awp", "new");
        Assert.Single(errors);
        var backup = Assert.Single(Directory.GetFiles(_directory, "*.corrupt-*"));
        Assert.Equal(content, await File.ReadAllTextAsync(backup));
        await using var restored = new SelectionStore(FilePath, true);
        Assert.Equal("new", restored.Get("765", "weapon_awp"));
    }

    [Fact]
    public async Task DiskFailureReportsErrorAndKeepsInMemoryChoice()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "block");
        var errors = new List<Exception>();
        await using (var store = new SelectionStore(Path.Combine(blocker, "selections.json"), true, errors.Add))
        {
            store.Set("765", "weapon_awp", "skin");
            Assert.Equal("skin", store.Get("765", "weapon_awp"));
        }
        Assert.Single(errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
