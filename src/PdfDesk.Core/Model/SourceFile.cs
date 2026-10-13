using PdfDesk.Core.Pdfium;
using PdfDesk.Core.Saving;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfDesk.Core.Model;

/// <summary>
/// A PDF file the working document takes pages from. The file is read once into memory, so it is
/// never locked and can be overwritten by Save; saving always uses these bytes, not the file on disk.
/// </summary>
public sealed class SourceFile : IDisposable
{
    private SourceFile(int id, string path, byte[] bytes, string? password, PdfiumDocument pdfium,
        IReadOnlyList<PageGeometry> geometry, IReadOnlyList<FormField> formFields, string? saveProblem)
    {
        Id = id;
        Path = path;
        Bytes = bytes;
        Password = password;
        Pdfium = pdfium;
        Geometry = geometry;
        FormFields = formFields;
        SaveProblem = saveProblem;
    }

    public int Id { get; }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public byte[] Bytes { get; }

    public string? Password { get; }

    public bool IsEncrypted => Password is not null;

    public PdfiumDocument Pdfium { get; }

    public int PageCount => Pdfium.PageCount;

    public IReadOnlyList<PageGeometry> Geometry { get; }

    public IReadOnlyList<FormField> FormFields { get; }

    /// <summary>Set when the file can be viewed but PDFsharp cannot read it, so it cannot be saved.</summary>
    public string? SaveProblem { get; }

    /// <summary>Opens a PDF. Throws <see cref="PdfOpenException"/> (password, corrupt) or IO exceptions.</summary>
    public static async Task<SourceFile> OpenAsync(int id, string path, string? password = null)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes);
        }

        var pdfium = await PdfiumDocument.OpenAsync(bytes, password);
        try
        {
            var (geometry, fields, problem) = await Task.Run(() => ReadStructure(bytes, password, pdfium));
            return new SourceFile(id, path, bytes, password, pdfium, geometry, fields, problem);
        }
        catch
        {
            pdfium.Dispose();
            throw;
        }
    }

    internal PdfDocument OpenForPdfSharp(PdfDocumentOpenMode mode) =>
        Password is null
            ? PdfReader.Open(new MemoryStream(Bytes, writable: false), mode)
            : PdfReader.Open(new MemoryStream(Bytes, writable: false), Password, mode);

    public void Dispose() => Pdfium.Dispose();

    private static (IReadOnlyList<PageGeometry>, IReadOnlyList<FormField>, string?) ReadStructure(
        byte[] bytes, string? password, PdfiumDocument pdfium)
    {
        try
        {
            PdfSharpSetup.EnsureInitialized();
            var stream = new MemoryStream(bytes, writable: false);
            using var document = password is null
                ? PdfReader.Open(stream, PdfDocumentOpenMode.Import)
                : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import);
            if (document.PageCount != pdfium.PageCount)
            {
                throw new InvalidOperationException($"Page count mismatch ({document.PageCount} vs {pdfium.PageCount}).");
            }

            var geometry = new PageGeometry[document.PageCount];
            for (var i = 0; i < document.PageCount; i++)
            {
                geometry[i] = ReadGeometry(document.Pages[i]);
            }

            IReadOnlyList<FormField> fields;
            try
            {
                fields = FormFieldReader.Read(document, geometry);
            }
            catch (Exception ex)
            {
                Log.Error("Reading form fields failed", ex);
                fields = [];
            }

            return (geometry, fields, null);
        }
        catch (Exception ex)
        {
            Log.Error("PDFsharp cannot read the file", ex);
            var fallback = pdfium.PageSizes.Select(s => PageGeometry.Simple(s.Width, s.Height)).ToArray();
            return (fallback, [], "PdfDesk can show this file but cannot save it: " + ex.Message);
        }
    }

    private static PageGeometry ReadGeometry(PdfPage page)
    {
        var m = page.MediaBox;
        var media = PdfBox.Normalized(m.X1, m.Y1, m.X2, m.Y2);
        var crop = media;
        if (page.Elements.GetRectangle("/CropBox") is { IsZero: false } c)
        {
            var box = PdfBox.Normalized(c.X1, c.Y1, c.X2, c.Y2);
            crop = new PdfBox(
                Math.Max(box.X0, media.X0), Math.Max(box.Y0, media.Y0),
                Math.Min(box.X1, media.X1), Math.Min(box.Y1, media.Y1));
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                crop = media;
            }
        }

        return new PageGeometry(page.Rotate, crop, media);
    }
}
