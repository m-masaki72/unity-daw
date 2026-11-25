using System;

namespace UniDAW
{
    /// <summary>センドエフェクトの種類（チャンネルからの送り先）。</summary>
    public enum SendKind { Delay = 0, Reverb = 1 }

    /// <summary>エフェクトの数値パラメータ（ミキサーのつまみ 1 本ずつ）。</summary>
    public enum FxParam { DelayFeedback = 0, DelayReturn = 1, ReverbSize = 2, ReverbDamp = 3, ReverbReturn = 4, DuckRelease = 5 }

    /// <summary>
    /// 曲全体のセンドエフェクト（テンポ同期ディレイとリバーブ）とサイドチェインのトリガー設定。各チャンネルの送り量は <see cref="ChannelMix"/> に持つ。
    /// 試聴・ゲーム内再生・WAV 書き出しのすべてに効く。
    /// </summary>
    [Serializable]
    public class SongEffects
    {
        public const float MaxFeedback = 0.9f;

        /// <summary>選べるディレイタイム（拍）と表示名。</summary>
        public static readonly float[] DelayBeatChoices = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f };
        public static readonly string[] DelayBeatNames = { "1/16", "1/8", "1/8.", "1/4", "1/4.", "1/2" };
        public const float MaxDelayBeats = 2f;

        /// <summary>ディレイタイム（拍）。</summary>
        public float delayBeats = 0.75f;
        public float delayFeedback = 0.35f;
        /// <summary>左右に交互に跳ねる。</summary>
        public bool delayPingPong = true;
        /// <summary>ディレイのリターン音量（dB）。</summary>
        public float delayReturnDb;
        /// <summary>残響の長さ（0..1）。</summary>
        public float reverbSize = 0.6f;
        /// <summary>高域の減衰（0..1）。</summary>
        public float reverbDamp = 0.4f;
        public float reverbReturnDb;
        /// <summary>サイドチェインのトリガー元チャンネル（既定 Noise）。フェーダー前の音で検出するので、ミュートしても効く。</summary>
        public int sidechainSource = 3;
        /// <summary>ダッキングから戻る速さ（0..1 → <see cref="ReleaseMs"/>）。</summary>
        public float duckRelease = 0.3f;

        /// <summary>リリース（0..1）をミリ秒に。</summary>
        public static float ReleaseMs(float v) => 30f + 570f * Clamp01(v);

        public SongEffects Clone() => (SongEffects)MemberwiseClone();

        public static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));

        public static (float min, float max, float def) Range(FxParam p)
        {
            switch (p)
            {
                case FxParam.DelayFeedback: return (0f, MaxFeedback, 0.35f);
                case FxParam.ReverbSize: return (0f, 1f, 0.6f);
                case FxParam.ReverbDamp: return (0f, 1f, 0.4f);
                case FxParam.DuckRelease: return (0f, 1f, 0.3f);
                default: return (ChannelMix.MinDb, ChannelMix.MaxDb, 0f);
            }
        }

        public float Get(FxParam p)
        {
            switch (p)
            {
                case FxParam.DelayFeedback: return delayFeedback;
                case FxParam.DelayReturn: return delayReturnDb;
                case FxParam.ReverbSize: return reverbSize;
                case FxParam.ReverbDamp: return reverbDamp;
                case FxParam.DuckRelease: return duckRelease;
                default: return reverbReturnDb;
            }
        }

        public void Set(FxParam p, float v)
        {
            var (min, max, def) = Range(p);
            v = float.IsNaN(v) ? def : Math.Max(min, Math.Min(max, v));
            switch (p)
            {
                case FxParam.DelayFeedback: delayFeedback = v; break;
                case FxParam.DelayReturn: delayReturnDb = v; break;
                case FxParam.ReverbSize: reverbSize = v; break;
                case FxParam.ReverbDamp: reverbDamp = v; break;
                case FxParam.DuckRelease: duckRelease = v; break;
                default: reverbReturnDb = v; break;
            }
        }

        /// <summary>ディレイタイムに一番近い選択肢の番号。</summary>
        public int DelayChoice
        {
            get
            {
                int best = 0;
                for (int i = 1; i < DelayBeatChoices.Length; i++)
                    if (Math.Abs(DelayBeatChoices[i] - delayBeats) < Math.Abs(DelayBeatChoices[best] - delayBeats)) best = i;
                return best;
            }
        }

        /// <summary>範囲外・不正な値を直す。</summary>
        public void Validate()
        {
            delayBeats = float.IsNaN(delayBeats) ? 0.75f : Math.Max(1f / 16f, Math.Min(MaxDelayBeats, delayBeats));
            foreach (FxParam p in Enum.GetValues(typeof(FxParam))) Set(p, Get(p));
            sidechainSource = Math.Max(0, Math.Min(Song.ChannelCount - 1, sidechainSource));
        }
    }
}
