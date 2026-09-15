using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ElasticBezierEasing.Core;

namespace ElasticBezierEasing.UI;

/// <summary>
/// エディタウィンドウで「名前を付けて保存」したユーザープリセット。
/// 1プリセット＝1JSONファイルとして Presets フォルダに保存する。
/// </summary>
internal sealed class UserPreset
{
    public string Name { get; set; } = "新しいプリセット";
    public double Amplitude { get; set; }
    public double Oscillation { get; set; }
    public double Damping { get; set; }
    public ElasticMode Mode { get; set; } = ElasticMode.EaseOut;

    public ElasticBezierParameters ToParameters() => new(Amplitude, Oscillation, Damping, Mode);

    public static UserPreset FromParameters(string name, ElasticBezierParameters p)
        => new() { Name = name, Amplitude = p.Amplitude, Oscillation = p.Oscillation, Damping = p.Damping, Mode = p.Mode };
}

internal static class UserPresetStore
{
    private static readonly string PresetDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
        "Presets");

    public static List<UserPreset> LoadAll()
    {
        var result = new List<UserPreset>();
        try
        {
            Directory.CreateDirectory(PresetDir);
            foreach (var file in Directory.GetFiles(PresetDir, "*.json").OrderBy(f => f))
            {
                try
                {
                    var preset = JsonSerializer.Deserialize<UserPreset>(File.ReadAllText(file));
                    if (preset != null) result.Add(preset);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    public static void Save(UserPreset preset)
    {
        try
        {
            Directory.CreateDirectory(PresetDir);
            var safeName = string.Join("_", preset.Name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "preset";

            var path = Path.Combine(PresetDir, $"{safeName}.json");
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
            };
            File.WriteAllText(path, JsonSerializer.Serialize(preset, options));
        }
        catch { }
    }

    public static void Delete(UserPreset preset)
    {
        try
        {
            Directory.CreateDirectory(PresetDir);
            var safeName = string.Join("_", preset.Name.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(PresetDir, $"{safeName}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
