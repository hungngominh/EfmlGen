using System;
using System.IO;

namespace EfmlGen.Vsix.Services
{
    /// <summary>
    /// Resolves the on-disk path of the bundled <c>EfmlGen.Cli.exe</c>.
    /// At install time VS unpacks the .vsix into
    /// <c>%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_xxxx\Extensions\&lt;publisher&gt;\&lt;name&gt;\&lt;version&gt;\</c>
    /// and the bundled CLI lands under <c>tools\cli\EfmlGen.Cli.exe</c> sibling to the package DLL.
    /// </summary>
    internal static class CliLocator
    {
        private static string _cachedPath;

        public static string ExePath
        {
            get
            {
                if (_cachedPath != null) return _cachedPath;
                var asmDir = Path.GetDirectoryName(typeof(CliLocator).Assembly.Location)
                    ?? throw new InvalidOperationException("Cannot determine assembly directory.");
                var candidate = Path.Combine(asmDir, "tools", "cli", "EfmlGen.Cli.exe");
                if (!File.Exists(candidate))
                {
                    throw new FileNotFoundException(
                        $"Bundled EfmlGen.Cli.exe not found at expected path: {candidate}. " +
                        "VSIX may not have been packed correctly — rebuild the extension.",
                        candidate);
                }
                _cachedPath = candidate;
                return _cachedPath;
            }
        }
    }
}
