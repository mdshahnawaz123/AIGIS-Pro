using System.Windows;

namespace AiGisConverter.Presentation.Views.Shell;

/// <summary>
/// The main window.
/// </summary>
/// <remarks>
/// No code behind beyond the generated initialisation. Everything the window does is a binding to
/// <c>ShellViewModel</c>, which is what makes the navigation and the notification list testable
/// without opening a window.
/// </remarks>
public partial class ShellWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="ShellWindow"/> class.</summary>
    public ShellWindow() => InitializeComponent();
}
