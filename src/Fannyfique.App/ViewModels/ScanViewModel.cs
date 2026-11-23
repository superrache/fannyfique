using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fannyfique.Core.Formatting;
using Fannyfique.Core.Model;
using Fannyfique.Core.Scanning;

namespace Fannyfique.App.ViewModels;

/// <summary>Écran de suivi du scan (CLAUDE.md 5.4) : lit l'avancement 10 fois par seconde.</summary>
public sealed partial class ScanViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _readingClock = new();
    private ScanProgress? _progress;
    private CancellationTokenSource? _cts;
    private int _readAtStart;

    public ScanViewModel()
    {
        _timer.Tick += (_, _) => Update();
    }

    public ObservableCollection<ScanSourceItem> SourceItems { get; } = [];

    [ObservableProperty]
    private string _phaseText = "";

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _mainCounter = "";

    [ObservableProperty]
    private string _detailText = "";

    [ObservableProperty]
    private string _currentPath = "";

    [ObservableProperty]
    private string? _errorsText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

    public void Start(ScanProgress progress, IEnumerable<Source> sources, CancellationTokenSource cts)
    {
        _progress = progress;
        _cts = cts;
        IsCancelling = false;
        _readingClock.Reset();
        SourceItems.Clear();
        foreach (var source in sources.Where(s => s.Enabled))
        {
            SourceItems.Add(new ScanSourceItem(source));
        }

        Update();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Update();
        _progress = null;
        _cts = null;
    }

    private bool CanCancel() => !IsCancelling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelling = true;
        PhaseText = "Annulation…";
        _cts?.Cancel();
    }

    private void Update()
    {
        if (_progress is null)
        {
            return;
        }

        var s = _progress.Snapshot();
        foreach (var item in SourceItems)
        {
            item.Update(s.Sources.FirstOrDefault(p => p.SourceId == item.Source.Id));
        }

        ErrorsText = s.ErrorCount == 0 ? null : Format.Plural(s.ErrorCount, "erreur");
        if (IsCancelling)
        {
            return;
        }

        switch (s.Phase)
        {
            case ScanPhase.NotStarted:
            case ScanPhase.Exploring:
                PhaseText = "Exploration des dossiers";
                IsIndeterminate = true;
                MainCounter = Format.Plural(s.RawFound, "RAW trouvé", "RAW trouvés");
                DetailText = Format.Plural(s.DirectoriesExplored, "dossier parcouru", "dossiers parcourus");
                CurrentPath = s.CurrentDirectory ?? "";
                break;

            case ScanPhase.Reading:
                if (!_readingClock.IsRunning)
                {
                    _readingClock.Start();
                    _readAtStart = s.Read;
                }

                PhaseText = "Lecture des métadonnées";
                CurrentPath = "";
                if (s.ToRead == 0)
                {
                    IsIndeterminate = true;
                    MainCounter = "Rien de nouveau";
                    DetailText = $"{Format.Count(s.FromCache)} RAW déjà connus";
                    break;
                }

                IsIndeterminate = false;
                ProgressValue = (double)s.Read / s.ToRead;
                MainCounter = $"{Format.Count(s.Read)} / {Format.Count(s.ToRead)}";
                var elapsed = _readingClock.Elapsed.TotalSeconds;
                var rate = elapsed > 1 ? (s.Read - _readAtStart) / elapsed : 0;
                var remaining = rate > 0 ? Format.Duration(TimeSpan.FromSeconds((s.ToRead - s.Read) / rate)) : "…";
                DetailText = $"{rate:0} fichiers/s · encore {remaining} · {Format.Count(s.FromCache)} déjà connus";
                break;

            case ScanPhase.Analyzing:
                PhaseText = "Recherche des doublons";
                IsIndeterminate = true;
                MainCounter = Format.Plural(s.RawFound, "RAW", "RAW");
                DetailText = "";
                break;

            default:
                PhaseText = "Terminé";
                IsIndeterminate = false;
                ProgressValue = 1;
                break;
        }
    }
}

public sealed partial class ScanSourceItem(Source source) : ObservableObject
{
    public Source Source { get; } = source;

    public string Name => Source.Name;

    public SourceType Type => Source.Type;

    [ObservableProperty]
    private string _stateText = "En attente";

    [ObservableProperty]
    private string _countersText = "";

    [ObservableProperty]
    private SourceScanState _state;

    public void Update(SourceProgressSnapshot? p)
    {
        if (p is null)
        {
            return;
        }

        State = p.State;
        StateText = p.State switch
        {
            SourceScanState.Exploring => "Exploration…",
            SourceScanState.Reading => "Lecture…",
            SourceScanState.Done => "Terminé",
            SourceScanState.Offline => "Hors ligne",
            _ => "En attente",
        };
        var counters = $"{Format.Plural(p.RawFound, "RAW", "RAW")} · {Format.Count(p.Read)} lus · {Format.Count(p.FromCache)} en cache";
        CountersText = p.Errors > 0 ? $"{counters} · {Format.Plural(p.Errors, "erreur")}" : counters;
    }
}
