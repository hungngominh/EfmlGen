using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfmlGen.Cli;

/// <summary>
/// Reads the profile JSON saved by the WPF UI at
/// %AppData%/EfmlGen/profiles.json (Windows) or ~/.config/EfmlGen/profiles.json (others).
/// Lets CLI commands skip repeating long flags by selecting a profile via --profile NAME.
///
/// Field schema MUST stay in sync with EfmlGen.Wpf.Services.ConnectionProfile.
/// </summary>
internal static class ProfileResolver
{
    private sealed class StoredProfile
    {
        public string Name { get; set; } = "";
        public string Provider { get; set; } = "Postgres";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public string Schemas { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string OutputDir { get; set; } = "";
        public string ContextClass { get; set; } = "";
    }

    private sealed class StoredSettings
    {
        public List<StoredProfile> Profiles { get; set; } = new();
        public string? LastUsedProfileName { get; set; }
    }

    private static string DefaultProfilesPath()
    {
        var dir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EfmlGen")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "EfmlGen");
        return Path.Combine(dir, "profiles.json");
    }

    /// <summary>
    /// If --profile NAME (or --profile-file PATH) is present in <paramref name="opts"/>,
    /// fill any missing scaffold/connection flags from the named profile. Explicit CLI
    /// flags already in <paramref name="opts"/> are preserved (CLI wins).
    /// </summary>
    public static void ApplyProfile(Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("--profile", out var profileName) || string.IsNullOrWhiteSpace(profileName))
            return;

        var path = opts.TryGetValue("--profile-file", out var pf) && !string.IsNullOrWhiteSpace(pf)
            ? pf
            : DefaultProfilesPath();

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"--profile '{profileName}' specified but profiles file not found at '{path}'. " +
                $"Open the WPF UI and Save Profile first, or pass --profile-file <path>.");

        StoredSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Failed to parse profiles file '{path}': {ex.Message}", ex);
        }

        var profile = settings?.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            var available = settings == null || settings.Profiles.Count == 0
                ? "(none saved)"
                : string.Join(", ", settings.Profiles.Select(p => $"'{p.Name}'"));
            throw new InvalidOperationException(
                $"Profile '{profileName}' not found in '{path}'. Available: {available}.");
        }

        // Fill in connection string only if user didn't provide --conn / --conn-env.
        if (!opts.ContainsKey("--conn") && !opts.ContainsKey("--conn-env"))
        {
            var pwd = DecryptPassword(profile.EncryptedPassword);
            if (string.IsNullOrEmpty(pwd) && !string.IsNullOrEmpty(profile.EncryptedPassword))
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.Name}' has an encrypted password that could not be decrypted on this " +
                    $"machine/user (DPAPI is bound to the saving user+machine). Pass --conn-env <ENV_VAR> to " +
                    $"override, or re-save the profile in WPF on this machine.");
            }
            opts["--conn"] = BuildConnectionString(profile, pwd);
        }

        FillIfMissing(opts, "--provider", profile.Provider);
        FillIfMissing(opts, "--schemas", profile.Schemas);
        FillIfMissing(opts, "--name", profile.ModelName);
        FillIfMissing(opts, "--namespace", profile.Namespace);
        FillIfMissing(opts, "--context-class", profile.ContextClass);

        // --out semantics differ per command:
        //   scaffold-efml: file path → OutputDir/ModelName.efml
        //   gen-code:      directory  → OutputDir
        //   db-smoke:      not used
        // Caller (each Run) knows the right shape; we expose both so they can pick.
        FillIfMissing(opts, "--profile-output-dir", profile.OutputDir);
        FillIfMissing(opts, "--profile-model-name", profile.ModelName);
    }

    private static void FillIfMissing(Dictionary<string, string> opts, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (opts.ContainsKey(key)) return;
        opts[key] = value;
    }

    private static string DecryptPassword(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }

    private static string BuildConnectionString(StoredProfile p, string password) =>
        p.Provider switch
        {
            "Postgres" => $"Host={p.Host};Port={p.Port};Username={p.Username};Password={password};Database={p.Database}",
            "SqlServer" => $"Server={p.Host},{p.Port};Database={p.Database};User Id={p.Username};Password={password};TrustServerCertificate=true",
            _ => throw new NotSupportedException($"Profile '{p.Name}' has unknown Provider '{p.Provider}'. Expected 'Postgres' or 'SqlServer'.")
        };
}
