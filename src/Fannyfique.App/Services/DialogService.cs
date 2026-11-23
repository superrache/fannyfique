using System.Windows;
using Microsoft.Win32;

namespace Fannyfique.App.Services;

public interface IDialogService
{
    IReadOnlyList<string> PickFolders();

    bool Confirm(string title, string message);

    void Error(string title, string message);
}

public sealed class DialogService : IDialogService
{
    public IReadOnlyList<string> PickFolders()
    {
        var dialog = new OpenFolderDialog { Title = "Ajouter des dossiers à scanner", Multiselect = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderNames : [];
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public void Error(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
