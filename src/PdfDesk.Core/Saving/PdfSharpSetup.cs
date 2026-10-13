using PdfSharp.Fonts;

namespace PdfDesk.Core.Saving;

internal static class PdfSharpSetup
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        // PDFsharp's Core build needs to be told where fonts come from.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;
}
