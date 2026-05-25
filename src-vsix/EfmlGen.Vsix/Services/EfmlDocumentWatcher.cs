using System;
using System.IO;
using EfmlGen.Vsix.ToolWindow;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace EfmlGen.Vsix.Services
{
    /// <summary>
    /// Watches DTE.DocumentEvents.DocumentOpened. When a .efml file is opened
    /// (e.g. double-click in Solution Explorer), opens the tool window and
    /// loads — or creates — the connection profile bound to that file's path.
    /// </summary>
    internal sealed class EfmlDocumentWatcher
    {
        private readonly AsyncPackage _package;
        private readonly OutputPaneLogger _log;
        // Holding strong references is mandatory: EnvDTE event sinks are released
        // when their wrapper objects get GC'd, and handlers stop firing silently.
        private DTE2 _dte;
        private Events _events;
        private DocumentEvents _docEvents;

        private EfmlDocumentWatcher(AsyncPackage package, OutputPaneLogger log)
        {
            _package = package;
            _log = log;
        }

        public static async System.Threading.Tasks.Task<EfmlDocumentWatcher> RegisterAsync(AsyncPackage package, OutputPaneLogger log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2
                ?? throw new InvalidOperationException("DTE service unavailable.");
            var instance = new EfmlDocumentWatcher(package, log);
            instance._dte = dte;
            instance._events = dte.Events;
            instance._docEvents = instance._events.DocumentEvents;
            instance._docEvents.DocumentOpened += instance.OnDocumentOpened;
            return instance;
        }

        private void OnDocumentOpened(Document doc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (doc == null) return;
            string fullName;
            try { fullName = doc.FullName; }
            catch { return; }
            if (string.IsNullOrEmpty(fullName)) return;
            if (!string.Equals(Path.GetExtension(fullName), ".efml", StringComparison.OrdinalIgnoreCase)) return;

            // Capture path first — doc handle becomes unusable after Close().
            var capturedDoc = doc;

            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

                // Dismiss the XML editor tab VS auto-opened — only the tool window should show.
                try { capturedDoc.Close(vsSaveChanges.vsSaveChangesNo); }
                catch (Exception ex) { _log?.WriteLine($"[warn] Could not close .efml editor tab: {ex.Message}"); }

                var window = await _package.ShowToolWindowAsync(
                    typeof(EfmlGenToolWindow), id: 0, create: true,
                    cancellationToken: _package.DisposalToken) as EfmlGenToolWindow;
                if (window?.Frame is IVsWindowFrame frame)
                {
                    window.Initialize(_log);
                    ErrorHandler.ThrowOnFailure(frame.Show());
                    window.LoadEfml(fullName);
                }
            }).FileAndForget("efmlgen/vsix/efmldocumentwatcher");
        }
    }
}
