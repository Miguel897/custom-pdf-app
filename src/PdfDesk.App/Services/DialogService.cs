using System.Windows;
using Microsoft.Win32;
using PdfDesk.App.Views;

namespace PdfDesk.App.Services;

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

public interface IDialogService
{
    void ShowError(string message);

    void ShowInfo(string message);

    bool Confirm(string message);

    SaveChoice AskSaveChanges(string documentName);

    /// <summary>Returns the chosen files, or an empty list if the user cancelled.</summary>
    IReadOnlyList<string> AskOpenPaths(string? folder, bool multiple);

    /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
    string? AskSavePath(string? folder, string fileName);

    /// <summary>Returns the password, or null if the user cancelled.</summary>
    string? AskPassword(string fileName, bool retry);

    /// <summary>Shows the Combine dialog; returns the ordered files, or null if cancelled.</summary>
    IReadOnlyList<string>? AskCombine();
}

public sealed class DialogService(Window owner) : IDialogService
{
    private const string AppName = "PdfDesk";
    private const string PdfFilter = "PDF files (*.pdf)|*.pdf";

    public void ShowError(string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public SaveChoice AskSaveChanges(string documentName) =>
        MessageBox.Show(owner, $"Save changes to {documentName}?", AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => SaveChoice.Save,
            MessageBoxResult.No => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };

    public IReadOnlyList<string> AskOpenPaths(string? folder, bool multiple)
    {
        var dialog = new OpenFileDialog { Filter = PdfFilter, Multiselect = multiple, InitialDirectory = folder ?? "" };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }

    public string? AskSavePath(string? folder, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            InitialDirectory = folder ?? "",
            FileName = fileName,
            Filter = PdfFilter,
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskPassword(string fileName, bool retry)
    {
        var dialog = new PasswordWindow(fileName, retry) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public IReadOnlyList<string>? AskCombine()
    {
        var dialog = new CombineWindow { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ViewModel.Result : null;
    }
}
