using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfDesk.Core;
using PdfDesk.Core.Model;
using PdfDesk.Core.Pdfium;

namespace PdfDesk.App.ViewModels;

/// <summary>
/// One page in the viewer and the thumbnail strip. Bitmaps are rendered on demand and released when
/// the page scrolls far away. Sizes are in points (base frame); <see cref="Scale"/> maps points to DIPs.
/// </summary>
public sealed partial class PageViewModel : ObservableObject
{
    /// <summary>Re-render only when the needed resolution changes by more than this factor.</summary>
    private const double RerenderThreshold = 1.15;

    /// <summary>Caps one page bitmap at ~64 MB even at 400 % on a 4K monitor.</summary>
    private const double MaxPixels = 16_000_000;

    private readonly MainViewModel _owner;
    private CancellationTokenSource? _imageRender;
    private double _imagePixelsPerPoint;
    private bool _thumbnailRequested;

    public PageViewModel(MainViewModel owner, SourceFile source, PageItem item)
    {
        _owner = owner;
        Source = source;
        Item = item;
        (BaseWidth, BaseHeight) = source.Pdfium.PageSizes[item.PageIndex];
        Fields = [.. source.FormFields
            .Where(f => f.PageIndex == item.PageIndex)
            .Select(f => new FieldViewModel(owner, new FieldKey(source.Id, f.Name), f))];
    }

    public Guid Id => Item.Id;

    public PageItem Item { get; private set; }

    public SourceFile Source { get; }

    public double BaseWidth { get; }

    public double BaseHeight { get; }

    /// <summary>Extra rotation chosen by the user, in degrees (applied with a LayoutTransform).</summary>
    public double Angle => Item.Rotation * 90;

    /// <summary>Displayed size in points (after the user's rotation).</summary>
    public double RotatedWidth => Item.Rotation % 2 == 0 ? BaseWidth : BaseHeight;

    public double RotatedHeight => Item.Rotation % 2 == 0 ? BaseHeight : BaseWidth;

    public string ToolTip => $"{Source.FileName} · page {Item.PageIndex + 1}";

    public ObservableCollection<OverlayViewModel> Overlays { get; } = [];

    public IReadOnlyList<FieldViewModel> Fields { get; }

    [ObservableProperty]
    public partial int Number { get; set; }

    /// <summary>DIPs per point at the current zoom.</summary>
    [ObservableProperty]
    public partial double Scale { get; set; } = 96.0 / 72;

    [ObservableProperty]
    public partial ImageSource? Image { get; private set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; private set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public void Update(PageItem item, DocumentState state)
    {
        var rotated = item.Rotation != Item.Rotation;
        Item = item;
        if (rotated)
        {
            OnPropertyChanged(nameof(Angle));
            OnPropertyChanged(nameof(RotatedWidth));
            OnPropertyChanged(nameof(RotatedHeight));
        }

        SyncOverlays(item.Overlays);
        foreach (var field in Fields)
        {
            field.Refresh(state);
        }
    }

    /// <summary>Renders the page if it has no bitmap yet or the resolution changed noticeably.</summary>
    public async void EnsureImage(double pixelsPerPoint)
    {
        pixelsPerPoint = Math.Min(pixelsPerPoint, Math.Sqrt(MaxPixels / (BaseWidth * BaseHeight)));
        var ratio = _imagePixelsPerPoint > 0 ? pixelsPerPoint / _imagePixelsPerPoint : 0;
        if (ratio is > 1 / RerenderThreshold and < RerenderThreshold)
        {
            return;
        }

        _imageRender?.Cancel();
        var cancel = new CancellationTokenSource();
        _imageRender = cancel;
        _imagePixelsPerPoint = pixelsPerPoint;
        try
        {
            var bitmap = await Source.Pdfium.RenderAsync(Item.PageIndex, 0, pixelsPerPoint, cancel.Token);
            if (!cancel.IsCancellationRequested)
            {
                Image = ToImage(bitmap);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _imagePixelsPerPoint = 0;
            Log.Error($"Rendering page {Item.PageIndex + 1} of {Source.FileName} failed", ex);
        }
    }

    public void ReleaseImage()
    {
        _imageRender?.Cancel();
        _imageRender = null;
        _imagePixelsPerPoint = 0;
        Image = null;
    }

    public async void EnsureThumbnail(double maxPixels)
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;
        try
        {
            var bitmap = await Source.Pdfium.RenderAsync(Item.PageIndex, 0, maxPixels / Math.Max(BaseWidth, BaseHeight));
            Thumbnail = ToImage(bitmap);
        }
        catch (Exception ex)
        {
            _thumbnailRequested = false;
            Log.Error($"Thumbnail of page {Item.PageIndex + 1} of {Source.FileName} failed", ex);
        }
    }

    /// <summary>Adds an overlay that exists only in the UI until it is committed with some text.</summary>
    public void AddPendingOverlay(OverlayViewModel overlay) => Overlays.Add(overlay);

    public void RemovePendingOverlay(OverlayViewModel overlay) => Overlays.Remove(overlay);

    private void SyncOverlays(IReadOnlyList<TextOverlay> models)
    {
        var ids = models.Select(m => m.Id).ToHashSet();
        for (var i = Overlays.Count - 1; i >= 0; i--)
        {
            if (!Overlays[i].IsPending && !ids.Contains(Overlays[i].Id))
            {
                Overlays.RemoveAt(i);
            }
        }

        foreach (var model in models)
        {
            var existing = Overlays.FirstOrDefault(o => o.Id == model.Id);
            if (existing is null)
            {
                var overlay = new OverlayViewModel(_owner, this, model.Id);
                overlay.Load(model);
                Overlays.Add(overlay);
            }
            else
            {
                existing.Load(model);
            }
        }
    }

    private static BitmapSource ToImage(RenderedBitmap bitmap)
    {
        // Rendered on opaque white, so the alpha channel can be ignored (Bgr32 draws faster).
        var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32, null, bitmap.Pixels, bitmap.Stride);
        image.Freeze();
        return image;
    }
}
