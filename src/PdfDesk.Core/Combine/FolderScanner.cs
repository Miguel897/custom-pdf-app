using System.Runtime.InteropServices;
using PdfDesk.Core.Pdfium;

namespace PdfDesk.Core.Combine;

public enum CombineSort
{
    Name,
    Date,
    Manual,
}

/// <summary>A PDF found in the folder. <see cref="Problem"/> is set when it cannot be combined.</summary>
public sealed record CombineCandidate(string Path, long Size, DateTime Modified, int PageCount, string? Problem)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public static class FolderScanner
{
    /// <summary>Lists the PDFs of a folder (optionally with subfolders) and reads their page counts.</summary>
    public static async Task<IReadOnlyList<CombineCandidate>> ScanAsync(string folder, bool includeSubfolders, CancellationToken cancel = default)
    {
        var files = await Task.Run(() => Directory.EnumerateFiles(folder, "*.pdf", new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        }).ToList(), cancel);

        var result = new List<CombineCandidate>(files.Count);
        foreach (var path in files)
        {
            cancel.ThrowIfCancellationRequested();
            result.Add(await InspectAsync(path));
        }

        return Sort(result, CombineSort.Name);
    }

    public static async Task<CombineCandidate> InspectAsync(string path)
    {
        var info = new FileInfo(path);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var document = await PdfiumDocument.OpenAsync(bytes);
            return new CombineCandidate(path, info.Length, info.LastWriteTime, document.PageCount, null);
        }
        catch (PdfOpenException ex)
        {
            return new CombineCandidate(path, info.Length, info.LastWriteTime, 0,
                ex.Error == PdfOpenError.PasswordRequired ? "password protected" : "damaged or not a PDF");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CombineCandidate(path, info.Length, info.LastWriteTime, 0, "cannot be read");
        }
    }

    public static IReadOnlyList<CombineCandidate> Sort(IEnumerable<CombineCandidate> items, CombineSort sort) => sort switch
    {
        CombineSort.Name => [.. items.OrderBy(i => i.Path, NaturalComparer.Instance)],
        CombineSort.Date => [.. items.OrderBy(i => i.Modified).ThenBy(i => i.Path, NaturalComparer.Instance)],
        _ => [.. items],
    };
}

/// <summary>Explorer's ordering: "file2" before "file10", case-insensitive.</summary>
public sealed partial class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);
}
