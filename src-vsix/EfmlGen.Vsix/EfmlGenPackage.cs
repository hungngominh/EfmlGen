using System;
using System.Runtime.InteropServices;
using System.Threading;
using EfmlGen.Vsix.Commands;
using EfmlGen.Vsix.Services;
using EfmlGen.Vsix.ToolWindow;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace EfmlGen.Vsix
{
    /// <summary>
    /// Entry point package cho EfmlGen VSIX. AsyncPackage cho phép VS load extension
    /// trong background thread không block UI khi khởi động.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "0.3.0", IconResourceID = 400)]
    [Guid(PackageGuids.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(EfmlGenToolWindow), Style = VsDockStyle.Tabbed, Window = "DocumentWell")]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideBindingPath]
    public sealed class EfmlGenPackage : AsyncPackage
    {
        internal OutputPaneLogger Log { get; private set; }

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Log = await OutputPaneLogger.GetAsync(this, cancellationToken);
            Log.WriteLine("[EfmlGen] Extension loaded.");

            await SmokeTestCommand.RegisterAsync(this, Log);
            await UpdateFromDbCommand.RegisterAsync(this, Log);
            await GenerateCodeCommand.RegisterAsync(this, Log);
            await ShowToolWindowCommand.RegisterAsync(this, Log);
        }
    }
}
