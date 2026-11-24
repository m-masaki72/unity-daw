namespace UniDAW
{
    /// <summary>
    /// 4 チャンネルのミックス：ミュート/ソロ、チャンネルゲイン、DC ブロッカー、マスター音量、クリップ、レベル計測。
    /// </summary>
    internal sealed class ChipMixer
    {
        const float NoiseGain = 0.8f;
        const float Headroom = 0.25f;
        const float DcPole = 0.995f;

        readonly float[] peak = new float[ChipEngine.Channels];
        float dcIn, dcOut;
        bool anySolo;

        /// <summary>ブロックの始めに呼ぶ（ソロ状態の確定とピークのリセット）。</summary>
        public void BeginBlock(bool[] solo)
        {
            anySolo = false;
            for (int c = 0; c < peak.Length; c++)
            {
                anySolo |= solo[c];
                peak[c] = 0f;
            }
        }

        /// <summary>全チャンネルを 1 サンプル進めてミックスする。</summary>
        public float Mix(ChipChannel[] channels, double sampleRate, bool[] mute, bool[] solo, float master)
        {
            float mix = 0f;
            for (int c = 0; c < channels.Length; c++)
            {
                // ミュート中もボイスは進める（位相を保つ）
                float v = channels[c].NextSample(sampleRate);
                if (mute[c] || (anySolo && !solo[c])) continue;
                if (c == 3) v *= NoiseGain;
                mix += v;
                float a = v < 0 ? -v : v;
                if (a > peak[c]) peak[c] = a;
            }

            // DC ブロッカー（パルスのデューティによる直流分を除去）
            float y = mix - dcIn + DcPole * dcOut;
            dcIn = mix;
            dcOut = y;
            y *= Headroom * master;
            if (y > 1f) y = 1f;
            else if (y < -1f) y = -1f;
            return y;
        }

        /// <summary>ブロックの終わりにチャンネル毎のピークを書き出す。</summary>
        public void EndBlock(float[] levels)
        {
            for (int c = 0; c < peak.Length; c++) levels[c] = peak[c];
        }
    }
}
