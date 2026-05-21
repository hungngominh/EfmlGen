using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace EfmlGen.Vsix.Services
{
    /// <summary>
    /// Wraps the "EfmlGen" Output Window pane. Writes are thread-safe.
    /// Get the singleton via <see cref="GetAsync"/> once per package init,
    /// reuse the returned instance for the lifetime of the session.
    /// </summary>
    internal sealed class OutputPaneLogger
    {
        // Stable GUID for the pane — same value across sessions so VS preserves ordering.
        private static readonly Guid PaneGuid = new Guid("e4f7b1a9-2d5e-4c63-9c1f-7a8b3e2d4f51");
        private const string PaneTitle = "EfmlGen";

        private readonly IVsOutputWindowPane _pane;

        private OutputPaneLogger(IVsOutputWindowPane pane)
        {
            _pane = pane;
        }

        public static async Task<OutputPaneLogger> GetAsync(IAsyncServiceProvider services, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var outputWindow = await services.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow
                ?? throw new InvalidOperationException("SVsOutputWindow service unavailable.");

            var guid = PaneGuid;
            // CreatePane is idempotent — calling for an existing guid returns the existing pane.
            ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(ref guid, PaneTitle, fInitVisible: 1, fClearWithSolution: 0));
            ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref guid, out var pane));
            return new OutputPaneLogger(pane);
        }

        /// <summary>Append a line. Safe to call from any thread.</summary>
        public void WriteLine(string text)
        {
            // OutputStringThreadSafe is documented as callable off the UI thread
            // (https://learn.microsoft.com/dotnet/api/microsoft.visualstudio.shell.interop.ivsoutputwindowpane.outputstringthreadsafe).
            // The VSTHRD010 analyzer can't tell, so suppress here only.
#pragma warning disable VSTHRD010
            _pane.OutputStringThreadSafe((text ?? string.Empty) + Environment.NewLine);
#pragma warning restore VSTHRD010
        }

        /// <summary>Append text without newline. Safe to call from any thread.</summary>
        public void Write(string text)
        {
#pragma warning disable VSTHRD010
            _pane.OutputStringThreadSafe(text ?? string.Empty);
#pragma warning restore VSTHRD010
        }

        /// <summary>Force pane visible + activate. Must run on main thread.</summary>
        public async Task ActivateAsync(CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            _pane.Activate();
        }
    }
}
