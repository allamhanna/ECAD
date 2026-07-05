using System.Windows;
using Ecad.Core.Enums;

namespace Ecad.App.Views;

public partial class AddPageDialog : Window
{
    public AddPageDialog()
    {
        InitializeComponent();
        PageTypeCombo.ItemsSource = Enum.GetValues<PageType>();
        PageTypeCombo.SelectedItem = PageType.Schematic;
    }

    public string? FunctionSegment { get; private set; }
    public string? LocationSegment { get; private set; }
    public string? DocumentTypeSegment { get; private set; }
    public string? PageNumberSegment { get; private set; }
    public PageType SelectedPageType { get; private set; } = PageType.Schematic;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PageNumberBox.Text))
        {
            ValidationText.Text = "Page Number is required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        FunctionSegment = string.IsNullOrWhiteSpace(FunctionBox.Text) ? null : FunctionBox.Text.Trim();
        LocationSegment = string.IsNullOrWhiteSpace(LocationBox.Text) ? null : LocationBox.Text.Trim();
        DocumentTypeSegment = string.IsNullOrWhiteSpace(DocumentTypeBox.Text) ? null : DocumentTypeBox.Text.Trim();
        PageNumberSegment = PageNumberBox.Text.Trim();
        SelectedPageType = (PageType)PageTypeCombo.SelectedItem;
        DialogResult = true;
    }
}
