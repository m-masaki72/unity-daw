using System;

namespace UniDAW
{
    /// <summary>
    /// 1 チャンネルのミキサー設定（音量・パン・ミュート・ソロ）。曲と一緒に保存され、
    /// エディタの試聴・ゲーム内再生・WAV 書き出しのすべてに効く。
    /// </summary>
    [Serializable]
    public class ChannelMix
    {
        /// <summary>この値以下は無音（-∞ dB 扱い）。</summary>
        public const float MinDb = -48f;
        public const float MaxDb = 6f;

        /// <summary>フェーダー（dB）。</summary>
        public float volumeDb;
        /// <summary>-1 = 左 … 0 = 中央 … 1 = 右。</summary>
        public float pan;
        public bool mute;
        public bool solo;
        /// <summary>ディレイ / リバーブへの送り量（0..1、フェーダー・パン後）。</summary>
        public float delaySend;
        public float reverbSend;

        public float Send(SendKind kind) => kind == SendKind.Delay ? delaySend : reverbSend;

        public void SetSend(SendKind kind, float v)
        {
            v = SongEffects.Clamp01(v);
            if (kind == SendKind.Delay) delaySend = v;
            else reverbSend = v;
        }

        public float Gain => DbToGain(volumeDb);

        public static float DbToGain(float db) => db <= MinDb ? 0f : (float)Math.Pow(10.0, db / 20.0);

        public static float ClampDb(float db) => Math.Max(MinDb, Math.Min(MaxDb, db));
        public static float ClampPan(float pan) => Math.Max(-1f, Math.Min(1f, pan));

        public ChannelMix Clone() => (ChannelMix)MemberwiseClone();

        /// <summary>表示用："-6.0" / "-∞"。</summary>
        public static string FormatDb(float db) => db <= MinDb ? "-∞" : db.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>表示用："C" / "L32" / "R64"（DAW と同じ 0〜64 の目盛り）。</summary>
        public static string FormatPan(float pan)
        {
            int v = (int)Math.Round(pan * 64);
            return v == 0 ? "C" : (v < 0 ? "L" + -v : "R" + v);
        }
    }
}
