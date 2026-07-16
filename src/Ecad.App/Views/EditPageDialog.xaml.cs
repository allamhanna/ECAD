using System.Windows;
using Ecad.Core.Enums;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>Edits an existing Page's own segments/type in place — the Pages sidebar's "Rename" action,
/// same field shape as AddPageDialog but pre-filled and single-page-only (see MainViewModel.RenamePage).</summary>
public partial class EditPageDialog : Window
{
    public EditPageDialog(Page page)
    {
        InitializeComponent();
        PageTypeCombo.ItemsSource = Enum.GetValues<PageType>();
        PageTypeCombo.SelectedItem = page.PageType;

        FunctionBox.Text = page.FunctionSegment ?? string.Empty;
        LocationBox.Text = page.LocationSegment ?? string.Empty;
        DocumentTypeBox.Text = page.DocumentTypeSegment ?? string.Empty;
        PageNumberBox.Text = page.PageNumberSegment ?? string.Empty;

        Loaded += (_, _) =>
        {
            PageNumberBox.Focus();
            PageNumberBox.SelectAll();
        };
    }

    public string? FunctionSegment { get; private set; }
    public string? LocationSegment { get; private set; }
    public string? DocumentTypeSegment { get; private set; }
    public string PageNumberSegment { get; private set; } = string.Empty;
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
