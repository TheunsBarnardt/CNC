using Backend.Models;
using Backend.Services;
using Xunit;

namespace Backend.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cnc-profile-tests-" + Guid.NewGuid());
    private string StorePath => Path.Combine(_dir, "profiles.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp cleanup */ }
    }

    private static MaterialProfile Make(string name = "Steel 3mm") => new()
    {
        Name = name,
        Material = "Mild steel",
        ThicknessMm = 3,
        KerfWidthMm = 1.5,
        FeedRateMmMin = 2200,
        PierceDelayS = 0.5,
        CutHeightMm = 1.5,
        PierceHeightMm = 3.8,
    };

    [Fact]
    public void FreshStore_SeedsExampleProfiles()
    {
        var store = new ProfileStore(StorePath);
        Assert.NotEmpty(store.All());
        Assert.All(store.All(), p => Assert.Contains("example", p.Name));
        Assert.True(File.Exists(StorePath), "seed profiles must be persisted");
    }

    [Fact]
    public void AddUpdateDelete_PersistAcrossReload()
    {
        var store = new ProfileStore(StorePath);
        var profile = Make();
        store.Add(profile);

        // Reload from disk — the added profile survives.
        var reloaded = new ProfileStore(StorePath);
        var found = reloaded.Find(profile.Id);
        Assert.NotNull(found);
        Assert.Equal("Steel 3mm", found.Name);
        Assert.Equal(2200, found.FeedRateMmMin);

        found.FeedRateMmMin = 2400;
        Assert.True(reloaded.Update(found));
        Assert.Equal(2400, new ProfileStore(StorePath).Find(profile.Id)!.FeedRateMmMin);

        Assert.True(reloaded.Delete(profile.Id));
        Assert.Null(new ProfileStore(StorePath).Find(profile.Id));
    }

    [Fact]
    public void Update_UnknownId_ReturnsFalse()
    {
        var store = new ProfileStore(StorePath);
        Assert.False(store.Update(Make()));
        Assert.False(store.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void All_ReturnsCopies_MutationDoesNotLeakIn()
    {
        var store = new ProfileStore(StorePath);
        var profile = Make();
        store.Add(profile);

        // Mutating the returned object must not silently change the store.
        var copy = store.Find(profile.Id)!;
        copy.FeedRateMmMin = 9999;
        Assert.Equal(2200, store.Find(profile.Id)!.FeedRateMmMin);
    }

    [Fact]
    public void ImportJson_MergesById()
    {
        var store = new ProfileStore(StorePath);
        var profile = Make();
        store.Add(profile);
        int before = store.All().Count;

        // Export, tweak the known profile + add a new one, re-import.
        var exported = store.ExportJson();
        var changed = profile.Id;
        var json = exported.Replace("\"Steel 3mm\"", "\"Steel 3mm v2\"");

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        int imported = store.ImportJson(stream);

        Assert.Equal(before, imported);
        Assert.Equal(before, store.All().Count); // replaced, not duplicated
        Assert.Equal("Steel 3mm v2", store.Find(changed)!.Name);
    }

    [Fact]
    public void ImportJson_BadInput_Throws()
    {
        var store = new ProfileStore(StorePath);
        using var stream = new MemoryStream("not json"u8.ToArray());
        Assert.Throws<FormatException>(() => store.ImportJson(stream));
    }

    [Fact]
    public void CorruptFile_IsSetAside_AndStoreStartsFresh()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StorePath, "{ definitely broken");
        var store = new ProfileStore(StorePath);
        Assert.NotEmpty(store.All()); // reseeded
        Assert.True(File.Exists(StorePath + ".corrupt"), "broken file kept for the user");
    }
}
