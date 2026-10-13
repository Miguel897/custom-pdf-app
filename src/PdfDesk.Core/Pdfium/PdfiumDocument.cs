using System.Runtime.InteropServices;

namespace PdfDesk.Core.Pdfium;

/// <summary>A 32-bit BGRA bitmap rendered by PDFium on an opaque white background.</summary>
public sealed record RenderedBitmap(int Width, int Height, int Stride, byte[] Pixels);

public enum PdfOpenError
{
    PasswordRequired,
    Corrupt,
    Unsupported,
}

public sealed class PdfOpenException(PdfOpenError error, string message) : Exception(message)
{
    public PdfOpenError Error { get; } = error;
}

/// <summary>
/// A PDF loaded into PDFium from an in-memory copy of the file (the file itself is not kept open,
/// so it can be overwritten on save). Page sizes are in points and already include the page's own
/// <c>/Rotate</c>.
/// </summary>
public sealed class PdfiumDocument : IDisposable
{
    // FPDF_FORMFILLINFO: version 1 with no callbacks. Generously sized; PDFium only reads known fields.
    private const int FormFillInfoSize = 512;

    private nint _data;
    private nint _document;
    private nint _formInfo;
    private nint _form;

    private PdfiumDocument(nint data, nint document, IReadOnlyList<(double Width, double Height)> pageSizes)
    {
        _data = data;
        _document = document;
        PageSizes = pageSizes;
    }

    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }

    public int PageCount => PageSizes.Count;

    public static Task<PdfiumDocument> OpenAsync(byte[] bytes, string? password = null) => PdfiumThread.Invoke(() =>
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        var document = PdfiumNative.FPDF_LoadMemDocument64(data, (nuint)bytes.Length, password);
        if (document == 0)
        {
            var error = PdfiumNative.FPDF_GetLastError();
            Marshal.FreeHGlobal(data);
            throw error switch
            {
                PdfiumNative.ErrPassword => new PdfOpenException(PdfOpenError.PasswordRequired,
                    password is null ? "The file is password protected." : "The password is incorrect."),
                5 => new PdfOpenException(PdfOpenError.Unsupported, "The file uses an unsupported security scheme."),
                _ => new PdfOpenException(PdfOpenError.Corrupt, "The file is not a valid PDF or is damaged."),
            };
        }

        var count = PdfiumNative.FPDF_GetPageCount(document);
        var sizes = new (double, double)[count];
        for (var i = 0; i < count; i++)
        {
            PdfiumNative.FPDF_GetPageSizeByIndexF(document, i, out var size);
            sizes[i] = (size.Width, size.Height);
        }

        var doc = new PdfiumDocument(data, document, sizes);
        doc.InitForms();
        return doc;
    });

    /// <summary>
    /// Renders a page. <paramref name="quarterTurns"/> is extra clockwise rotation (0–3). A request
    /// cancelled before PDFium gets to it is skipped (fast scrolling queues many renders).
    /// </summary>
    public Task<RenderedBitmap> RenderAsync(int pageIndex, int quarterTurns, double pixelsPerPoint, CancellationToken cancel = default) => PdfiumThread.Invoke(() =>
    {
        cancel.ThrowIfCancellationRequested(); // skip requests that went stale while queued
        ObjectDisposedException.ThrowIf(_document == 0, this);
        var (w, h) = PageSizes[pageIndex];
        if (quarterTurns % 2 == 1)
        {
            (w, h) = (h, w);
        }

        var width = Math.Max(1, (int)Math.Round(w * pixelsPerPoint));
        var height = Math.Max(1, (int)Math.Round(h * pixelsPerPoint));
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var page = PdfiumNative.FPDF_LoadPage(_document, pageIndex);
        if (page == 0)
        {
            throw new InvalidOperationException($"Page {pageIndex + 1} could not be loaded.");
        }

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (_form != 0)
            {
                PdfiumNative.FORM_OnAfterLoadPage(page, _form);
            }

            var bitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.BitmapBgra, handle.AddrOfPinnedObject(), stride);
            PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, quarterTurns & 3, PdfiumNative.RenderAnnotations);
            if (_form != 0)
            {
                PdfiumNative.FPDF_FFLDraw(_form, bitmap, page, 0, 0, width, height, quarterTurns & 3, PdfiumNative.RenderAnnotations);
                PdfiumNative.FORM_OnBeforeClosePage(page, _form);
            }

            PdfiumNative.FPDFBitmap_Destroy(bitmap);
        }
        finally
        {
            handle.Free();
            PdfiumNative.FPDF_ClosePage(page);
        }

        return new RenderedBitmap(width, height, stride, pixels);
    });

    public void Dispose()
    {
        if (_document == 0)
        {
            return;
        }

        PdfiumThread.Invoke(() =>
        {
            if (_form != 0)
            {
                PdfiumNative.FPDFDOC_ExitFormFillEnvironment(_form);
                _form = 0;
            }

            PdfiumNative.FPDF_CloseDocument(_document);
            _document = 0;
            Marshal.FreeHGlobal(_formInfo);
            Marshal.FreeHGlobal(_data);
            _formInfo = _data = 0;
        }).GetAwaiter().GetResult();
    }

    private void InitForms()
    {
        _formInfo = Marshal.AllocHGlobal(FormFillInfoSize);
        unsafe
        {
            new Span<byte>((void*)_formInfo, FormFillInfoSize).Clear();
        }

        Marshal.WriteInt32(_formInfo, 1); // version
        _form = PdfiumNative.FPDFDOC_InitFormFillEnvironment(_document, _formInfo);
    }
}
