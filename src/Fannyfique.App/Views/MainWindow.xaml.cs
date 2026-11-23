using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Fannyfique.App.Services;
using Fannyfique.App.ViewModels;

namespace Fannyfique.App.Views;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private readonly MainViewModel _viewModel;
    private readonly ThemeManager _theme;
    private readonly DispatcherTimer _deviceDebounce = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(MainViewModel viewModel, ThemeManager theme)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _theme = theme;
        _deviceDebounce.Tick += (_, _) =>
        {
            _deviceDebounce.Stop();
            _viewModel.CheckRemovableVolumes();
        };
        Drop += OnDrop;
        DragOver += OnDragOver;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _theme.ApplyTitleBar(this);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    /// <summary>Branchement ou retrait d'un lecteur : on regroupe les messages avant de vérifier.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange)
        {
            _deviceDebounce.Stop();
            _deviceDebounce.Start();
        }

        return IntPtr.Zero;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.DropFolders(paths);
        }
    }
}
