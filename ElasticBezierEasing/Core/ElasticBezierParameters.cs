using System;
using System.Text.Json.Serialization;

namespace ElasticBezierEasing.Core;

/// <summary>
/// エラスティックイージングのモード（方向）。
/// </summary>
public enum ElasticMode
{
    /// <summary>Ease Out: 急加速して目標値を行き過ぎ、減衰しながら収束する（デフォルト）。</summary>
    EaseOut = 0,

    /// <summary>Ease In: 助走をつけて振動してから急加速して到達する。</summary>
    EaseIn = 1,

    /// <summary>Ease In-Out: 助走の振動＋到達時の行き過ぎ減衰の両方を行う。</summary>
    EaseInOut = 2
}

/// <summary>
/// エラスティックベジェ1個分のパラメーター。
///
/// Robert Penner の指数減衰型 Elastic 関数をベースに、
/// Amplitude（強さ）、Oscillation（振動回数）、Damping（減衰率）を自由調整可能にした設計。
///
/// 基本数式:
/// - easeOutElastic:
///     f(x) = A * 2^(-k*x) * sin( (x - s) * 2π / p ) + 1
/// - easeInElastic:
///     f(x) = -A * 2^(k*(x - 1)) * sin( (x - 1 - s) * 2π / p )
/// - easeInOutElastic:
///     x < 0.5 : -0.5 * A * 2^(2k*x - k) * sin( (2x - 1 - sIO) * 2π / pIO )
///     x >= 0.5:  0.5 * A * 2^(-2k*x + k) * sin( (2x - 1 - sIO) * 2π / pIO ) + 1
/// </summary>
public sealed class ElasticBezierParameters : IEquatable<ElasticBezierParameters>
{
    /// <summary>イージングのモード（Out / In / In-Out）。</summary>
    public ElasticMode Mode { get; set; } = ElasticMode.EaseOut;

    /// <summary>
    /// 行き過ぎ／めり込みの強さ（振幅倍率）。標準: 1.0
    /// </summary>
    public double Amplitude { get; set; } = 1.0;

    /// <summary>
    /// 振動回数（周波数）。標準: 10/3 ≈ 3.333 (周期 p = 0.3)
    /// </summary>
    public double Oscillation { get; set; } = 10.0 / 3.0;

    /// <summary>
    /// 減衰の強さ (0.0〜1.0)。標準: 0.50（減衰係数 k = 10.0）
    /// </summary>
    public double Damping { get; set; } = 0.50;

    public ElasticBezierParameters() { }

    public ElasticBezierParameters(double amplitude, double oscillation, double damping, ElasticMode mode = ElasticMode.EaseOut)
    {
        Amplitude = amplitude;
        Oscillation = oscillation;
        Damping = damping;
        Mode = mode;
    }

    public ElasticBezierParameters Clone() => new(Amplitude, Oscillation, Damping, Mode);

    public bool Equals(ElasticBezierParameters? other)
    {
        if (other is null) return false;
        const double eps = 1e-5;
        return Mode == other.Mode
            && Math.Abs(Amplitude - other.Amplitude) < eps
            && Math.Abs(Oscillation - other.Oscillation) < eps
            && Math.Abs(Damping - other.Damping) < eps;
    }

    public override bool Equals(object? obj) => Equals(obj as ElasticBezierParameters);

    public override int GetHashCode()
        => HashCode.Combine((int)Mode, Math.Round(Amplitude, 3), Math.Round(Oscillation, 3), Math.Round(Damping, 3));

    /// <summary>組み込みプリセット（標準数式 A=1.0, Osc=10/3, Damp=0.50 を基準としたバリエーション）。</summary>
    public static class Presets
    {
        public static ElasticBezierParameters Soft   => new(0.70, 2.50, 0.70, ElasticMode.EaseOut);
        public static ElasticBezierParameters Normal => new(1.00, 10.0 / 3.0, 0.50, ElasticMode.EaseOut);
        public static ElasticBezierParameters Strong => new(1.30, 4.50, 0.30, ElasticMode.EaseOut);

        public static ElasticBezierParameters InNormal    => new(1.00, 10.0 / 3.0, 0.50, ElasticMode.EaseIn);
        public static ElasticBezierParameters InOutNormal => new(1.00, 10.0 / 3.0, 0.50, ElasticMode.EaseInOut);
    }
}
