using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fannyfique.App.Services;
using Fannyfique.Core;
using Fannyfique.Core.Formatting;
using Fannyfique.Core.Model;
using Fannyfique.Core.Platform;

namespace Fannyfique.App.ViewModels;

/// <summary>Écran Sources (CLAUDE.md 7.2).</summary>
public sealed partial class SourcesViewModel(
    Workspace workspace,
    IVolumeInfoProvider volumes,
    IFileExplorer explorer,
    IDialogService dialogs,
    MainViewModel main) : ObservableObject
{

    public ObservableCollection<SourceItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public void Refresh(IReadOnlySet<Guid> offline)
    {
        var stats = workspace.StatsBySource();
        Items.Clear();
        foreach (var source in workspace.Settings.Sources)
        {
            Items.Add(new SourceItemViewModel(source, !offline.Contains(source.Id), stats.GetValueOrDefault(source.Id), this));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void AddFolders() => AddPaths(dialogs.PickFolders());

    public void AddPaths(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (workspace.Settings.Sources.Any(s => string.Equals(s.Path, full, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var (type, serial, label) = Guess(full);
            var name = Path.GetFileName(full) is { Length: > 0 } folder && !folder.Equals("DCIM", StringComparison.OrdinalIgnoreCase)
                ? folder
                : label ?? full;
            workspace.AddSource(full, type, name, serial, LooksRecovered(full));
            added = true;
        }

        if (added)
        {
            Refresh(workspace.OfflineSourceIds);
        }
    }

    /// <summary>Type proposé selon le lecteur (l'utilisateur•ice peut le changer).</summary>
    private (SourceType Type, string? Serial, string? Label) Guess(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return (SourceType.Nas, null, null);
        }

        var volume = volumes.GetVolume(path);
        if (volume is { IsRemovable: true })
        {
            var type = Directory.Exists(Path.Combine(volume.RootPath, "DCIM")) ? SourceType.SdCard : SourceType.ExternalDisk;
            return (type, volume.Serial, volume.Label);
        }

        // Disque interne ou externe USB (souvent vu comme « fixe ») : PC par défaut. Le numéro de série
        // n'est gardé que hors disque système, pour suivre un disque externe qui change de lettre.
        var isSystemDrive = string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
        return (SourceType.Pc, isSystemDrive ? null : volume?.Serial, volume?.Label);
    }

    /// <summary>
    /// Dossier issu d'une récupération de carte (PhotoRec : <c>recup_dir.1</c>, <c>recup_dir.2</c>…) : les noms de
    /// fichiers n'y sont pas d'origine. L'option reste modifiable sur la tuile de la source.
    /// </summary>
    private static bool LooksRecovered(string path)
    {
        static bool IsRecupDir(string? name) => name?.StartsWith("recup_dir", StringComparison.OrdinalIgnoreCase) == true;
        if (IsRecupDir(Path.GetFileName(path)))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateDirectories(path).Any(d => IsRecupDir(Path.GetFileName(d)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal async Task ChangedAsync()
    {
        workspace.SaveSettings();
        await main.RefreshAnalysisAsync(refreshSources: false);
    }

    internal async Task RemoveAsync(SourceItemViewModel item)
    {
        if (!dialogs.Confirm(
            "Retirer la source",
            $"Retirer « {item.Name} » de la liste des sources ?\n\nAucun fichier ne sera supprimé : la source est seulement oubliée par Fannyfique."))
        {
            return;
        }

        await workspace.RemoveSourceAsync(item.Source);
        await main.RefreshAnalysisAsync();
    }

    internal void Open(SourceItemViewModel item) => explorer.OpenFolder(item.Path);
}

public sealed partial class SourceItemViewModel : ObservableObject
{
    private readonly SourcesViewModel _owner;

    public SourceItemViewModel(Source source, bool online, (int Count, long Bytes) stats, SourcesViewModel owner)
    {
        Source = source;
        IsOnline = online;
        _owner = owner;
        _name = source.Name;
        _type = source.Type;
        _enabled = source.Enabled;
        _ignoreFileNames = source.IgnoreFileNames;
        StatsText = stats.Count == 0 ? "Pas encore scannée" : $"{Format.Plural(stats.Count, "RAW")} · {Format.Bytes(stats.Bytes)}";
    }

    public Source Source { get; }

    public string Path => Source.Path;

    public bool IsOnline { get; }

    public string StateText => IsOnline ? "En ligne" : $"Hors ligne — vu le {Format.Date(Source.LastScanUtc?.ToLocalTime())}";

    public string StatsText { get; }

    public string LastScanText => Source.LastScanUtc is { } d ? $"Dernier scan : {Format.DateTime(d.ToLocalTime())}" : "Jamais scannée";

    public IReadOnlyList<SourceTypeOption> TypeOptions => SourceTypeOption.All;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private SourceType _type;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _ignoreFileNames;

    partial void OnNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Source.Name = value.Trim();
            _ = _owner.ChangedAsync();
        }
    }

    partial void OnTypeChanged(SourceType value)
    {
        Source.Type = value;
        _ = _owner.ChangedAsync();
    }

    partial void OnEnabledChanged(bool value)
    {
        Source.Enabled = value;
        _ = _owner.ChangedAsync();
    }

    partial void OnIgnoreFileNamesChanged(bool value)
    {
        // Seule l'analyse change : les métadonnées sont déjà dans l'index, pas besoin de re-scanner.
        Source.IgnoreFileNames = value;
        _ = _owner.ChangedAsync();
    }

    [RelayCommand]
    private void Open() => _owner.Open(this);

    [RelayCommand]
    private Task RemoveAsync() => _owner.RemoveAsync(this);
}
