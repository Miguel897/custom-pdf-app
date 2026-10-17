using PdfDesk.Core.Model;

namespace PdfDesk.Core.Tests;

public class PageRangesTests
{
    [Theory]
    [InlineData(new[] { 3 }, "3")]
    [InlineData(new[] { 1, 2, 3, 5, 7, 8 }, "1-3, 5, 7-8")]
    [InlineData(new[] { 8, 2, 3, 2, 4 }, "2-4, 8")]
    public void Format_CollapsesConsecutivePages(int[] pages, string expected) =>
        Assert.Equal(expected, PageRanges.Format(pages));

    [Fact]
    public void ExtractFileName_UsesSingularForOnePage()
    {
        Assert.Equal("Report (page 3).pdf", PageRanges.ExtractFileName("Report", [3]));
        Assert.Equal("Report (pages 2-4, 7).pdf", PageRanges.ExtractFileName("Report", [2, 3, 4, 7]));
    }

    [Theory]
    [InlineData(3, 9, "Report - page 3.pdf")]
    [InlineData(3, 12, "Report - page 03.pdf")]
    [InlineData(12, 120, "Report - page 012.pdf")]
    public void SplitFileName_PadsToSortInPageOrder(int page, int count, string expected) =>
        Assert.Equal(expected, PageRanges.SplitFileName("Report", page, count));
}
