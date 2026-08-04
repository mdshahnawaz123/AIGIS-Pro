using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace AiGisConverter.Presentation.Services;

/// <summary>
/// Shows the operating system's file and folder pickers.
/// </summary>
/// <remarks>
/// Abstracted so a view model can be tested without a window. A dialog opened directly from a
/// command is a view model that can only be exercised by a human.
/// </remarks>
public interface IDialogService
{
    /// <summary>Asks for one or more drawings to convert.</summary>
    /// <param name="supportedExtensions">The extensions readers can handle, each with a leading dot.</param>
    /// <returns>The chosen paths, or empty when cancelled.</returns>
    IReadOnlyList<string> PickDrawings(IReadOnlyList<string> supportedExtensions);

    /// <summary>Asks where the outputs should go.</summary>
    /// <param name="initialPath">Where to start browsing.</param>
    /// <returns>The chosen folder, or null when cancelled.</returns>
    string? PickOutputFolder(string? initialPath = null);

    /// <summary>Asks the operator to confirm something.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">What is being confirmed.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    bool Confirm(string title, string message);
}

/// <summary>The Windows file pickers.</summary>
public sealed class WpfDialogService : IDialogService
{
    /// <inheritdoc />
    public IReadOnlyList<string> PickDrawings(IReadOnlyList<string> supportedExtensions)
    {
        // The filter is built from what readers actually claim, so installing a plugin widens the
        // dialog without anyone editing a hard-coded list of formats.
        string patterns = supportedExtensions.Count == 0
            ? "*.*"
            : string.Join(";", supportedExtensions.Select(static extension => $"*{extension}"));

        OpenFileDialog dialog = new()
        {
            Title = "Select drawings to convert",
            Multiselect = true,
            Filter = $"Supported drawings ({patterns})|{patterns}|All files (*.*)|*.*",
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    /// <inheritdoc />
    public string? PickOutputFolder(string? initialPath = null)
    {
        OpenFolderDialog dialog = new() { Title = "Select the output folder" };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
}
