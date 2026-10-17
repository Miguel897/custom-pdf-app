using PdfDesk.Core.Combine;
using PdfDesk.Core.Model;
using PdfDesk.Core.Pdfium;
using PdfDesk.Core.Saving;

namespace PdfDesk.Core.Tests;

public sealed class PdfExporterTests : IDisposable
{
    private readonly string _dir = TestPdfs.TempDir();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<WorkingDocument> OpenAsync(params string[] paths)
    {
        var document = new WorkingDocument();
        var pages = new List<PageItem>();
        foreach (var path in paths)
        {
            pages.AddRange(await document.AddSourceAsync(path));
        }

        document.Initialize(pages, paths.Length == 1 ? paths[0] : null);
        return document;
    }

    private static HashSet<Guid> Ids(WorkingDocument doc, params int[] indices) => [.. indices.Select(i => doc.State.Pages[i].Id)];

    [Fact]
    public async Task SingleSource_ReorderDeleteRotate_IsSavedInOrder()
    {
        var path = TestPdfs.Numbered(_dir, "a.pdf", 4);
        using var doc = await OpenAsync(path);
        doc.Apply(s => PageOperations.Move(s, Ids(doc, 3), 0));       // 3 0 1 2
        doc.Apply(s => PageOperations.Delete(s, Ids(doc, 2)));        // 3 0 2
        doc.Apply(s => PageOperations.Rotate(s, Ids(doc, 1), 1));     // page "0" rotated

        var output = Path.Combine(_dir, "out.pdf");
        PdfExporter.Save(doc.Sources, doc.State, output);

        var sizes = await TestPdfs.PageSizesAsync(output);
        Assert.Equal([(330.0, 500.0), (500.0, 300.0), (320.0, 500.0)], sizes);
    }

    [Fact]
    public async Task Combine_TwoSourcesAndEncrypted_ImportsAllPages()
    {
        var a = TestPdfs.Numbered(_dir, "a.pdf", 2, widthBase: 300);
        var b = TestPdfs.Numbered(_dir, "b.pdf", 1, widthBase: 400);
        var secret = TestPdfs.Encrypted(_dir, "secret.pdf", "pw");

        using var doc = new WorkingDocument();
        var pages = new List<PageItem>();
        pages.AddRange(await doc.AddSourceAsync(a));
        pages.AddRange(await doc.AddSourceAsync(b));
        await Assert.ThrowsAsync<PdfOpenException>(() => doc.AddSourceAsync(secret));
        pages.AddRange(await doc.AddSourceAsync(secret, "pw"));
        doc.Initialize(pages, null);
        doc.Apply(s => PageOperations.Move(s, Ids(doc, 2), 0));

        var output = Path.Combine(_dir, "combined.pdf");
        PdfExporter.Save(doc.Sources, doc.State, output);

        var sizes = await TestPdfs.PageSizesAsync(output); // output is not encrypted
        Assert.Equal([400.0, 300.0, 310.0, 300.0, 310.0], sizes.Select(s => s.Width));
    }

    [Fact]
    public async Task Keep_SavesOnlyTheChosenPages_AndLeavesTheSourceAlone()
    {
        var path = TestPdfs.Numbered(_dir, "a.pdf", 4);
        using var doc = await OpenAsync(path);
        doc.Apply(s => PageOperations.Rotate(s, Ids(doc, 3), 1));

        var output = Path.Combine(_dir, "extract.pdf");
        PdfExporter.Save(doc.Sources, PageOperations.Keep(doc.State, Ids(doc, 3, 1)), output);

        Assert.Equal([(310.0, 500.0), (500.0, 330.0)], await TestPdfs.PageSizesAsync(output));
        Assert.Equal(4, (await TestPdfs.PageSizesAsync(path)).Count);
    }

    [Fact]
    public async Task Save_OverwritesTheOpenSourceFile()
    {
        var path = TestPdfs.Numbered(_dir, "a.pdf", 3);
        using var doc = await OpenAsync(path);
        doc.Apply(s => PageOperations.Delete(s, Ids(doc, 0)));
        PdfExporter.Save(doc.Sources, doc.State, path);
        Assert.Equal(2, (await TestPdfs.PageSizesAsync(path)).Count);
        Assert.False(File.Exists(path + ".pdfdesk-tmp"));
    }

    [Theory]
    [InlineData(0, 0, 0, 595, 842)]
    [InlineData(90, 0, 0, 595, 842)]
    [InlineData(180, 0, 0, 595, 842)]
    [InlineData(270, 0, 0, 595, 842)]
    [InlineData(0, 0, 0, 842, 595)]     // landscape media box
    [InlineData(90, 50, 30, 650, 830)]  // offset media box
    [InlineData(0, 50, 30, 650, 830)]
    public async Task FlattenedAndAnnotationText_AppearWhereTheyWerePlaced(int rotate, double x0, double y0, double x1, double y1)
    {
        foreach (var kind in new[] { TextKind.Flattened, TextKind.Annotation })
        {
            var path = TestPdfs.Blank(_dir, $"blank-{kind}.pdf", x0, y0, x1, y1, rotate);
            using var doc = await OpenAsync(path);
            var overlay = new TextOverlay(Guid.NewGuid(), kind, 100, 150, 0, "MMMMMM", 20, "#FF0000");
            doc.Apply(s => PageOperations.AddOverlay(s, s.Pages[0].Id, overlay));
            var output = Path.Combine(_dir, $"text-{kind}.pdf");
            PdfExporter.Save(doc.Sources, doc.State, output);

            var bounds = await TestPdfs.RedBoundsAsync(output);
            Assert.True(bounds.HasValue, $"{kind}: no red text rendered");
            var (left, top, right, bottom) = bounds.Value;
            Assert.True(left is >= 98 and <= 106 && top is >= 150 and <= 162, $"{kind}: bounds {bounds}");
            Assert.True(right - left > 2 * (bottom - top), $"{kind}: text is not horizontal ({bounds})");
        }
    }

