using System;
using System.ComponentModel.Design;
using EfmlGen.Vsix.Services;
using EfmlGen.Vsix.ToolWindow;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace EfmlGen.Vsix.Commands
{
    /// <summary>Tools menu → "EfmlGen Tool Window". Opens or focuses the tool window.</summary>
    internal sealed class ShowToolWindowCommand
    {
        private readonly AsyncPackage _package;
        private readonly OutputPaneLogger _log;

        private ShowToolWindowCommand(AsyncPackage package, OutputPaneLogger log)
        {
            _package = package;
            _log = log;
        }

        public static async Task RegisterAsync(AsyncPackage package, OutputPaneLogger log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("IMenuCommandService unavailable.");
            var instance = new ShowToolWindowCommand(package, log);
            commandService.AddCommand(new MenuCommand(instance.OnExecute,
                new CommandID(PackageGuids.CommandSetGuid, PackageGuids.CmdIdShowToolWindow)));
        }

        private void OnExecute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                var window = await _package.ShowToolWindowAsync(
                    typeof(EfmlGenToolWindow), id: 0, create: true,
                    cancellationToken: _package.DisposalToken) as EfmlGenToolWindow;
                if (window?.Frame is IVsWindowFrame frame)
                {
                    window.Initialize(_log);
                    ErrorHandler.ThrowOnFailure(frame.Show());
                }
            }).FileAndForget("efmlgen/vsix/showtoolwindow");
        }
    }
}
