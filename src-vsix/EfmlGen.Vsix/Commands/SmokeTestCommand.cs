using System;
using System.ComponentModel.Design;
using EfmlGen.Vsix.Services;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace EfmlGen.Vsix.Commands
{
    /// <summary>
    /// Tools menu → "EfmlGen: Smoke Test". Verifies the bundled CLI is reachable
    /// and the streaming pipeline to the Output pane works end-to-end. Calls
    /// <c>db-smoke --help</c> (a trivial verb that prints usage and exits 0/1).
    /// </summary>
    internal sealed class SmokeTestCommand
    {
        private readonly AsyncPackage _package;
        private readonly OutputPaneLogger _log;

        private SmokeTestCommand(AsyncPackage package, OutputPaneLogger log)
        {
            _package = package;
            _log = log;
        }

        public static async Task RegisterAsync(AsyncPackage package, OutputPaneLogger log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("IMenuCommandService unavailable.");
            var instance = new SmokeTestCommand(package, log);
            var cmdId = new CommandID(PackageGuids.CommandSetGuid, PackageGuids.CmdIdSmokeTest);
            commandService.AddCommand(new MenuCommand(instance.OnExecute, cmdId));
        }

        private void OnExecute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _log.ActivateAsync(_package.DisposalToken);
                _log.WriteLine("");
                _log.WriteLine("=== Smoke test: invoking bundled CLI 'db-smoke --help' ===");
                try
                {
                    var exit = await CliRunner.RunAsync(
                        verb: "db-smoke",
                        args: new[] { "--help" },
                        onStdout: new Progress<string>(s => _log.WriteLine(s)),
                        onStderr: new Progress<string>(s => _log.WriteLine("[stderr] " + s)),
                        cancellationToken: _package.DisposalToken);
                    _log.WriteLine($"=== Exit code: {exit} ===");
                }
                catch (Exception ex)
                {
                    _log.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
                }
            }).FileAndForget("efmlgen/vsix/smoketest");
        }
    }
}
