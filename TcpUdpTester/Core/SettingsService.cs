using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TcpUdpTester.Core;

public static class SettingsService
{
    public static string? ProfileName { get; set; }

    private static string GetPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(ProfileName)
            ? Path.Combine(appData, "NetTestConsole", "settings.json")
            : Path.Combine(appData, "NetTestConsole", "profiles", ProfileName, "settings.json");
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
            }
        }
        catch { /* 読み込み失敗時はデフォルト値を使用 */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, _options));
        }
        catch { /* 書き込み失敗は無視 */ }
    }
}
