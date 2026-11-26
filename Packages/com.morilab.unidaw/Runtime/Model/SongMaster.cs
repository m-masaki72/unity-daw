using System;

namespace UniDAW
{
    /// <summary>マスターチェーンの数値パラメータ（つまみ 1 本ずつ）。</summary>
    public enum MasterParam { EqLow = 0, EqMid = 1, EqHigh = 2, Width = 3, Threshold = 4, Ratio = 5, Gain = 6, Ceiling = 7 }

    /// <summary>
    /// 曲全体のマスターチェーン：EQ → ステレオ幅 → コンプレッサー → ゲイン → リミッター。
    /// マスター音量（<see cref="Song.masterDb"/>）の後に掛かり、試聴・ゲーム内再生・WAV 書き出しのすべてに効く。
    /// <see cref="enabled"/> が false なら素通し（従来どおり）。
    /// </summary>
    [Serializable]
    public class SongMaster
    {
        public const int ParamCount = 8;

        public bool enabled;
        public float eqLowDb, eqMidDb, eqHighDb;
        /// <summary>ステレオ幅（0 = モノラル、1 = そのまま、2 = 広げる）。</summary>
        public float width = 1f;
        /// <summary>コンプのスレッショルド（dBFS）。</summary>
        public float compThresholdDb = -12f;
        /// <summary>コンプのレシオ（1 = 掛けない）。</summary>
        public float compRatio = 1f;
        /// <summary>リミッター前のゲイン（dB）。ラウドネス合わせで動かす。</summary>
        public float gainDb;
        public bool limiter = true;
        /// <summary>リミッターの上限（dBFS）。</summary>
        public float ceilingDb = -1f;
        /// <summary>目標ラウドネス（LUFS）。解析とラウドネス合わせで使う。</summary>
        public float targetLufs = -14f;

        public static readonly float[] TargetChoices = { -14f, -16f, -11f, -9f, -23f };
        public static readonly string[] TargetNames = { "-14 LUFS（配信）", "-16 LUFS（控えめ）", "-11 LUFS（ゲーム BGM）", "-9 LUFS（大きめ）", "-23 LUFS（放送）" };

        public SongMaster Clone() => (SongMaster)MemberwiseClone();

        public static (float min, float max, float def) Range(MasterParam p)
        {
            switch (p)
            {
                case MasterParam.Width: return (0f, 2f, 1f);
                case MasterParam.Threshold: return (-30f, 0f, -12f);
                case MasterParam.Ratio: return (1f, 10f, 1f);
                case MasterParam.Gain: return (-12f, 12f, 0f);
                case MasterParam.Ceiling: return (-6f, 0f, -1f);
                default: return (-12f, 12f, 0f);
            }
        }

        public float Get(MasterParam p)
        {
            switch (p)
            {
                case MasterParam.EqLow: return eqLowDb;
                case MasterParam.EqMid: return eqMidDb;
                case MasterParam.EqHigh: return eqHighDb;
                case MasterParam.Width: return width;
                case MasterParam.Threshold: return compThresholdDb;
                case MasterParam.Ratio: return compRatio;
                case MasterParam.Gain: return gainDb;
                default: return ceilingDb;
            }
        }

        public void Set(MasterParam p, float v)
        {
            var (min, max, def) = Range(p);
            v = float.IsNaN(v) ? def : Math.Max(min, Math.Min(max, v));
            switch (p)
            {
                case MasterParam.EqLow: eqLowDb = v; break;
                case MasterParam.EqMid: eqMidDb = v; break;
                case MasterParam.EqHigh: eqHighDb = v; break;
                case MasterParam.Width: width = v; break;
                case MasterParam.Threshold: compThresholdDb = v; break;
                case MasterParam.Ratio: compRatio = v; break;
                case MasterParam.Gain: gainDb = v; break;
                default: ceilingDb = v; break;
            }
        }

        public static float ClampTarget(float v) => float.IsNaN(v) ? -14f : Math.Max(-30f, Math.Min(-6f, v));

        public void Validate()
        {
            for (int i = 0; i < ParamCount; i++) Set((MasterParam)i, Get((MasterParam)i));
            targetLufs = ClampTarget(targetLufs);
        }

        public static string FormatParam(MasterParam p, float v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (p)
            {
                case MasterParam.Width: return Math.Round(v * 100) + "%";
                case MasterParam.Ratio: return v <= 1.01f ? "off" : v.ToString("0.#", inv) + ":1";
                case MasterParam.Threshold:
                case MasterParam.Ceiling: return v.ToString("0.#", inv) + "dB";
                default: return v.ToString("+0.#;-0.#;0", inv) + "dB";
            }
        }
    }
}
