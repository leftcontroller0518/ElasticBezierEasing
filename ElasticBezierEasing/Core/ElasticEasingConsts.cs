using System;

namespace ElasticBezierEasing.Core;

/// <summary>
/// AnimationType への ID 割り当てルールと、指数減衰型エラスティックのイージング計算式。
///
/// 基準となる標準数式（Robert Penner / easings.net 準拠）:
///
///   easeOutElastic:
///     f(x) = 2^(-10x) · sin((10x - 0.75) · 2π/3) + 1
///
///   easeInElastic:
///     f(x) = -2^(10x - 10) · sin((10x - 10.75) · 2π/3)
///
///   easeInOutElastic:
///     x < 0.5 : -(2^(20x - 10) · sin((20x - 11.125) · 2π/4.5)) / 2
///     x ≥ 0.5 :  (2^(-20x + 10) · sin((20x - 11.125) · 2π/4.5)) / 2 + 1
/// </summary>
internal static class ElasticEasingConsts
{
    public static int BaseId => ElasticEasingConfig.BaseId;

    /// <summary>1プラグインあたり最大100万パターンまで割り当て可能。</summary>
    public static int MaxConfigIndex => 999_999;

    public static int MaxId => BaseId + MaxConfigIndex;

    public static bool IsElasticEasing(int typeId) => typeId >= BaseId && typeId <= MaxId;

    public static int EncodeId(int configIndex) => BaseId + Math.Clamp(configIndex, 0, MaxConfigIndex);

    public static int DecodeConfigIndex(int typeId) => typeId - BaseId;

    /// <summary>
    /// エラスティックイージング関数。
    ///
    /// 振幅(Amplitude)、周期(Oscillation)、減衰(Damping)を歪みなく反映し、
    /// 端点(t=0, 0.5, 1)でのカクつきや不自然な逆走・停滞のない滑らかな動作を保証します。
    /// </summary>
    public static double CalculateEasingRate(double t, ElasticBezierParameters p)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;

        switch (p.Mode)
        {
            case ElasticMode.EaseIn:
                return 1.0 - CalculateEaseOut(1.0 - t, p.Amplitude, p.Oscillation, p.Damping);

            case ElasticMode.EaseInOut:
            {
                double oscIO = p.Oscillation / 1.5;
                if (t < 0.5)
                {
                    return 0.5 * (1.0 - CalculateEaseOut(1.0 - 2.0 * t, p.Amplitude, oscIO, p.Damping));
                }
                else
                {
                    return 0.5 + 0.5 * CalculateEaseOut(2.0 * t - 1.0, p.Amplitude, oscIO, p.Damping);
                }
            }

            case ElasticMode.EaseOut:
            default:
                return CalculateEaseOut(t, p.Amplitude, p.Oscillation, p.Damping);
        }
    }

    /// <summary>
    /// 基本となる EaseOutElastic の計算。
    /// Robert Penner 原典に準拠しつつ、a &lt; 1 のソフト振動にも完全対応。
    /// </summary>
    private static double CalculateEaseOut(double t, double a, double osc, double damping)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;

        double decay = 2.0 + damping * 16.0;
        double period = 1.0 / Math.Max(0.01, osc);

        if (a >= 1.0)
        {
            // Penner 標準: a >= 1 のとき位相を asin(1/a) に連動させて t=0 で厳密に 0 を保証
            double s = period / (2.0 * Math.PI) * Math.Asin(1.0 / a);
            return a * Math.Pow(2.0, -decay * t) * Math.Sin((t - s) * (2.0 * Math.PI) / period) + 1.0;
        }
        else
        {
            // a < 1 (Soft振動): 標準 Penner 振動 (a=1) を基準に、
            // 立ち上がりのなめらかさを保ちつつオーバーシュート・振動幅を a 倍に綺麗にスケール
            double s = period / 4.0;
            double std = Math.Pow(2.0, -decay * t) * Math.Sin((t - s) * (2.0 * Math.PI) / period) + 1.0;

            if (t >= s)
            {
                return 1.0 + (std - 1.0) * a;
            }
            else
            {
                double blend = Math.Pow(t / s, 2.0) * (a - 1.0) + 1.0;
                return 1.0 + (std - 1.0) * blend;
            }
        }
    }

    /// <summary>イージング一覧UIで表示する短縮ラベル。</summary>
    public static string GetShortLabel() => "弾";

    public static string GetShortLabel(ElasticBezierParameters p) => "弾";

    public static string GetTooltip(ElasticBezierParameters p)
        => $"エラスティックベジェ ({p.Mode})\n強さ(Amp): {p.Amplitude:F2}\n周期/回数(Osc): {p.Oscillation:F2}\n減衰(Damp): {p.Damping:F2}";
}
