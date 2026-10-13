using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfDesk.Core.Model;
using PdfDesk.Core.Saving;

namespace PdfDesk.App.ViewModels;

/// <summary>
/// A text box on a page. While typing, changes stay here; the document state is updated when the
/// box loses focus, is moved, or its style changes (one undo step each).
/// </summary>
public sealed partial class OverlayViewModel(MainViewModel owner, PageViewModel page, Guid id) : ObservableObject
{
    public MainViewModel Owner { get; } = owner;

    public PageViewModel Page { get; } = page;

    public Guid Id { get; } = id;

    /// <summary>Last committed version; null while the box is new and not yet in the document.</summary>
    public TextOverlay? Committed { get; private set; }

    public bool IsPending => Committed is null;

    /// <summary>Set for a box just created by a click, so the view focuses it once it appears.</summary>
    public bool FocusOnLoad { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Angle))]
    public partial int Rotation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineHeight))]
    public partial double FontSize { get; set; } = 11;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foreground))]
    public partial string Color { get; set; } = "#000000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnnotation))]
    public partial TextKind Kind { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public double Angle => Rotation * 90;

    public double LineHeight => FontSize * PdfExporter.LineSpacing;

    public bool IsAnnotation => Kind == TextKind.Annotation;

    public Brush Foreground
    {
        get
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(Color));
            brush.Freeze();
            return brush;
        }
    }

    public TextOverlay ToModel() => new(Id, Kind, X, Y, Rotation, Text, FontSize, Color);

    public void Load(TextOverlay model)
    {
        Committed = model;
        Text = model.Text;
        X = model.X;
        Y = model.Y;
        Rotation = model.Rotation;
        FontSize = model.FontSize;
        Color = model.Color;
        Kind = model.Kind;
    }
}
