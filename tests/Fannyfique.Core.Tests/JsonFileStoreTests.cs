using Fannyfique.Core.Indexing;
using Fannyfique.Core.Model;
using Fannyfique.Core.Storage;
using Fannyfique.Core.Tests.Support;

namespace Fannyfique.Core.Tests;

public class JsonFileStoreTests
{
    [Fact]
    public void Aller_retour_de_l_index()
    {
        using var tree = new TempTree();
        var paths = new AppPaths(tree.Root);
        var index = new FileIndex();
        var id = Guid.NewGuid();
        index.Upsert(new IndexedFile
        {
            SourceId = id,
            RelativePath = Path.Combine("DCIM", "IMG_0001.CR2"),
            Size = 42,
            LastWriteUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTaken = new DateTime(2024, 5, 12, 14, 3, 22, 450),
            Serial = "SN",
            Status = MetadataStatus.Unreadable,
        });
        index.SetAnnexes(id, [new FolderAnnexes { SourceId = id, RelativeDirectory = "DCIM", Jpeg = 2 }]);
        index.AddHistory(new ScanSummaryEntry(DateTime.UtcNow, 10, 1, 2));

        JsonFileStore.Save(paths.IndexFile, index.ToData());
        JsonFileStore.Save(paths.IndexFile, index.ToData()); // remplace un fichier existant
        var loaded = FileIndex.FromData(JsonFileStore.Load<IndexData>(paths.IndexFile));

        var file = loaded.Get(id, Path.Combine("dcim", "img_0001.cr2"));
        Assert.NotNull(file);
        Assert.Equal(new DateTime(2024, 5, 12, 14, 3, 22, 450), file.DateTaken);
        Assert.Equal(MetadataStatus.Unreadable, file.Status);
        Assert.Equal(2, Assert.Single(loaded.Annexes).Jpeg);
        Assert.Single(loaded.History);
        Assert.False(File.Exists(paths.IndexFile + ".tmp"));
    }

    [Fact]
    public void Schema_inconnu_donne_un_index_vide()
    {
        var data = new IndexData { SchemaVersion = 999, Files = [new IndexedFile { RelativePath = "a.CR2" }] };

        Assert.Equal(0, FileIndex.FromData(data).Count);
    }

    [Fact]
    public void Aller_retour_des_preferences()
    {
        using var tree = new TempTree();
        var paths = new AppPaths(tree.Root);
        var settings = new AppSettings();
        settings.Sources.Add(new Source { Name = "Carte", Type = SourceType.SdCard, Path = "E:\\DCIM", VolumeSerial = "1234-ABCD" });

        JsonFileStore.Save(paths.SettingsFile, settings, indented: true);
        var loaded = JsonFileStore.Load<AppSettings>(paths.SettingsFile)!;

        var source = Assert.Single(loaded.Sources);
        Assert.Equal((settings.Sources[0].Id, SourceType.SdCard, "1234-ABCD"), (source.Id, source.Type, source.VolumeSerial));
        Assert.Contains("\"SdCard\"", File.ReadAllText(paths.SettingsFile));
        Assert.Equal(2, loaded.ParallelismFor(SourceType.Nas));
    }

    [Fact]
    public void Fichier_absent()
    {
        Assert.Null(JsonFileStore.Load<AppSettings>("/nexiste/pas.json"));
    }
}
