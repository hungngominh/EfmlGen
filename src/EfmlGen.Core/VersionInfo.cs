using System;
using System.Reflection;

namespace EfmlGen.Core;

/// <summary>
/// Resolves the running tool's version (e.g. "0.4.4") from the entry assembly.
/// Used to stamp generated file headers and to drive the update check.
/// </summary>
public static class VersionInfo
{
    /// <summary>
    /// Version of the host process (CLI or WPF exe). Falls back to this assembly,
    /// then "0.0.0" if no version metadata is available.
    /// </summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly;

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // Strip SourceLink build metadata, e.g. "0.4.4+abc123".
            var plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }

        var v = asm.GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    }
}
