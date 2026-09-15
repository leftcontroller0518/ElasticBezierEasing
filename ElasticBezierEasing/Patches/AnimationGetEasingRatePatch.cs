using System.Reflection;
using ElasticBezierEasing.Core;
using HarmonyLib;
using YukkuriMovieMaker.Commons;

namespace ElasticBezierEasing.Patches;

/// <summary>
/// Animation.GetEasingRate(double rate) をパッチし、
/// AnimationType が本プラグインのID範囲のとき、エラスティックベジェの計算結果を返す。
///
/// 標準ベジェ(ID:100000/100001)と同様、Span や length/videoFPS を使わず、
/// 0〜1に正規化された rate をそのまま f(t) に渡すだけでよい想定
/// （周期運動ではなく、単純な「2点間の補間イージング」のため）。
/// </summary>
[HarmonyPatch]
internal class AnimationGetEasingRatePatch
{
    private static MethodBase? TargetMethod()
        => typeof(Animation).GetMethod("GetEasingRate", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool Prefix(Animation __instance, double rate, ref double __result)
    {
        try
        {
            if (__instance == null) return true;

            int typeId = (int)__instance.AnimationType;
            if (!ElasticEasingConsts.IsElasticEasing(typeId)) return true;

            var parameters = ElasticConfigStore.Get(ElasticEasingConsts.DecodeConfigIndex(typeId));
            __result = ElasticEasingConsts.CalculateEasingRate(rate, parameters);
            return false; // 元のGetEasingRateは実行させない
        }
        catch
        {
            return true; // 想定外のエラー時は元の処理にフォールバックする
        }
    }
}
