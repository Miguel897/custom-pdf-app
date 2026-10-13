using PdfDesk.Core.Combine;
using PdfDesk.Core.Model;

namespace PdfDesk.Core.Tests;

public class PageGeometryTests
{
    private static readonly PdfBox Media = new(10, 20, 610, 820);
    private static readonly PdfBox Crop = new(30, 40, 580, 800);

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(-90)]
    public void BaseToUser_And_Back_RoundTrips(int rotate)
    {
        var g = new PageGeometry(rotate, Crop, Media);
        foreach (var (x, y) in new[] { (0.0, 0.0), (12.5, 99.0), (g.BaseWidth, g.BaseHeight) })
        {
            var (ux, uy) = g.BaseToUser(x, y);
            var (bx, by) = g.UserToBase(ux, uy);
            Assert.Equal(x, bx, 6);
            Assert.Equal(y, by, 6);
        }
    }

    [Theory]
    [InlineData(0, 30, 800)]   // base top-left = crop top-left
    [InlineData(90, 30, 40)]   // rotated clockwise: base top-left = crop bottom-left
    [InlineData(180, 580, 40)]
    [InlineData(270, 580, 800)]
    public void BaseOrigin_MapsToExpectedCropCorner(int rotate, double ux, double uy)
    {
        var (x, y) = new PageGeometry(rotate, Crop, Media).BaseToUser(0, 0);
        Assert.Equal(ux, x);
        Assert.Equal(uy, y);
    }

    [Fact]
    public void BaseSize_SwapsForQuarterTurns()
    {
        var g = new PageGeometry(90, Crop, Media);
        Assert.Equal(Crop.Height, g.BaseWidth);
        Assert.Equal(Crop.Width, g.BaseHeight);
    }

    [Fact]
    public void NaturalComparer_SortsLikeExplorer()
    {
        string[] names = ["file10.pdf", "File2.pdf", "file1.pdf", "annex.pdf"];
        Assert.Equal(["annex.pdf", "file1.pdf", "File2.pdf", "file10.pdf"], names.Order(NaturalComparer.Instance));
    }
}
