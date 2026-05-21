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
    /// Solution Explorer right-click on <c>*.efml</c> → "Update Model from Database…".
    /// Re-runs <c>scaffold-efml</c> against the selected file using the saved profile
    /// (last-used). Merge logic in <c>EfmlMerger</c> preserves p1:Guid and user renames.
    /// </summary>
    internal sealed class UpdateFromDbCommand
    {
        private readonly AsyncPackage _package;
        private readonly OutputPaneLogger _log;

        private UpdateFromDbCommand(AsyncPackage package, OutputPaneLogger log)
        {
            _package = package;
            _log = log;
        }

        public static async Task RegisterAsync(AsyncPackage package, OutputPaneLogger log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("IMenuCommandService unavailable.");
            var instance = new UpdateFromDbCommand(package, log);
            var menuItem = new OleMenuCommand(instance.OnExecute,
                new CommandID(PackageGuids.CommandSetGuid, PackageGuids.CmdIdUpdateFromDb));
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
                _log.WriteLine($"=== Update Model from Database: {efmlPath} ===");

                var settings = ProfileStore.Load();
                var profileName = settings.LastUsedProfileName;
                if (string.IsNullOrEmpty(profileName) || settings.Profiles.Count == 0)
                {
                    _log.WriteLine("[error] No saved profile found. Open the EfmlGen Tool Window (Tools menu) " +
                                   "to create a connection profile, then retry. Profile file: " +
                                   $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\EfmlGen\\profiles.json");
                    return;
                }

                _log.WriteLine($"Using profile: {profileName}");
                var args = new[]
                {
                    "--profile", profileName,
                    "--out", efmlPath,
                    // --name/--namespace come from profile; CLI flag override above.
                };

                try
                {
                    var exit = await CliRunner.RunAsync(
                        verb: "scaffold-efml",
                        args: args,
                        onStdout: new Progress<string>(s => _log.WriteLine(s)),
                        onStderr: new Progress<string>(s => _log.WriteLine("[stderr] " + s)),
                        workingDirectory: Path.GetDirectoryName(efmlPath),
                        cancellationToken: _package.DisposalToken);
                    _log.WriteLine($"=== scaffold-efml exit: {exit} ===");
                }
                catch (Exception ex)
                {
                    _log.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
                }
            }).FileAndForget("efmlgen/vsix/updatefromdb");
        }
    }
}
