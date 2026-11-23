using System.Diagnostics;
using Fannyfique.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Fannyfique.App.Services;

/// <summary>Ouvre l'Explorateur Windows sur un dossier ou un fichier.</summary>
public sealed class WindowsFileExplorer(ILogger<WindowsFileExplorer> logger) : IFileExplorer
{
    public void OpenFolder(string path) => Start($"\"{path}\"");

    public void SelectFile(string path) => Start($"/select,\"{path}\"");

    private void Start(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible d'ouvrir l'Explorateur : {Arguments}", arguments);
        }
    }
}
