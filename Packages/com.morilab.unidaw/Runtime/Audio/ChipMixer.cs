using System;

namespace UniDAW
{
    /// <summary>
    /// 4 チャンネルのステレオミックス：曲のミキサー設定（音量・パン・ミュート・ソロ）と
    /// エンジン側の上書き（ゲームからのミュート/ソロ）、DC ブロッカー、マスター音量、クリップ、レベル計測。
    /// パンは中央で左右とも 1 倍（バランス型）なので、モノラルに畳むと従来と同じ音量になる。
    /// ディレイ / リバーブへの送りはフェーダー・パン後。エフェクトは送りが初めて使われたときに用意する。
    /// </summary>
    internal sealed class ChipMixer
    {
        const float NoiseGain = 0.8f;
        const float Headroom = 0.25f;
        const float DcPole = 0.995f;

        readonly float[] gainL = new float[ChipEngine.Channels];
        readonly float[] gainR = new float[ChipEngine.Channels];
        readonly float[] peak = new float[ChipEngine.Channels];
        readonly float[] sendD = new float[ChipEngine.Channels];
        readonly float[] sendR = new float[ChipEngine.Channels];
        readonly double sampleRate;
        ChipEffects fx;
        bool fxOn;
        float peakL, peakR;
        float dcInL, dcOutL, dcInR, dcOutR;
        float master;

        /// <summary>ブロックの始めに呼ぶ：ミキサー設定を確定し、ピークをリセットする。</summary>
        public ChipMixer(double sampleRate) => this.sampleRate = sampleRate;

        public void BeginBlock(Song song, bool[] mute, bool[] solo, float masterVolume, float bpm, int frames)
        {
            bool anySend = false;
            bool anySolo = false;
            for (int c = 0; c < gainL.Length; c++)
                anySolo |= solo[c] || (song?.Mix(c)?.solo ?? false);

            for (int c = 0; c < gainL.Length; c++)
            {
                var m = song?.Mix(c);
                bool soloed = solo[c] || (m?.solo ?? false);
                bool silent = mute[c] || (m?.mute ?? false) || (anySolo && !soloed);
                float g = silent ? 0f : (m?.Gain ?? 1f);
                if (c == 3) g *= NoiseGain;
                float pan = m != null ? ChannelMix.ClampPan(m.pan) : 0f;
                gainL[c] = g * Math.Min(1f, 1f - pan);
                gainR[c] = g * Math.Min(1f, 1f + pan);
                sendD[c] = g > 0f ? (m?.delaySend ?? 0f) : 0f;
                sendR[c] = g > 0f ? (m?.reverbSend ?? 0f) : 0f;
                anySend |= sendD[c] > 0f || sendR[c] > 0f;
                peak[c] = 0f;
            }
            if (anySend && fx == null) fx = new ChipEffects(sampleRate);
            fx?.Begin(song?.effects, bpm, anySend, frames);
            fxOn = fx != null && fx.Active;
            peakL = peakR = 0f;
            master = masterVolume * (song != null ? ChannelMix.DbToGain(song.masterDb) : 1f);
        }

        /// <summary>全チャンネルを 1 サンプル進めてミックスする。</summary>
        public void Mix(ChipChannel[] channels, double sampleRate, out float left, out float right)
        {
            float l = 0f, r = 0f, dl = 0f, dr = 0f, rl = 0f, rr = 0f;
            for (int c = 0; c < channels.Length; c++)
            {
                // 無音のチャンネルもボイスは進める（位相を保つ）
                float v = channels[c].NextSample(sampleRate);
                float gl = gainL[c], gr = gainR[c];
                if (gl == 0f && gr == 0f) continue;
                float vl = v * gl, vr = v * gr;
                l += vl;
                r += vr;
                dl += vl * sendD[c];
                dr += vr * sendD[c];
                rl += vl * sendR[c];
                rr += vr * sendR[c];
                float a = Math.Abs(v) * Math.Max(gl, gr);
                if (a > peak[c]) peak[c] = a;
            }

            if (fxOn)
            {
                fx.Process(dl, dr, rl, rr, out float wl, out float wr);
                l += wl;
                r += wr;
            }

            // DC ブロッカー（パルスのデューティによる直流分を除去）
            float yl = l - dcInL + DcPole * dcOutL;
            dcInL = l;
            dcOutL = yl;
            float yr = r - dcInR + DcPole * dcOutR;
            dcInR = r;
            dcOutR = yr;
            left = Clip(yl * Headroom * master);
            right = Clip(yr * Headroom * master);
            float al = Math.Abs(left), ar = Math.Abs(right);
            if (al > peakL) peakL = al;
            if (ar > peakR) peakR = ar;
        }

        static float Clip(float y) => y > 1f ? 1f : (y < -1f ? -1f : y);

        /// <summary>ブロックの終わりにチャンネル毎（フェーダー後）と出力 L/R のピークを書き出す。</summary>
        public void EndBlock(float[] levels, float[] output)
        {
            for (int c = 0; c < peak.Length; c++) levels[c] = peak[c];
            output[0] = peakL;
            output[1] = peakR;
        }
    }
}
