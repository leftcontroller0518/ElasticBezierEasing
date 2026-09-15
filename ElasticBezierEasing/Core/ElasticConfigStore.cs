using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ElasticBezierEasing.Core;

/// <summary>
/// AnimationType の ID (BaseId + configIndex) と ElasticBezierParameters を対応付けて
/// プラグインフォルダ内の JSON ファイルに永続化するストア。
///
/// YMM4のプロジェクトファイル(.ymmp)は Animation.AnimationType (int) のみを保存する前提のため、
/// 実際のパラメーター値（Amplitude/Oscillation/Damping）はこちら側で管理する。
///
/// 【重要な制約】
/// Configs/elastic_configs.json を削除・変更すると、
/// 既存プロジェクトのエラスティックベジェ設定が読み込めなくなる（＝ID→パラメーター対応が失われる）。
/// プロジェクトファイルを他のPCへ持ち出す場合は、このファイルも一緒にコピーする必要がある。
/// これは IterativeMovementExtension の config.json（BaseId変更で設定が読めなくなる）と同種の制約。
///
/// 【将来的な改善案】
/// ・使われなくなった configIndex を回収するガベージコレクション機能
/// ・プロジェクトファイル(.ymmp)側にパラメーターを直接埋め込む、より確実な永続化方式
///   （YMM4本体のAnimationクラスのシリアライズ実装を確認できれば置き換え可能）
/// </summary>
internal static class ElasticConfigStore
{
    private static readonly string StoreDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
        "Configs");

    private static readonly string StorePath = Path.Combine(StoreDir, "elastic_configs.json");

    private static readonly Dictionary<int, ElasticBezierParameters> _configs = new();
    private static bool _loaded;
    private static readonly object _lock = new();

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            try
            {
                Directory.CreateDirectory(StoreDir);
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, ElasticBezierParameters>>(json);
                    if (data != null)
                    {
                        foreach (var kv in data)
                        {
                            if (int.TryParse(kv.Key, out int idx) && kv.Value != null)
                                _configs[idx] = kv.Value;
                        }
                    }
                }
            }
            catch
            {
                // 読み込み失敗時は空の状態から開始する
            }
            _loaded = true;
        }
    }

    private static void SaveInternal()
    {
        try
        {
            Directory.CreateDirectory(StoreDir);
            var data = _configs.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
            };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(data, options));
        }
        catch { }
    }

    /// <summary>configIndex からパラメーターを取得する。見つからない場合は既定値を返す。</summary>
    public static ElasticBezierParameters Get(int configIndex)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _configs.TryGetValue(configIndex, out var p) ? p : new ElasticBezierParameters();
        }
    }

    /// <summary>
    /// パラメーターに対応する configIndex を取得する。
    ///
    /// 既存の設定と（誤差の範囲で）完全に一致するものがあればそれを再利用し、
    /// 無ければ新しい configIndex を割り当てて保存する。
    ///
    /// 重要: 既存エントリの値を書き換えることは絶対にしない。
    /// 複数の AnimationSlider が dedup により同じ configIndex を共有している可能性があるため、
    /// 値を変更する場合は必ず新しい configIndex を発行し、そちらへ切り替える設計にしている
    /// （そうしないと、片方を編集したら無関係な別の箇所のカーブまで変わってしまう）。
    /// </summary>
    public static int GetOrCreateIndex(ElasticBezierParameters parameters)
    {
        EnsureLoaded();
        lock (_lock)
        {
            foreach (var kv in _configs)
            {
                if (kv.Value.Equals(parameters)) return kv.Key;
            }

            int newIndex = _configs.Count == 0 ? 0 : _configs.Keys.Max() + 1;
            if (newIndex > ElasticEasingConsts.MaxConfigIndex)
            {
                // 上限に達した場合は歯抜け（削除等で空いている）番号を探すフォールバック
                newIndex = Enumerable.Range(0, ElasticEasingConsts.MaxConfigIndex + 1)
                    .First(i => !_configs.ContainsKey(i));
            }

            _configs[newIndex] = parameters.Clone();
            SaveInternal();
            return newIndex;
        }
    }
}
