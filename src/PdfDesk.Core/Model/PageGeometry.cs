namespace PdfDesk.Core.Model;

/// <summary>A rectangle in PDF user space (origin bottom-left, y up).</summary>
public readonly record struct PdfBox(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;

    public double Height => Y1 - Y0;

    public static PdfBox Normalized(double x0, double y0, double x1, double y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
}

/// <summary>
/// Converts between three coordinate frames of one source page:
/// <list type="bullet">
/// <item><b>Base frame</b>: what PDFium renders at rotate=0 (crop box, page <c>/Rotate</c> applied), top-left origin, y down.</item>
/// <item><b>User space</b>: raw PDF coordinates, bottom-left origin, y up, no rotation.</item>
/// <item><b>Graphics frame</b>: PDFsharp <c>XGraphics</c> on the page: y down, no rotation.</item>
/// </list>
/// </summary>
public sealed record PageGeometry(int PageRotation, PdfBox CropBox, PdfBox MediaBox)
{
    /// <summary>Page <c>/Rotate</c> in clockwise quarter turns (0–3).</summary>
    public int Quarters => ((PageRotation / 90 % 4) + 4) % 4;

    public double BaseWidth => Quarters % 2 == 0 ? CropBox.Width : CropBox.Height;

    public double BaseHeight => Quarters % 2 == 0 ? CropBox.Height : CropBox.Width;

    public static PageGeometry Simple(double width, double height) =>
        new(0, new PdfBox(0, 0, width, height), new PdfBox(0, 0, width, height));

    public (double X, double Y) BaseToUser(double bx, double by)
    {
        var c = CropBox;
        return Quarters switch
        {
            0 => (c.X0 + bx, c.Y1 - by),
            1 => (c.X0 + by, c.Y0 + bx),
            2 => (c.X1 - bx, c.Y0 + by),
            _ => (c.X1 - by, c.Y1 - bx),
        };
    }

    public (double X, double Y) UserToBase(double ux, double uy)
    {
        var c = CropBox;
        return Quarters switch
        {
            0 => (ux - c.X0, c.Y1 - uy),
            1 => (uy - c.Y0, ux - c.X0),
            2 => (c.X1 - ux, uy - c.Y0),
            _ => (c.Y1 - uy, c.X1 - ux),
        };
    }

    /// <remarks>PDFsharp puts the graphics origin at user (0, media height), ignoring the media box offset (verified by tests).</remarks>
    public (double X, double Y) UserToGraphics(double ux, double uy) => (ux, MediaBox.Height - uy);

    /// <summary>Clockwise angle in the graphics frame of a direction given in the base frame.</summary>
    public double BaseAngleToGraphics(int baseQuarters) => (((baseQuarters - Quarters) % 4) + 4) % 4 * 90;

    /// <summary>Axis-aligned user-space box of a base-frame rectangle rotated around its top-left corner.</summary>
    public PdfBox RotatedBaseRectToUser(double bx, double by, double width, double height, int quarters)
    {
        var angle = quarters * Math.PI / 2;
        var (cos, sin) = (Math.Round(Math.Cos(angle)), Math.Round(Math.Sin(angle)));
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (lx, ly) in new[] { (0.0, 0.0), (width, 0.0), (0.0, height), (width, height) })
        {
            // Clockwise rotation in a y-down frame.
            var (x, y) = BaseToUser(bx + (lx * cos) - (ly * sin), by + (lx * sin) + (ly * cos));
            (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
        }

        return new PdfBox(minX, minY, maxX, maxY);
    }
}
