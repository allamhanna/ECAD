using System.Windows;

namespace Ecad.App.Views;

/// <summary>
/// Draws or edits a cable definition line's Cable Tag — the only field this dialog asks for. No core
/// count/cross-section setup is required here: crossings are auto-detected and auto-numbered the moment
/// this is confirmed (see ProjectSession.DrawCableLine/ReassignCableLine). Shown both when finalizing a
/// brand-new line (Remove hidden) and when double-clicking an existing one to re-home or remove it
/// (Remove shown).
/// </summary>
public partial class CableLineDialog : Window
{
    public CableLineDialog(string currentCableTag, bool isExisting)
    {
        InitializeComponent();

        TagBox.Text = currentCableTag;
        RemoveButton.Visibility = isExisting ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            TagBox.Focus();
            TagBox.SelectAll();
        };
    }

    public string CableTag { get; private set; } = string.Empty;

    /// <summary>True when the user clicked Remove — the caller should delete the cable line entirely.</summary>
    public bool Removed { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var tag = TagBox.Text.Trim();
        if (tag.Length == 0)
        {
            ShowValidation("Cable tag is required.");
            return;
        }

        CableTag = tag;
        DialogResult = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
