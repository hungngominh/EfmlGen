using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EfmlGen.Wpf.Services;

public sealed class ConnectionProfile
{
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "Postgres";  // "Postgres" | "SqlServer"
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    /// <summary>Base64 of DPAPI-encrypted password. Encrypt per-user, current machine.</summary>
    public string EncryptedPassword { get; set; } = "";

    // Last-used scaffold options for this profile
    public string Schemas { get; set; } = "dbo";
    public string ModelName { get; set; } = "Entities";
    public string Namespace { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string ContextClass { get; set; } = "";
}

public sealed class AppSettings
{
    public List<ConnectionProfile> Profiles { get; set; } = new();
    public string? LastUsedProfileName { get; set; }
}

public static class ProfileStore
{
    private static readonly string DirPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EfmlGen");

    private static readonly string FilePath = Path.Combine(DirPath, "profiles.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Could not load profiles from {FilePath} ({ex.GetType().Name}: {ex.Message}). Starting with empty profile list — your existing file will be overwritten on next Save.");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirPath);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static string EncryptPassword(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string DecryptPassword(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Could not decrypt saved password ({ex.GetType().Name}: {ex.Message}). DPAPI ties encryption to the current Windows user on the current machine — passwords don't transfer across users/machines. Re-enter the password and Save Profile.");
            return "";
        }
    }

    public static string BuildConnectionString(ConnectionProfile p, string password)
    {
        return p.Provider switch
        {
            "Postgres" => $"Host={p.Host};Port={p.Port};Username={p.Username};Password={password};Database={p.Database}",
            "SqlServer" => $"Server={p.Host},{p.Port};Database={p.Database};User Id={p.Username};Password={password};TrustServerCertificate=true",
            _ => throw new NotSupportedException($"Unknown provider: {p.Provider}")
        };
    }
}
