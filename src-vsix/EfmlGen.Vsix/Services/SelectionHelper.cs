using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace EfmlGen.Vsix.Services
{
    /// <summary>
    /// Resolves the currently-selected item in Solution Explorer to a filesystem path.
    /// Returns null when nothing or multiple items are selected, or selection is not a file.
    /// MUST be called on the main thread.
    /// </summary>
    internal static class SelectionHelper
    {
        public static string TryGetSelectedFilePath(IAsyncServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var monitorSelection = ThreadHelper.JoinableTaskFactory.Run(async () =>
                await services.GetServiceAsync(typeof(SVsShellMonitorSelection))) as IVsMonitorSelection;
            if (monitorSelection == null) return null;

            IntPtr hierPtr = IntPtr.Zero;
            IntPtr selContainerPtr = IntPtr.Zero;
            try
            {
                if (ErrorHandler.Failed(monitorSelection.GetCurrentSelection(
                        out hierPtr, out var itemId, out var multi, out selContainerPtr)))
                {
                    return null;
                }
                if (multi != null || itemId == VSConstants.VSITEMID_NIL || hierPtr == IntPtr.Zero)
                {
                    return null;
                }
                var hierarchy = Marshal.GetObjectForIUnknown(hierPtr) as IVsHierarchy;
                if (hierarchy == null) return null;

                if (ErrorHandler.Failed(hierarchy.GetCanonicalName(itemId, out var path)))
                {
                    return null;
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return path;
            }
            finally
            {
                if (hierPtr != IntPtr.Zero) Marshal.Release(hierPtr);
                if (selContainerPtr != IntPtr.Zero) Marshal.Release(selContainerPtr);
            }
        }

        public static bool IsEfmlSelected(IAsyncServiceProvider services)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = TryGetSelectedFilePath(services);
            return path != null
                && string.Equals(Path.GetExtension(path), ".efml", StringComparison.OrdinalIgnoreCase);
        }
    }
}
