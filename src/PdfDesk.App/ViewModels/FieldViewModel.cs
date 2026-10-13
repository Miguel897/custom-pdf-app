using CommunityToolkit.Mvvm.ComponentModel;
using PdfDesk.Core.Model;

namespace PdfDesk.App.ViewModels;

/// <summary>A fillable AcroForm widget (text field or checkbox) drawn over the page.</summary>
public sealed partial class FieldViewModel(MainViewModel owner, FieldKey key, FormField formField) : ObservableObject
{
    private bool _refreshing;

    public FieldKey Key { get; } = key;

    public FormField Field { get; } = formField;

    public bool IsText => Field.Kind == FormFieldKind.Text;

    public bool IsCheckBox => Field.Kind == FormFieldKind.CheckBox;

    public double X => Field.X;

    public double Y => Field.Y;

    public double Width => Field.Width;

    public double Height => Field.Height;

    public bool IsEditable => !Field.ReadOnly;

    public bool MultiLine => Field.MultiLine;

    public System.Windows.TextAlignment TextAlignment => Field.Alignment switch
    {
        1 => System.Windows.TextAlignment.Center,
        2 => System.Windows.TextAlignment.Right,
        _ => System.Windows.TextAlignment.Left,
    };

    /// <summary>The field's own size, or "auto" (0) fitted to the box height.</summary>
    public double FontSize => Field.FontSize > 0 ? Field.FontSize : Math.Clamp((MultiLine ? 10 : Height * 0.65), 6, 12);

    public string ToolTip => Field.Name;

    [ObservableProperty]
    public partial string Text { get; set; } = formField.Kind == FormFieldKind.Text ? formField.Value : "";

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = formField.Value == "true";

    public void Refresh(DocumentState state)
    {
        var value = state.FormValues.GetValueOrDefault(Key, Field.Value);
        _refreshing = true;
        if (IsText)
        {
            Text = value;
        }
        else
        {
            IsChecked = value == "true";
        }

        _refreshing = false;
    }

    // Text binding uses UpdateSourceTrigger=LostFocus, so this runs once per edit.
    partial void OnTextChanged(string value)
    {
        if (!_refreshing && IsText)
        {
            owner.SetFormValue(Key, value, Field.Value);
        }
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_refreshing && IsCheckBox)
        {
            owner.SetFormValue(Key, value ? "true" : "false", Field.Value);
        }
    }
}
