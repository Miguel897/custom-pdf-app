using System.Windows;
using PdfDesk.App.Views;
using PdfDesk.Core;
using PdfDesk.Core.Settings;

namespace PdfDesk.App;

/// <summary>
/// No single-instance lock: every "Open with" starts its own window (PRD Q3). Windows of one
/// process share the settings object.
/// </summary>
public partial class App : Application
{
    public static SettingsStore SettingsStore { get; } = new();

    public static AppSettings Settings { get; private set; } = new();

    public static void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>Opens a new main window, optionally loading files (one file = open, several = combine).</summary>
    public static MainWindow OpenWindow(IReadOnlyList<string>? files = null, bool combine = false)
    {
        var window = new MainWindow();
        window.Show();
        if (files is { Count: > 0 })
        {
            _ = window.ViewModel.LoadAsync(files, combine);
        }

        return window;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Cleanup();
        Log.Info($"Start {Environment.ProcessPath} args={e.Args.Length}");
        Settings = SettingsStore.Load();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show($"Unexpected error:\n\n{args.Exception.Message}\n\nDetails are in the log ({Log.Folder}).",
                "PdfDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        var files = e.Args.Where(a => !a.StartsWith('-')).ToList();
        if (files.Count == 0)
        {
            OpenWindow();
        }
        else
        {
            // Explorer may pass several files: one window each.
            foreach (var file in files)
            {
                OpenWindow([file]);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveSettings();
        base.OnExit(e);
    }
}
