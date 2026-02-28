using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TcpUdpTester.Core;

public static class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetTestConsole", "settings.json");

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, _options));
        }
        catch { /* 書き込み失敗は無視 */ }
    }
}
