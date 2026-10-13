using System.Windows;

namespace PdfDesk.App.Views;

public partial class PasswordWindow : Window
{
    public PasswordWindow(string fileName, bool retry)
    {
        InitializeComponent();
        Prompt.Text = $"{fileName} is password protected. Enter the password to open it.";
        RetryText.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Input.Focus();
    }

    public string Password => Input.Password;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
