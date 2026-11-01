using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveryStore;

/// <summary>User settings, stored as JSON in %AppData%\EveryStore\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"system", "dark" or "light".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Look for new versions on launch and every <see cref="CheckHours"/> hours while the store is open.</summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>Install updates as they are found (apps that are open are skipped until they are closed).</summary>
    public bool AutoUpdate { get; set; }

    public int CheckHours { get; set; } = 6;

    /// <summary>The GitHub token as Windows DPAPI ciphertext (base64), readable only by this Windows user. Use <see cref="Token"/>.</summary>
    public string? ProtectedToken { get; set; }

    /// <summary>
    /// Optional personal access token. Anonymous GitHub access allows 60 requests an hour; a token raises that to
    /// 5000 and lets the store post bug reports and feedback straight to the app's issues.
    /// </summary>
    [JsonIgnore]
    public string? Token
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedToken)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        set
        {
            ProtectedToken = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EveryStore");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch { /* a corrupt file falls back to defaults and is rewritten on the next save */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}
