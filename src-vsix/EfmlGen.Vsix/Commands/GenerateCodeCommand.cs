using System;
using System.ComponentModel.Design;
using System.IO;
using EfmlGen.Vsix.Services;
using EfmlGen.Wpf.Services;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace EfmlGen.Vsix.Commands
{
    /// <summary>
    /// Solution Explorer right-click on <c>*.efml</c> → "Generate Code".
    /// Runs <c>gen-code --efml &lt;selected&gt; --out &lt;dir-of-selected&gt;</c>.
    /// Provider + context class read from last-used profile when available.
    /// </summary>
    internal sealed class GenerateCodeCommand
    {
        private readonly AsyncPackage _package;
        private readonly OutputPaneLogger _log;

        private GenerateCodeCommand(AsyncPackage package, OutputPaneLogger log)
        {
            _package = package;
            _log = log;
        }

        public static async Task RegisterAsync(AsyncPackage package, OutputPaneLogger log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("IMenuCommandService unavailable.");
            var instance = new GenerateCodeCommand(package, log);
            var menuItem = new OleMenuCommand(instance.OnExecute,
                new CommandID(PackageGuids.CommandSetGuid, PackageGuids.CmdIdGenerateCode));
            menuItem.BeforeQueryStatus += instance.OnQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private void OnQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is OleMenuCommand cmd)
            {
                var visible = SelectionHelper.IsEfmlSelected(_package);
                cmd.Visible = visible;
                cmd.Enabled = visible;
            }
        }

        private void OnExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var efmlPath = SelectionHelper.TryGetSelectedFilePath(_package);
            if (efmlPath == null || !efmlPath.EndsWith(".efml", StringComparison.OrdinalIgnoreCase))
            {
                _log.WriteLine("[error] No .efml file selected.");
                return;
            }

            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _log.ActivateAsync(_package.DisposalToken);
                _log.WriteLine("");
                _log.WriteLine($"=== Generate Code: {efmlPath} ===");

                var outDir = Path.GetDirectoryName(efmlPath);
                var args = new System.Collections.Generic.List<string>
                {
                    "--efml", efmlPath,
                    "--out", outDir,
                };

                // Augment from last-used profile if available — CLI flags here override.
                var settings = ProfileStore.Load();
                var profileName = settings.LastUsedProfileName;
                if (!string.IsNullOrEmpty(profileName))
                {
                    args.Add("--profile");
                    args.Add(profileName);
                }

                try
                {
                    var exit = await CliRunner.RunAsync(
                        verb: "gen-code",
                        args: args,
                        onStdout: new Progress<string>(s => _log.WriteLine(s)),
                        onStderr: new Progress<string>(s => _log.WriteLine("[stderr] " + s)),
                        workingDirectory: outDir,
                        cancellationToken: _package.DisposalToken);
                    _log.WriteLine($"=== gen-code exit: {exit} ===");

                    if (exit == 3)
                    {
                        // Collision detector found duplicate names. Offer to re-run with --force.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
                        var choice = System.Windows.MessageBox.Show(
                            "CollisionDetector found issues in this .efml (duplicate property or class names). " +
                            "See Output → EfmlGen for details.\n\n" +
                            "Click YES to rerun with --force (generates code anyway — fix collisions later).\n" +
                            "Click NO to abort and fix the .efml manually.",
                            "EfmlGen: gen-code collision",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);
                        if (choice == System.Windows.MessageBoxResult.Yes)
                        {
                            var forced = new System.Collections.Generic.List<string>(args) { "--force" };
                            _log.WriteLine("--- Retrying with --force ---");
                            var exit2 = await CliRunner.RunAsync(
                                verb: "gen-code",
                                args: forced,
                                onStdout: new Progress<string>(s => _log.WriteLine(s)),
                                onStderr: new Progress<string>(s => _log.WriteLine("[stderr] " + s)),
                                workingDirectory: outDir,
                                cancellationToken: _package.DisposalToken);
                            _log.WriteLine($"=== gen-code --force exit: {exit2} ===");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
                }
            }).FileAndForget("efmlgen/vsix/gencode");
        }
    }
}
