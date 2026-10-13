using System.Globalization;
using PdfDesk.Core.Model;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfDesk.Core.Saving;

public sealed class PdfSaveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Builds the output PDF from the sources and a <see cref="DocumentState"/> (PRD §7.4).
/// <list type="bullet">
/// <item>All pages from one unencrypted source, no duplicates: the source is edited in place, so
/// form fields, bookmarks and metadata survive.</item>
/// <item>Otherwise pages are imported into a new document. Form fields keep their filled-in look
/// but are no longer interactive.</item>
/// </list>
/// The file is written to a temp file next to the target and then moved over it.
/// </summary>
public static class PdfExporter
{
    public const string FontFamily = "Arial";

    /// <summary>Line height as a multiple of the font size (Arial's natural line spacing).</summary>
    public const double LineSpacing = 1.15;

    public static void Save(IReadOnlyList<SourceFile> sources, DocumentState state, string path)
    {
        PdfSharpSetup.EnsureInitialized();
        if (state.Pages.Count == 0)
        {
            throw new PdfSaveException("The document has no pages.");
        }

        var used = state.Pages.Select(p => p.SourceId).Distinct().Select(id => sources.First(s => s.Id == id)).ToList();
        if (used.FirstOrDefault(s => s.SaveProblem is not null) is { } bad)
        {
            throw new PdfSaveException($"{bad.FileName}: {bad.SaveProblem}");
        }

        var temp = path + ".pdfdesk-tmp";
        try
        {
            var inPlace = used.Count == 1 && !used[0].IsEncrypted
                && state.Pages.Select(p => p.PageIndex).Distinct().Count() == state.Pages.Count;
            using var output = inPlace ? BuildInPlace(used[0], state) : BuildByImport(used, state);

            for (var i = 0; i < state.Pages.Count; i++)
            {
                var item = state.Pages[i];
                var geometry = sources.First(s => s.Id == item.SourceId).Geometry[item.PageIndex];
                var page = output.Pages[i];
                AddOverlays(output, page, geometry, item.Overlays);
                page.Rotate = (geometry.Quarters + item.Rotation) % 4 * 90;
            }

            output.Info.Creator = "PdfDesk";
            output.Options.CompressContentStreams = true;
            output.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            output.Save(temp);
            File.Move(temp, path, overwrite: true);
            Log.Info($"Saved {path} ({state.Pages.Count} pages, {(inPlace ? "in place" : "imported")})");
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            Log.Error($"Saving {path} failed", ex);
            throw ex is PdfSaveException ? ex : new PdfSaveException(Describe(ex), ex);
        }
    }

    private static PdfDocument BuildInPlace(SourceFile source, DocumentState state)
    {
        var document = source.OpenForPdfSharp(PdfDocumentOpenMode.Modify);
        ApplyFormValues(document, source.Id, state);

        var original = Enumerable.Range(0, document.PageCount).Select(i => document.Pages[i]).ToList();
        var keep = state.Pages.Select(p => p.PageIndex).ToHashSet();
        for (var i = original.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(i))
            {
                document.Pages.RemoveAt(i);
            }
        }

        for (var target = 0; target < state.Pages.Count; target++)
        {
            var wanted = original[state.Pages[target].PageIndex];
            var current = IndexOf(document, wanted);
            if (current != target)
            {
                document.Pages.MovePage(current, target);
            }
        }

