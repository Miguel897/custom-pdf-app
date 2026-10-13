using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfDesk.Core.Model;

public enum FormFieldKind
{
    Text,
    CheckBox,
}

/// <summary>
/// One widget of a fillable field (a field may have several widgets sharing the value).
/// <see cref="X"/>/<see cref="Y"/>/<see cref="Width"/>/<see cref="Height"/> are in the page's base frame.
/// Checkbox values are "true"/"false". <see cref="Alignment"/> is the field's /Q: 0 left, 1 centre, 2 right.
/// </summary>
public sealed record FormField(
    string Name,
    FormFieldKind Kind,
    int PageIndex,
    double X,
    double Y,
    double Width,
    double Height,
    string Value,
    bool MultiLine,
    double FontSize,
    bool ReadOnly,
    int Alignment = 0);

/// <summary>Finds text fields and checkboxes in a PDF's AcroForm (low-hanging fruit only, PRD F28).</summary>
internal static partial class FormFieldReader
{
    private const int FlagReadOnly = 1 << 0;
    private const int FlagMultiline = 1 << 12;
    private const int FlagPassword = 1 << 13;
    private const int FlagRadio = 1 << 15;
    private const int FlagPushButton = 1 << 16;

    public static IReadOnlyList<FormField> Read(PdfDocument document, IReadOnlyList<PageGeometry> geometry)
    {
        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        var fields = acroForm?.Elements.GetArray("/Fields");
        if (fields is null)
        {
            return [];
        }

        // Widget object id -> page index.
        var pageOf = new Dictionary<PdfObjectID, int>();
        for (var i = 0; i < document.PageCount; i++)
        {
            var annots = document.Pages[i].Elements.GetArray("/Annots");
            if (annots is null)
            {
                continue;
            }

            foreach (var item in annots.Elements)
            {
                if (item is PdfReference r)
                {
                    pageOf.TryAdd(r.ObjectID, i);
                }
            }
        }

        var defaultDa = acroForm!.Elements.GetString("/DA");
        var result = new List<FormField>();
        var visited = new HashSet<PdfObjectID>();
        foreach (var item in fields.Elements)
        {
            if (Resolve(item) is { } dict)
            {
                Walk(dict, null, new Inherited(null, 0, null, defaultDa, acroForm.Elements.GetInteger("/Q")), pageOf, geometry, result, visited);
            }
        }

        return result;
    }

    private static void Walk(
        PdfDictionary field,
        string? parentName,
        Inherited inherited,
        Dictionary<PdfObjectID, int> pageOf,
        IReadOnlyList<PageGeometry> geometry,
        List<FormField> result,
        HashSet<PdfObjectID> visited)
    {
        if (field.Reference is { } self && !visited.Add(self.ObjectID))
        {
            return; // malformed cyclic tree
        }

        var partial = field.Elements.GetString("/T");
        var name = string.IsNullOrEmpty(partial) ? parentName : parentName is null ? partial : $"{parentName}.{partial}";
        var current = new Inherited(
            field.Elements.GetName("/FT") is { Length: > 0 } ft ? ft : inherited.FieldType,
            field.Elements.ContainsKey("/Ff") ? field.Elements.GetInteger("/Ff") : inherited.Flags,
            field.Elements.ContainsKey("/V") ? field.Elements["/V"] : inherited.Value,
            field.Elements.GetString("/DA") is { Length: > 0 } da ? da : inherited.DefaultAppearance,
            field.Elements.ContainsKey("/Q") ? field.Elements.GetInteger("/Q") : inherited.Alignment);

        var kids = field.Elements.GetArray("/Kids")?.Elements.Select(Resolve).OfType<PdfDictionary>().ToList() ?? [];
        foreach (var kid in kids.Where(k => k.Elements.ContainsKey("/T")))
        {
            Walk(kid, name, current, pageOf, geometry, result, visited);
        }

        var widgets = kids.Where(k => !k.Elements.ContainsKey("/T")).ToList();
        if (field.Elements.GetName("/Subtype") == "/Widget")
        {
            widgets.Add(field);
        }

        if (name is null || widgets.Count == 0)
        {
            return;
        }

        var kind = current.FieldType switch
        {
            "/Tx" when (current.Flags & FlagPassword) == 0 => FormFieldKind.Text,
            "/Btn" when (current.Flags & (FlagRadio | FlagPushButton)) == 0 => FormFieldKind.CheckBox,
            _ => (FormFieldKind?)null,
        };
        if (kind is null)
        {
            return;
        }

        foreach (var widget in widgets)
        {
            if (widget.Reference is not { } wr || !pageOf.TryGetValue(wr.ObjectID, out var pageIndex)
                || pageIndex >= geometry.Count || widget.Elements.GetRectangle("/Rect") is not { } rect)
            {
                continue;
            }

            var g = geometry[pageIndex];
            var (ax, ay) = g.UserToBase(rect.X1, rect.Y1);
            var (bx, by) = g.UserToBase(rect.X2, rect.Y2);
            var value = kind == FormFieldKind.Text
                ? TextValue(current.Value)
                : IsChecked(current.Value, widget) ? "true" : "false";

            result.Add(new FormField(
                name,
                kind.Value,
                pageIndex,
                Math.Min(ax, bx),
                Math.Min(ay, by),
                Math.Abs(bx - ax),
                Math.Abs(by - ay),
                value,
                (current.Flags & FlagMultiline) != 0,
                FontSize(widget.Elements.GetString("/DA") is { Length: > 0 } wda ? wda : current.DefaultAppearance),
                (current.Flags & FlagReadOnly) != 0,
                Math.Clamp(current.Alignment, 0, 2)));
        }
    }

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };

    private static string TextValue(PdfItem? value) => value switch
    {
        PdfString s => s.Value,
        PdfReference { Value: PdfStringObject so } => so.Value,
        _ => "",
    };

    private static bool IsChecked(PdfItem? value, PdfDictionary widget)
    {
        var state = value is PdfName n ? n.Value : widget.Elements.GetName("/AS");
        return !string.IsNullOrEmpty(state) && state != "/Off";
    }

    private static double FontSize(string? da)
    {
        var match = da is null ? null : TfRegex().Match(da);
        return match is { Success: true } && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? size
            : 0;
    }

    [GeneratedRegex(@"([0-9.]+)\s+Tf")]
    private static partial Regex TfRegex();

    private sealed record Inherited(string? FieldType, int Flags, PdfItem? Value, string? DefaultAppearance, int Alignment);
}
