using System.Globalization;
using System.Windows;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>Edits an existing Cable's own fields in place — the Cables Navigator's "Edit Cable..."
/// action, replacing the Grid Editor's old inline cell-edit for this same data (the navigator's grid
/// is read-only, see CablesNavigatorView). Single-cable-only, same scoping precedent as
/// EditDeviceTagDialog/EditPageDialog.</summary>
public partial class EditCableDialog : Window
{
    public EditCableDialog(Cable cable)
    {
        InitializeComponent();
        TagBox.Text = cable.Tag;
        TypeDesignationBox.Text = cable.TypeDesignation ?? string.Empty;
        LengthBox.Text = cable.LengthMm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        EndTypeClassificationBox.Text = cable.EndTypeClassification ?? string.Empty;

        Loaded += (_, _) =>
        {
            TagBox.Focus();
            TagBox.SelectAll();
        };
    }

    public string TagValue { get; private set; } = string.Empty;
    public string? TypeDesignationValue { get; private set; }
    public double? LengthMmValue { get; private set; }
    public string? EndTypeClassificationValue { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TagBox.Text))
        {
            ValidationText.Text = "Tag is required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        double? lengthMm = null;
        if (!string.IsNullOrWhiteSpace(LengthBox.Text))
        {
            if (!double.TryParse(LengthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                ValidationText.Text = "Length must be a number.";
                ValidationText.Visibility = Visibility.Visible;
                return;
            }
            lengthMm = parsed;
        }

        TagValue = TagBox.Text.Trim();
        TypeDesignationValue = string.IsNullOrWhiteSpace(TypeDesignationBox.Text) ? null : TypeDesignationBox.Text.Trim();
        LengthMmValue = lengthMm;
        EndTypeClassificationValue = string.IsNullOrWhiteSpace(EndTypeClassificationBox.Text) ? null : EndTypeClassificationBox.Text.Trim();
        DialogResult = true;
    }
}
