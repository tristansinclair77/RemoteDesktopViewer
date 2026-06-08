using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RDV.Host;

public class Config
{
    public string PasswordHash { get; set; } = "";
    public int Port { get; set; } = 8765;
    public string? DuckDnsToken { get; set; }
    public string? DuckDnsSubdomain { get; set; }

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RDV", "config.json");

    public bool IsFirstRun => string.IsNullOrEmpty(PasswordHash);

    public static Config Load()
    {
        if (!File.Exists(ConfigPath))
            return new Config();
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch { return new Config(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SetPassword(string password)
    {
        PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    // Auth: viewer sends SHA256(storedHash + nonce) — password never travels in plain text
    public bool VerifyResponse(string nonce, string response)
    {
        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(PasswordHash + nonce)));
        return string.Equals(expected, response, StringComparison.OrdinalIgnoreCase);
    }
}
