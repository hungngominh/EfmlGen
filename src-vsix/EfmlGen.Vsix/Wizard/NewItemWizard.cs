using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using EfmlGen.Vsix.Services;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TemplateWizard;

namespace EfmlGen.Vsix.Wizard
{
    /// <summary>
    /// IWizard wired to the EfmlGenEntityModel.vstemplate. Workflow:
    ///   1. RunStarted: show modal AddEfmlModelDialog. Capture profile + model name + namespace + tables.
    ///   2. Replace placeholders so VS uses the chosen model name as the file name.
    ///   3. ProjectItemFinishedGenerating: VS has dropped the placeholder .efml on disk.
    ///      Run `scaffold-efml --out &lt;that file&gt;` synchronously to overwrite it with real content.
    /// </summary>
    [Guid(PackageGuids.WizardGuidString)]
    public sealed class NewItemWizard : IWizard
    {
        private AddEfmlModelOptions _options;

        public void RunStarted(
            object automationObject,
            Dictionary<string, string> replacementsDictionary,
            WizardRunKind runKind,
            object[] customParams)
        {
            replacementsDictionary.TryGetValue("$rootnamespace$", out var rootNs);
            replacementsDictionary.TryGetValue("$safeitemname$", out var defaultName);

            var dlg = new AddEfmlModelDialog(defaultName ?? "Entities", rootNs ?? "")
            {
                Owner = System.Windows.Application.Current?.MainWindow,
            };
            if (dlg.ShowDialog() != true)
            {
                throw new WizardBackoutException("User cancelled.");
            }

            _options = dlg.Result;
            // Use the user-typed model name as the file's safe item name so the .efml is named correctly.
            if (!string.IsNullOrEmpty(_options.ModelName))
            {
                replacementsDictionary["$safeitemname$"] = _options.ModelName;
                replacementsDictionary["$fileinputname$"] = _options.ModelName;
            }
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_options == null) return;
            if (projectItem == null) return;

            // ProjectItem.FileNames is a 1-based COM-indexed property.
            string targetPath;
            try { targetPath = projectItem.FileNames[1]; }
            catch { return; }
            if (string.IsNullOrEmpty(targetPath)) return;
            if (!string.Equals(Path.GetExtension(targetPath), ".efml", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Run scaffold-efml synchronously. We're already on the main thread but the
            // CLI subprocess is independent, so blocking here is safe (and simpler than
            // wiring an async wizard flow).
            var args = new List<string>
            {
                "--profile", _options.ProfileName,
                "--name", _options.ModelName,
                "--namespace", _options.Namespace,
                "--out", targetPath,
            };
            if (!string.IsNullOrWhiteSpace(_options.Tables))
            {
                args.Add("--tables");
                args.Add(_options.Tables);
            }

            try
            {
                var exit = ThreadHelper.JoinableTaskFactory.Run(async () => await CliRunner.RunAsync(
                    verb: "scaffold-efml",
                    args: args,
                    onStdout: new Progress<string>(s => System.Diagnostics.Debug.WriteLine("[scaffold] " + s)),
                    onStderr: new Progress<string>(s => System.Diagnostics.Debug.WriteLine("[scaffold/err] " + s)),
                    workingDirectory: Path.GetDirectoryName(targetPath)));
                if (exit != 0)
                {
                    System.Windows.MessageBox.Show(
                        $"scaffold-efml exited with code {exit}. The .efml may be empty — check Output → EfmlGen for details.",
                        "EfmlGen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to run scaffold-efml: {ex.Message}",
                    "EfmlGen", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public bool ShouldAddProjectItem(string filePath) => true;
        public void BeforeOpeningFile(ProjectItem projectItem) { }
        public void ProjectFinishedGenerating(Project project) { }
        public void RunFinished() { }
    }
}
