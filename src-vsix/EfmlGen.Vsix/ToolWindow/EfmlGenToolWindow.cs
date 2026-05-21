using System.Runtime.InteropServices;
using EfmlGen.Vsix.Services;
using Microsoft.VisualStudio.Shell;

namespace EfmlGen.Vsix.ToolWindow
{
    /// <summary>
    /// Hosts the WPF UserControl. VS instantiates this via reflection
    /// (default constructor) so DataContext is wired post-creation by the
    /// package after it can hand over an <see cref="OutputPaneLogger"/>.
    /// </summary>
    [Guid(PackageGuids.ToolWindowGuidString)]
    public sealed class EfmlGenToolWindow : ToolWindowPane
    {
        public EfmlGenToolWindow() : base(null)
        {
            Caption = "EfmlGen";
            Content = new EfmlGenToolWindowControl();
        }

        internal void Initialize(OutputPaneLogger log)
        {
            var control = (EfmlGenToolWindowControl)Content;
            var vm = new ToolWindowViewModel(log);
            control.Initialize(vm);
        }
    }
}
