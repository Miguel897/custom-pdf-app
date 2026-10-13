using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace PdfDesk.App.Views;

/// <summary>Shows the file name or the folder of a path (recent files list).</summary>
public sealed class PathPartConverter : IValueConverter
{
    public string Part { get; set; } = "FileName";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string path
            ? (Part == "Folder" ? Path.GetDirectoryName(path) : Path.GetFileName(path)) ?? ""
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
