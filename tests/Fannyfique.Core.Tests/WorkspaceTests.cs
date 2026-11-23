using Fannyfique.Core.Model;
using Fannyfique.Core.Scanning;
using Fannyfique.Core.Storage;
using Fannyfique.Core.Tests.Support;

namespace Fannyfique.Core.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private static readonly DateTime Shoot = new(2024, 5, 12, 14, 0, 0);
    private readonly TempTree _tree = new();
    private readonly AppPaths _paths;

    public WorkspaceTests()
    {
        _paths = new AppPaths(_tree["data"]);
        TestRaw.Write(_tree["sd/DCIM/IMG_0001.CR2"], Shoot);
        TestRaw.Write(_tree["sd/DCIM/IMG_0002.CR2"], Shoot.AddSeconds(1));
        TestRaw.Write(_tree["nas/2024/IMG_0001.CR2"], Shoot);
    }

    public void Dispose() => _tree.Dispose();

    [Fact]
    public async Task Scan_analyse_et_persistance()
    {
        var workspace = Workspace.Load(_paths);
        var sd = workspace.AddSource(_tree["sd"] + Path.DirectorySeparatorChar, SourceType.SdCard, volumeSerial: "ABCD");
        workspace.AddSource(_tree["nas"], SourceType.Nas, "Mon NAS");

        var outcome = await workspace.ScanAsync(new ScanProgress());

        Assert.Equal("sd", sd.Name);
        Assert.Equal(_tree["sd"], sd.Path);
        var verdict = Assert.Single(outcome.Analysis.SdCards);
        Assert.Single(verdict.OnlyHere);
        Assert.Null(outcome.Previous);

        var reloaded = Workspace.Load(_paths);
        Assert.Equal(2, reloaded.Settings.Sources.Count);
        Assert.Equal(3, reloaded.Index.Count);
        Assert.Same(reloaded.Settings.Sources[0], reloaded.FindByVolume("abcd"));
        Assert.Single(reloaded.Index.History);
        Assert.Equal((2, reloaded.StatsFor(reloaded.Settings.Sources[0]).Bytes), reloaded.StatsFor(reloaded.Settings.Sources[0]));
    }

    [Fact]
    public async Task Progres_depuis_le_scan_precedent()
    {
        var workspace = Workspace.Load(_paths);
        workspace.AddSource(_tree["sd"], SourceType.SdCard);
        workspace.AddSource(_tree["nas"], SourceType.Nas);
        var first = await workspace.ScanAsync(new ScanProgress());
        Assert.True(first.Current.RecoverableBytes > 0);

        File.Delete(_tree["sd/DCIM/IMG_0001.CR2"]);
        var second = await workspace.ScanAsync(new ScanProgress());

        Assert.Equal(first.Current.RecoverableBytes, second.FreedBytes);
    }

    [Fact]
    public async Task Retirer_une_source_la_retire_de_l_index_sans_toucher_aux_fichiers()
    {
        var workspace = Workspace.Load(_paths);
        var sd = workspace.AddSource(_tree["sd"], SourceType.SdCard);
        workspace.AddSource(_tree["nas"], SourceType.Nas);
        await workspace.ScanAsync(new ScanProgress());

        await workspace.RemoveSourceAsync(sd);

        Assert.Equal(1, workspace.Index.Count);
        Assert.True(File.Exists(_tree["sd/DCIM/IMG_0001.CR2"]));
    }

    [Fact]
    public void Etat_hors_ligne_au_demarrage()
    {
        var workspace = Workspace.Load(_paths);
        var sd = workspace.AddSource(_tree["sd"], SourceType.SdCard);
        var nas = workspace.AddSource(_tree["absent"], SourceType.Nas);

        workspace.RefreshOnlineState();

        Assert.Equal([nas.Id], workspace.OfflineSourceIds);
    }

    [Fact]
    public void Un_fichier_corrompu_est_mis_de_cote()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.SettingsFile, "{ pas du json");

        var workspace = Workspace.Load(_paths);

        Assert.Empty(workspace.Settings.Sources);
        Assert.Single(Directory.GetFiles(_paths.DataDirectory, "fannyfique.json.corrompu-*"));
    }
}
