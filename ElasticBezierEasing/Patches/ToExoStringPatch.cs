using ElasticBezierEasing.Core;
using HarmonyLib;
using YukkuriMovieMaker.Commons;

namespace ElasticBezierEasing.Patches;

/// <summary>
/// AnimationTypeEx.ToExoString をパッチして、
/// 本プラグイン独自のIDが AviUtl (.exo) 書き出し時に不正な文字列になるのを防ぐ。
///
/// 拡張メソッド（static）のため [HarmonyPatch] 属性方式ではなく、
/// AddEasingPlugin.Initialize() から手動で Apply() を呼び出す
/// （IterativeMovementExtension と同じ理由・同じ対処）。
///
/// 現状は「直線移動」にフォールバックするだけ。
/// TODO: 将来的にはサンプリングしたカーブを3次ベジエで近似し、
///       AviUtl側でも似た動きになるように書き出す改善が考えられる。
/// </summary>
internal static class ToExoStringPatch
{
    internal static void Apply(Harmony harmony)
    {
        var original = AccessTools.Method(typeof(AnimationTypeEx), "ToExoString");
        if (original == null) return;

        var prefix = new HarmonyMethod(typeof(ToExoStringPatch), nameof(Prefix));
        harmony.Patch(original, prefix: prefix);
    }

    private static bool Prefix(AnimationType animationType, ref string __result)
    {
        if (ElasticEasingConsts.IsElasticEasing((int)animationType))
        {
            __result = "1"; // 直線移動としてフォールバック
            return false;
        }
        return true;
    }
}