    [Fact]
    public async Task RotatedText_OnRotatedPage_StaysUprightForTheViewer()
    {
        // Page shown rotated 90° clockwise by the user; text typed upright on screen has Rotation 3.
        var path = TestPdfs.Blank(_dir, "blank.pdf", 0, 0, 595, 842, 0);
        using var doc = await OpenAsync(path);
        doc.Apply(s => PageOperations.Rotate(s, Ids(doc, 0), 1));
        var overlay = new TextOverlay(Guid.NewGuid(), TextKind.Flattened, 100, 300, 3, "MMMMMM", 20, "#FF0000");
        doc.Apply(s => PageOperations.AddOverlay(s, s.Pages[0].Id, overlay));
        var output = Path.Combine(_dir, "rotated.pdf");
        PdfExporter.Save(doc.Sources, doc.State, output);

        var (left, top, right, bottom) = (await TestPdfs.RedBoundsAsync(output))!.Value;
        Assert.True(right - left > 2 * (bottom - top), "text is not horizontal in the saved (rotated) page");
    }

    [Fact]
    public async Task FormFields_AreReadAndFilled_InPlaceAndWhenCombined()
    {
        var form = TestPdfs.Form(_dir, "form.pdf");
        using var doc = await OpenAsync(form);
        var source = doc.Sources[0];
        var name = Assert.Single(source.FormFields, f => f.Kind == FormFieldKind.Text);
        var box = Assert.Single(source.FormFields, f => f.Kind == FormFieldKind.CheckBox);
        Assert.Equal("person.name", name.Name);
        Assert.Equal(12, name.FontSize);
        Assert.Equal((50.0, 400 - 320.0, 200.0, 20.0), (name.X, name.Y, name.Width, name.Height));
        Assert.Equal("false", box.Value);

        doc.Apply(s => PageOperations.SetFormValue(s, new FieldKey(source.Id, name.Name), "José Núñez", ""));
        doc.Apply(s => PageOperations.SetFormValue(s, new FieldKey(source.Id, box.Name), "true", "false"));
        doc.Apply(s => PageOperations.Move(s, Ids(doc, 1), 0));

        var inPlace = Path.Combine(_dir, "filled.pdf");
        PdfExporter.Save(doc.Sources, doc.State, inPlace);
        using (var reopened = await OpenAsync(inPlace))
        {
            var fields = reopened.Sources[0].FormFields;
            Assert.Equal("José Núñez", fields.Single(f => f.Name == "person.name").Value);
            Assert.Equal("true", fields.Single(f => f.Name == "agree").Value);
            Assert.Equal(1, fields[0].PageIndex); // the form page moved to position 2
        }

        // Combined with another file: imported pages, values still visible.
        var other = TestPdfs.Numbered(_dir, "other.pdf", 1);
        using var combined = await OpenAsync(form, other);
        var formId = combined.Sources[0].Id;
        combined.Apply(s => PageOperations.SetFormValue(s, new FieldKey(formId, "person.name"), "Ana", ""));
        var output = Path.Combine(_dir, "combined-form.pdf");
        PdfExporter.Save(combined.Sources, combined.State, output);
        Assert.Equal(3, (await TestPdfs.PageSizesAsync(output)).Count);
    }

    [Fact]
    public async Task UndoRedo_TracksDirtyState()
    {
        var path = TestPdfs.Numbered(_dir, "a.pdf", 2);
        using var doc = await OpenAsync(path);
        Assert.False(doc.IsDirty);
        doc.Apply(s => PageOperations.Delete(s, Ids(doc, 0)));
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.False(doc.IsDirty);
        Assert.Equal(2, doc.State.Pages.Count);
        doc.Redo();
        Assert.Single(doc.State.Pages);
        doc.MarkSaved(path);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task FolderScanner_FlagsBadFiles_AndSortsNaturally()
    {
        TestPdfs.Numbered(_dir, "doc10.pdf", 1);
        TestPdfs.Numbered(_dir, "doc2.pdf", 2);
        TestPdfs.Encrypted(_dir, "locked.pdf", "pw");
        File.WriteAllText(Path.Combine(_dir, "broken.pdf"), "not a pdf");

        var items = await FolderScanner.ScanAsync(_dir, includeSubfolders: false);

        Assert.Equal(["broken.pdf", "doc2.pdf", "doc10.pdf", "locked.pdf", "plain-locked.pdf"], items.Select(i => i.FileName));
        Assert.Equal("damaged or not a PDF", items[0].Problem);
        Assert.Equal(2, items[1].PageCount);
        Assert.Equal("password protected", items[3].Problem);
    }
}
