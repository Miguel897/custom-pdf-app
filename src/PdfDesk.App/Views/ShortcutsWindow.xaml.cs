using System.Windows;

namespace PdfDesk.App.Views;

/// <summary>Lists the keyboard and mouse shortcuts (F1). Keep in sync with MainWindow's key handling.</summary>
public partial class ShortcutsWindow : Window
{
    /// <summary>Each entry of <paramref name="Keys"/> is one key combination; alternatives are separate entries.</summary>
    public sealed record Shortcut(string[] Keys, string Action)
    {
        public string Spoken => $"{string.Join(" or ", Keys)}: {Action}";
    }

    public sealed record Section(string Title, Shortcut[] Items);

    public static IReadOnlyList<Section> All { get; } =
    [
        new("File",
        [
            new(["Ctrl+O"], "Open a PDF (or drop it on the window)"),
            new(["Ctrl+S"], "Save"),
            new(["Ctrl+Shift+S"], "Save as a copy"),
        ]),
        new("Pages",
        [
            new(["Ctrl+Z"], "Undo"),
            new(["Ctrl+Y", "Ctrl+Shift+Z"], "Redo"),
            new(["Ctrl+A"], "Select all pages (in the thumbnail strip)"),
            new(["Ctrl+Click", "Shift+Click"], "Select several pages"),
            new(["Alt+↑", "Alt+↓"], "Move the selected pages up or down"),
            new(["Drag"], "Move pages by dragging their thumbnails"),
            new(["Del"], "Delete the selected pages (or the selected text box)"),
        ]),
        new("Text",
        [
            new(["T"], "Text tool: click on a page and type"),
            new(["Shift+T"], "Editable text tool"),
            new(["Esc"], "Finish typing / leave the tool"),
        ]),
        new("Navigation and zoom",
        [
            new(["PgUp", "PgDn"], "Previous / next page"),
            new(["Home", "End"], "First / last page"),
            new(["Ctrl+Wheel"], "Zoom"),
            new(["Ctrl++", "Ctrl+−"], "Zoom in / out"),
            new(["Ctrl+0"], "Actual size"),
            new(["F1"], "This list"),
        ]),
    ];

    public ShortcutsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40; // scroll only on small screens
        Sections.ItemsSource = All;
    }
}
