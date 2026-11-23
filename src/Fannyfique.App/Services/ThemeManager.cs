using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Fannyfique.Core.Storage;
using Microsoft.Win32;

namespace Fannyfique.App.Services;

/// <summary>Thème clair ou sombre, qui suit Windows en mode automatique (CLAUDE.md 7.1).</summary>
public sealed class ThemeManager
{
    private AppTheme _theme = AppTheme.Auto;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _theme == AppTheme.Auto)
            {
                Application.Current.Dispatcher.Invoke(() => Apply(_theme));
            }
        };
    }

    public static event EventHandler? ThemeChanged;

    public bool IsDark { get; private set; }

    public void Apply(AppTheme theme)
    {
        _theme = theme;
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.Auto && !WindowsUsesLightTheme());
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Fannyfique;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml"),
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = palette;
        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Barre de titre sombre (Windows 10 20H1 et suivants).</summary>
    public void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
