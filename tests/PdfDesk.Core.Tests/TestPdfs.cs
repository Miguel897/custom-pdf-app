using System.Text;
using PdfDesk.Core.Pdfium;
using PdfDesk.Core.Saving;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfDesk.Core.Tests;

/// <summary>Generates small PDFs for tests (no binary fixtures in the repo).</summary>
internal static class TestPdfs
{
    static TestPdfs() => PdfSharpSetup.EnsureInitialized();

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PdfDeskTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Pages with distinct widths (300, 310, 320…) so their order can be checked after saving.</summary>
    public static string Numbered(string dir, string name, int pages, int rotate = 0, int widthBase = 300)
    {
        var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(widthBase + (i * 10));
            page.Height = XUnit.FromPoint(500);
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString($"{i + 1}", new XFont("Arial", 40), XBrushes.Black, new XPoint(50, 100));
            }

            page.Rotate = rotate;
        }

        var path = Path.Combine(dir, name);
        document.Save(path);
        return path;
    }

    /// <summary>One blank page with an explicit media box (user space) and /Rotate.</summary>
    public static string Blank(string dir, string name, double x0, double y0, double x1, double y1, int rotate)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new XPoint(x0, y0), new XPoint(x1, y1));
        page.Rotate = rotate;
        var path = Path.Combine(dir, name);
        document.Save(path);
        return path;
    }

    public static string Encrypted(string dir, string name, string password)
    {
        var source = Numbered(dir, "plain-" + name, 2);
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        document.SecuritySettings.UserPassword = password;
        document.SecuritySettings.OwnerPassword = password + "-owner";
        document.SecurityHandler.SetEncryptionToV5();
        var path = Path.Combine(dir, name);
        document.Save(path);
        return path;
    }

    /// <summary>A page with an AcroForm text field "person.name" and a checkbox "agree".</summary>
    public static string Form(string dir, string name)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(400);
        page.Height = XUnit.FromPoint(400);
        _ = document.AddPage(); // second page so reordering can be tested

        var helv = new PdfDictionary(document);
        helv.Elements["/Type"] = new PdfName("/Font");
        helv.Elements["/Subtype"] = new PdfName("/Type1");
        helv.Elements["/BaseFont"] = new PdfName("/Helvetica");
        helv.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        var fonts = new PdfDictionary(document);
        fonts.Elements["/Helv"] = helv;
        var resources = new PdfDictionary(document);
        resources.Elements["/Font"] = fonts;

        var fields = new PdfArray(document);
        var acroForm = new PdfDictionary(document);
        document.Internals.AddObject(acroForm);
        acroForm.Elements["/Fields"] = fields;
        acroForm.Elements["/DR"] = resources;
        acroForm.Elements["/DA"] = new PdfString("/Helv 0 Tf 0 g");
        document.Internals.Catalog.Elements["/AcroForm"] = acroForm.Reference;

        var annots = new PdfArray(document);
        page.Elements["/Annots"] = annots;

        // Parent "person" with a terminal child "name" that is also its widget.
        var parent = new PdfDictionary(document);
        document.Internals.AddObject(parent);
        parent.Elements["/T"] = new PdfString("person");
        var text = Widget(document, page, 50, 300, 250, 320);
        text.Elements["/FT"] = new PdfName("/Tx");
        text.Elements["/T"] = new PdfString("name");
        text.Elements["/DA"] = new PdfString("/Helv 12 Tf 0 g");
        text.Elements["/Parent"] = parent.Reference;
        parent.Elements["/Kids"] = new PdfArray(document, text.Reference!);
        fields.Elements.Add(parent.Reference!);
        annots.Elements.Add(text.Reference!);

        var box = Widget(document, page, 50, 250, 64, 264);
        box.Elements["/FT"] = new PdfName("/Btn");
        box.Elements["/T"] = new PdfString("agree");
        box.Elements["/V"] = new PdfName("/Off");
        box.Elements["/AS"] = new PdfName("/Off");
        var normal = new PdfDictionary(document);
        normal.Elements["/Yes"] = Stream(document, "0 g 2 2 10 10 re f");
        normal.Elements["/Off"] = Stream(document, "");
        var ap = new PdfDictionary(document);
        ap.Elements["/N"] = normal;
        box.Elements["/AP"] = ap;
        fields.Elements.Add(box.Reference!);
        annots.Elements.Add(box.Reference!);

        var path = Path.Combine(dir, name);
        document.Save(path);
        return path;
    }

    public static async Task<IReadOnlyList<(double Width, double Height)>> PageSizesAsync(string path, string? password = null)
    {
        using var document = await PdfiumDocument.OpenAsync(File.ReadAllBytes(path), password);
        return document.PageSizes;
    }

    /// <summary>Bounding box (pixels at 1 px/pt, base frame) of clearly red pixels on a rendered page.</summary>
    public static async Task<(int Left, int Top, int Right, int Bottom)?> RedBoundsAsync(string path, int pageIndex = 0)
    {
        using var document = await PdfiumDocument.OpenAsync(File.ReadAllBytes(path));
        var bitmap = await document.RenderAsync(pageIndex, 0, 1.0);
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var i = (y * bitmap.Stride) + (x * 4);
                var (b, g, r) = (bitmap.Pixels[i], bitmap.Pixels[i + 1], bitmap.Pixels[i + 2]);
                if (r > 180 && g < 100 && b < 100)
                {
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                }
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    private static PdfDictionary Widget(PdfDocument document, PdfPage page, double x0, double y0, double x1, double y1)
    {
        var widget = new PdfDictionary(document);
        document.Internals.AddObject(widget);
        widget.Elements["/Type"] = new PdfName("/Annot");
        widget.Elements["/Subtype"] = new PdfName("/Widget");
        widget.Elements["/Rect"] = new PdfArray(document, new PdfReal(x0), new PdfReal(y0), new PdfReal(x1), new PdfReal(y1));
        widget.Elements["/F"] = new PdfInteger(4);
        widget.Elements["/P"] = page.Reference!;
        return widget;
    }

    private static PdfReference Stream(PdfDocument document, string content)
    {
        var stream = new PdfDictionary(document);
        document.Internals.AddObject(stream);
        stream.Elements["/Type"] = new PdfName("/XObject");
        stream.Elements["/Subtype"] = new PdfName("/Form");
        stream.Elements["/BBox"] = new PdfArray(document, new PdfReal(0), new PdfReal(0), new PdfReal(14), new PdfReal(14));
        stream.CreateStream(Encoding.ASCII.GetBytes(content));
        return stream.Reference!;
    }
}
