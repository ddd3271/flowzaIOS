using System.Text.Json;
using FlowzaMobile.Models;

namespace FlowzaMobile.Services;

/// <summary>Android equivalent of Settings.cs - same shape, but %LocalAppData%\settings.json
/// becomes the app's private FileSystem.AppDataDirectory, which is sandboxed per-app on Android
/// the same way ApplicationData is on Windows.</summary>
public class SettingsStore
{
    static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "settings.json");

    public AppSettings Current { get; private set; } = Load();

    static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current)); }
        catch { }
    }
}
