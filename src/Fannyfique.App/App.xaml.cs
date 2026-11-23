using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Fannyfique.App.Services;
using Fannyfique.App.ViewModels;
using Fannyfique.App.Views;
using Fannyfique.Core;
using Fannyfique.Core.Platform;
using Fannyfique.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Fannyfique.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.Default;
        ConfigureLogging(paths);
        ConfigureCulture();
        HookUnhandledExceptions(paths);
        Log.Information("Démarrage de Fannyfique {Version}", typeof(App).Assembly.GetName().Version);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddSingleton(paths);
        services.AddSingleton(sp => Workspace.Load(paths, loggerFactory: sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IVolumeInfoProvider, WindowsVolumeInfoProvider>();
        services.AddSingleton<IFileExplorer, WindowsFileExplorer>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var workspace = _services.GetRequiredService<Workspace>();
        _services.GetRequiredService<ThemeManager>().Apply(workspace.Settings.Theme);
        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        await _services.GetRequiredService<MainViewModel>().InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Fermeture");
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "fannyfique-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    private static void ConfigureCulture()
    {
        var french = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = french;
        CultureInfo.DefaultThreadCurrentUICulture = french;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(french.IetfLanguageTag)));
    }

    private void HookUnhandledExceptions(AppPaths paths)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Exception non gérée");
            args.Handled = true;
            MessageBox.Show(
                $"Une erreur inattendue s'est produite :\n\n{args.Exception.Message}\n\nLes détails sont dans le journal :\n{paths.LogDirectory}",
                "Fannyfique",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Exception fatale");
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Exception de tâche non observée");
            args.SetObserved();
        };
    }
}