        return document;
    }

    private static PdfDocument BuildByImport(IReadOnlyList<SourceFile> used, DocumentState state)
    {
        var imported = new Dictionary<int, PdfDocument>();
        try
        {
            foreach (var source in used)
            {
                if (state.FormValues.Keys.Any(k => k.SourceId == source.Id))
                {
                    // Fill the fields in a copy first, then import its pages (Import mode cannot modify).
                    using var filled = source.OpenForPdfSharp(PdfDocumentOpenMode.Modify);
                    ApplyFormValues(filled, source.Id, state);
                    var buffer = new MemoryStream();
                    filled.Save(buffer, closeStream: false);
                    buffer.Position = 0;
                    imported[source.Id] = PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
                }
                else
                {
                    imported[source.Id] = source.OpenForPdfSharp(PdfDocumentOpenMode.Import);
                }
            }

            var output = new PdfDocument();
            foreach (var item in state.Pages)
            {
                output.AddPage(imported[item.SourceId].Pages[item.PageIndex]);
            }

            if (imported[state.Pages[0].SourceId].Info.Title is { Length: > 0 } title)
            {
                output.Info.Title = title;
            }

            return output;
        }
        finally
        {
            foreach (var document in imported.Values)
            {
                document.Dispose();
            }
        }
    }

    private static void ApplyFormValues(PdfDocument document, int sourceId, DocumentState state)
    {
        var values = state.FormValues.Where(kv => kv.Key.SourceId == sourceId).ToList();
        if (values.Count == 0 || document.AcroForm is not { } form)
        {
            return;
        }

        foreach (var (key, value) in values)
        {
            switch (form.Fields[key.Name])
            {
                case PdfTextField text:
                    text.Text = value;
                    break;
                case PdfCheckBoxField checkBox:
                    checkBox.Checked = value == "true";
                    break;
                default:
                    Log.Info($"Form field not found or unsupported: {key.Name}");
                    break;
            }
        }

        // Let viewers rebuild field appearances (PDFsharp's own are basic).
        form.Elements["/NeedAppearances"] = new PdfBoolean(true);
    }

    private static void AddOverlays(PdfDocument document, PdfPage page, PageGeometry geometry, IReadOnlyList<TextOverlay> overlays)
    {
        var flattened = overlays.Where(o => o.Kind == TextKind.Flattened && o.Text.Length > 0).ToList();
        if (flattened.Count > 0)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            foreach (var overlay in flattened)
            {
                var (ux, uy) = geometry.BaseToUser(overlay.X, overlay.Y);
                var (gx, gy) = geometry.UserToGraphics(ux, uy);
                var saved = gfx.Save();
                gfx.TranslateTransform(gx, gy);
                gfx.RotateTransform(geometry.BaseAngleToGraphics(overlay.Rotation));
                DrawLines(gfx, overlay);
                gfx.Restore(saved);
            }
        }

        foreach (var overlay in overlays.Where(o => o.Kind == TextKind.Annotation && o.Text.Length > 0))
        {
            AddFreeText(document, page, geometry, overlay);
        }
    }

    private static void AddFreeText(PdfDocument document, PdfPage page, PageGeometry geometry, TextOverlay overlay)
    {
        var (width, height) = Measure(overlay);
        var rect = geometry.RotatedBaseRectToUser(overlay.X, overlay.Y, width, height, overlay.Rotation);

        // Appearance stream: the text drawn into a form XObject, rotated to match the page.
        var form = new XForm(document, new XRect(0, 0, width, height));
        using (var gfx = XGraphics.FromForm(form))
        {
            DrawLines(gfx, overlay);
        }

        var formRef = GetFormReference(document, form);
        var angle = -geometry.BaseAngleToGraphics(overlay.Rotation) * Math.PI / 180;
        var (cos, sin) = (Math.Round(Math.Cos(angle)), Math.Round(Math.Sin(angle)));
        ((PdfDictionary)formRef.Value).Elements["/Matrix"] = new PdfArray(document,
            new PdfReal(cos), new PdfReal(sin), new PdfReal(-sin), new PdfReal(cos), new PdfReal(0), new PdfReal(0));

        var (r, g, b) = ParseColor(overlay.Color);
        var size = overlay.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var annot = new PdfDictionary(document);
        document.Internals.AddObject(annot);
        annot.Elements["/Type"] = new PdfName("/Annot");
        annot.Elements["/Subtype"] = new PdfName("/FreeText");
        annot.Elements["/NM"] = new PdfString($"PdfDesk-{overlay.Id:N}");
        annot.Elements["/Rect"] = new PdfArray(document, new PdfReal(rect.X0), new PdfReal(rect.Y0), new PdfReal(rect.X1), new PdfReal(rect.Y1));
        annot.Elements["/Contents"] = new PdfString(overlay.Text, PdfStringEncoding.Unicode);
        annot.Elements["/DA"] = new PdfString(string.Create(CultureInfo.InvariantCulture, $"/Helv {size} Tf {r / 255.0:0.###} {g / 255.0:0.###} {b / 255.0:0.###} rg"));
        annot.Elements["/DS"] = new PdfString($"font: Helvetica,sans-serif {size}pt; color:{overlay.Color}");
        annot.Elements["/F"] = new PdfInteger(4); // print
        annot.Elements["/M"] = new PdfString($"D:{DateTime.Now:yyyyMMddHHmmss}");
        annot.Elements["/P"] = page.Reference!;
        var border = new PdfDictionary(document);
        border.Elements["/W"] = new PdfInteger(0);
        annot.Elements["/BS"] = border;
        var appearance = new PdfDictionary(document);
        appearance.Elements["/N"] = formRef;
        annot.Elements["/AP"] = appearance;

        var annots = page.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(document);
            page.Elements["/Annots"] = annots;
        }

        annots.Elements.Add(annot.Reference!);
    }

    /// <summary>
    /// <c>XForm.PdfForm</c> is internal in PDFsharp 6: draw the form once on a scratch page and take
    /// its XObject reference from that page's resources, then drop the page.
    /// </summary>
    private static PdfReference GetFormReference(PdfDocument document, XForm form)
    {
        var scratch = document.AddPage();
        try
        {
            using (var gfx = XGraphics.FromPdfPage(scratch))
            {
                gfx.DrawImage(form, 0, 0);
            }

            var xobjects = scratch.Resources.Elements.GetDictionary("/XObject")
                ?? throw new InvalidOperationException("Form XObject not found.");
            return (PdfReference)xobjects.Elements.Values.Single()!;
        }
        finally
        {
            document.Pages.Remove(scratch);
        }
    }

    internal static XFont CreateFont(double size) =>
        new(FontFamily, size, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));

    /// <summary>Width and height in points of the text box, matching how the app lays it out.</summary>
    internal static (double Width, double Height) Measure(TextOverlay overlay)
    {
        var font = CreateFont(overlay.FontSize);
        var lines = SplitLines(overlay.Text);
        var measure = XGraphics.CreateMeasureContext(new XSize(10000, 10000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var width = lines.Max(l => measure.MeasureString(l.Length == 0 ? " " : l, font).Width);
        return (Math.Max(1, width), lines.Length * overlay.FontSize * LineSpacing);
    }

    private static void DrawLines(XGraphics gfx, TextOverlay overlay)
    {
        var font = CreateFont(overlay.FontSize);
        var (r, g, b) = ParseColor(overlay.Color);
        var brush = new XSolidBrush(XColor.FromArgb(r, g, b));
        var lineHeight = overlay.FontSize * LineSpacing;
        var lines = SplitLines(overlay.Text);
        for (var i = 0; i < lines.Length; i++)
        {
            // Centre the font's own height inside the line box, as WPF does.
            var top = (i * lineHeight) + ((lineHeight - font.GetHeight()) / 2);
            gfx.DrawString(lines[i], font, brush, new XPoint(0, top), XStringFormats.TopLeft);
        }
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static (byte R, byte G, byte B) ParseColor(string color)
    {
        var hex = color.TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? ((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : ((byte)0, (byte)0, (byte)0);
    }

    private static int IndexOf(PdfDocument document, PdfPage page)
    {
        for (var i = 0; i < document.PageCount; i++)
        {
            if (ReferenceEquals(document.Pages[i], page))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Page not found while reordering.");
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied. Choose another folder or check the file is not read-only.",
        IOException => "The file could not be written. It may be open in another program.\n\n" + ex.Message,
        _ => "The PDF could not be saved: " + ex.Message,
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
