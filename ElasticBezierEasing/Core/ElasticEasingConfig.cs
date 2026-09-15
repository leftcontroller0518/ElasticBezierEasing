using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElasticBezierEasing.Core;

/// <summary>
/// プラグインフォルダ内の config.json から BaseId を読み込む。
///
/// config.json の場所:
///   YMM4\user\plugin\ElasticBezierEasing\config.json
///
/// 初回起動時に自動生成される。
/// 他プラグイン（IterativeMovementExtension 等）と AnimationType の ID が衝突する場合は
/// この config.json の BaseId を書き換えて YMM4 を再起動すること。
/// </summary>
internal static class ElasticEasingConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
        "config.json");

    // IterativeMovementExtension の既定値(1,200,000系)と衝突しない値をデフォルトにしてある。
    public static int BaseId { get; private set; } = 1_300_000;

    /// <summary>AddEasingPlugin.Initialize() の先頭で呼び出す。</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var data = JsonSerializer.Deserialize<ConfigData>(File.ReadAllText(ConfigPath));
                if (data != null && data.BaseId > 0)
                    BaseId = data.BaseId;
            }
            else
            {
                Save();
            }
        }
        catch
        {
            // 読み込み失敗時はデフォルト値のまま続行
        }
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new ConfigData { BaseId = BaseId },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    private sealed class ConfigData
    {
        [JsonPropertyName("BaseId")]
        public int BaseId { get; set; } = 1_300_000;
    }
}
