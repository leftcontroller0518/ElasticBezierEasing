using System;
using System.Reflection;
using System.Windows;
using ElasticBezierEasing.Patches;
using HarmonyLib;
using YukkuriMovieMaker.Plugin;

namespace ElasticBezierEasing.Core;

/// <summary>
/// プラグインのエントリポイント。
/// YMM4 はアセンブリ内の IPlugin 実装をインスタンス化することでプラグインをロードする
/// （IterativeMovementExtension と同じ方式）。
/// これは映像エフェクトプラグインではなく、Harmonyで既存のイージング選択UIを拡張する
/// 「ツールプラグイン」である。
/// </summary>
public class AddEasingPlugin : IPlugin
{
    private static bool _initialized;

    static AddEasingPlugin()
    {
        if (_initialized) return;
        Initialize();
        _initialized = true;
    }

    public string Name => "エラスティックベジェイージング";

    private static void Initialize()
    {
        try
        {
            ElasticEasingConfig.Load();

            // TODO: 他プラグインとのHarmony IDの衝突を避けるため、適宜書き換えてください。
            var harmony = new Harmony("com.example.elasticbeziereasing");

            // [HarmonyPatch] 属性を持つクラスを一括パッチ
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // 拡張メソッド（AnimationTypeEx.ToExoString）は属性方式だと正しく動かないため手動適用
            ToExoStringPatch.Apply(harmony);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"初期化エラー: {ex.Message}\n\n{ex.StackTrace}",
                "ElasticBezierEasing Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
