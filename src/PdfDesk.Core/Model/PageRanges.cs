namespace PdfDesk.Core.Model;

/// <summary>Human-readable page numbers and file names for extracting and splitting pages.</summary>
public static class PageRanges
{
    /// <summary>1-based page numbers → "2-4, 7" (sorted, duplicates removed).</summary>
    public static string Format(IEnumerable<int> pageNumbers)
    {
        var numbers = pageNumbers.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < numbers.Count;)
        {
            var start = numbers[i];
            var end = start;
            while (i + 1 < numbers.Count && numbers[i + 1] == end + 1)
            {
                end = numbers[++i];
            }

            parts.Add(start == end ? $"{start}" : $"{start}-{end}");
            i++;
        }

        return string.Join(", ", parts);
    }

    /// <summary>"Report (page 3).pdf" or "Report (pages 2-4, 7).pdf".</summary>
    public static string ExtractFileName(string baseName, IReadOnlyCollection<int> pageNumbers) =>
        $"{baseName} ({(pageNumbers.Distinct().Count() == 1 ? "page" : "pages")} {Format(pageNumbers)}).pdf";

    /// <summary>"Report - page 03.pdf": zero-padded so the files sort in page order.</summary>
    public static string SplitFileName(string baseName, int pageNumber, int pageCount) =>
        $"{baseName} - page {pageNumber.ToString().PadLeft(pageCount.ToString().Length, '0')}.pdf";
}
