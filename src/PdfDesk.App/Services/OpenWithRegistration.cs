using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PdfDesk.App.Services;

/// <summary>
/// Per-user "Open with" integration (PRD F40): HKCU only, no admin, no installer. Windows 11 does not
/// let apps make themselves the default; the user picks PdfDesk once in "Open with › Always".
/// </summary>
public static partial class OpenWithRegistration
{
    private const string ProgId = "PdfDesk.pdf";
    private const string Classes = @"Software\Classes";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown exe path.");

    private static string AppKey => $@"{Classes}\Applications\{Path.GetFileName(ExePath)}";

    /// <summary>True when registered for this exe path (false if the exe has moved since).</summary>
    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ProgId}\shell\open\command");
        return key?.GetValue(null) is string command && command.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        var command = $"\"{ExePath}\" \"%1\"";

        using (var progId = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgId}"))
        {
            progId.SetValue(null, "PDF document (PdfDesk)");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{ExePath}\",0");
            using var open = progId.CreateSubKey(@"shell\open\command");
            open.SetValue(null, command);
        }

        using (var openWith = Registry.CurrentUser.CreateSubKey($@"{Classes}\.pdf\OpenWithProgids"))
        {
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var app = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            app.SetValue("FriendlyAppName", "PdfDesk");
            using var types = app.CreateSubKey("SupportedTypes");
            types.SetValue(".pdf", "");
            using var open = app.CreateSubKey(@"shell\open\command");
            open.SetValue(null, command);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        using (var openWith = Registry.CurrentUser.OpenSubKey($@"{Classes}\.pdf\OpenWithProgids", writable: true))
        {
            openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        NotifyShell();
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
