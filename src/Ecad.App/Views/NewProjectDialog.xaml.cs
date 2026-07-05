using System.Windows;

namespace Ecad.App.Views;

public partial class NewProjectDialog : Window
{
    public NewProjectDialog()
    {
        InitializeComponent();
    }

    public string ProjectName { get; private set; } = string.Empty;
    public string? Customer { get; private set; }
    public string? ProjectNumber { get; private set; }
    public string? Revision { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationText.Text = "Name is required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ProjectName = NameBox.Text.Trim();
        Customer = string.IsNullOrWhiteSpace(CustomerBox.Text) ? null : CustomerBox.Text.Trim();
        ProjectNumber = string.IsNullOrWhiteSpace(ProjectNumberBox.Text) ? null : ProjectNumberBox.Text.Trim();
        Revision = string.IsNullOrWhiteSpace(RevisionBox.Text) ? null : RevisionBox.Text.Trim();
        DialogResult = true;
    }
}
