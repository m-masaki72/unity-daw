using System;

namespace UniDAW
{
    /// <summary>
    /// 4 チャンネルのステレオミックス：曲のミキサー設定（音量・パン・ミュート・ソロ）と
    /// エンジン側の上書き（ゲームからのミュート/ソロ）、DC ブロッカー、マスター音量、クリップ、レベル計測。
    /// パンは中央で左右とも 1 倍（バランス型）なので、モノラルに畳むと従来と同じ音量になる。
    /// ディレイ / リバーブへの送りはフェーダー・パン後。エフェクトは送りが初めて使われたときに用意する。
    /// EQ（<see cref="ChipEq"/>）とサイドチェインのダッキングはフェーダーの前のモノラル信号に掛ける。
    /// サイドチェインの検出はトリガー元チャンネルのフェーダー前の音をローパスに通して整流したもの
    /// （ノイズのキックは低域が多くハイハットは少ないので、キックに強く反応する）。
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
        readonly float[] duck = new float[ChipEngine.Channels];
        readonly float[] samples = new float[ChipEngine.Channels];
        readonly ChipEq[] eq = new ChipEq[ChipEngine.Channels];
        readonly bool[] eqOn = new bool[ChipEngine.Channels];
        readonly ChannelMix[] mixRef = new ChannelMix[ChipEngine.Channels];
        /// <summary>エフェクト列（L / M / H / D）で上書きした値。NaN はミキサーの値。</summary>
        readonly float[,] automation = new float[ChipEngine.Channels, 4];
        readonly double sampleRate;
        // サイドチェイン：検出用ローパス・エンベロープ・係数
        const double DetectorHz = 180.0;
        /// <summary>ローパス後の振幅をエンベロープ（1 で最大の深さ）に直す倍率。</summary>
        public const float DetectorGain = 3f;
        const double AttackMs = 4.0;
        readonly float detectCoef, attackCoef;
        float releaseCoef, detectLp, envelope;
        int scSource;
        bool anyDuck;
        ChipEffects fx;
        bool fxOn;
        float peakL, peakR;
        float dcInL, dcOutL, dcInR, dcOutR;
        float master;

        /// <summary>ブロックの始めに呼ぶ：ミキサー設定を確定し、ピークをリセットする。</summary>
        public ChipMixer(double sampleRate)
        {
            this.sampleRate = sampleRate;
            detectCoef = (float)(1.0 - Math.Exp(-2.0 * Math.PI * DetectorHz / sampleRate));
            attackCoef = (float)(1.0 - Math.Exp(-1000.0 / (AttackMs * sampleRate)));
            ResetAutomation();
        }

        /// <summary>オートメーションを外してミキサーの値に戻す（停止・再生開始）。</summary>
        public void ResetAutomation()
        {
            for (int c = 0; c < mixRef.Length; c++)
            {
                for (int p = 0; p < 4; p++) automation[c, p] = float.NaN;
                Resolve(c);
            }
        }

        /// <summary>エフェクト列からの上書き（オーディオスレッド、tick の中で呼ぶ）。</summary>
        public void SetAutomation(int c, MixParam p, float value)
        {
            if (c < 0 || c >= mixRef.Length) return;
            automation[c, (int)p] = value;
            Resolve(c);
        }

        /// <summary>今の値（オートメーション優先、テスト用）。</summary>
        public float Value(int c, MixParam p)
        {
            float a = automation[c, (int)p];
            if (!float.IsNaN(a)) return a;
            var m = mixRef[c];
            return m != null ? m.Get(p) : 0f;
        }

        /// <summary>EQ の係数とダッキングの深さを決め直す。</summary>
        void Resolve(int c)
        {
            float lo = Value(c, MixParam.EqLow), md = Value(c, MixParam.EqMid), hi = Value(c, MixParam.EqHigh);
            if (lo != 0f || md != 0f || hi != 0f || eq[c] != null)
            {
                eq[c] ??= new ChipEq();
                eq[c].Set(lo, md, hi, sampleRate);
            }
            eqOn[c] = eq[c] != null && eq[c].Active;
            duck[c] = c == scSource ? 0f : SongEffects.Clamp01(Value(c, MixParam.Duck));
            anyDuck = false;
            for (int i = 0; i < duck.Length; i++) anyDuck |= duck[i] > 0f;
        }

        /// <summary>今のサイドチェインのエンベロープ（テスト・表示用）。</summary>
        public float Envelope => envelope;

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
                mixRef[c] = m;
            }
            var e = song?.effects;
            scSource = Math.Max(0, Math.Min(gainL.Length - 1, e?.sidechainSource ?? 3));
            for (int c = 0; c < gainL.Length; c++) Resolve(c);
            float relMs = SongEffects.ReleaseMs(e?.duckRelease ?? 0.3f);
            releaseCoef = (float)Math.Exp(-1000.0 / (relMs * sampleRate));
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
            // 無音のチャンネルもボイスは進める（位相を保つ）
            for (int c = 0; c < channels.Length; c++) samples[c] = channels[c].NextSample(sampleRate);
            if (anyDuck)
            {
                detectLp += (samples[scSource] - detectLp) * detectCoef;
                float x = Math.Min(1f, Math.Abs(detectLp) * DetectorGain);
                envelope = x > envelope ? envelope + (x - envelope) * attackCoef : envelope * releaseCoef;
            }
            else envelope = detectLp = 0f;

            for (int c = 0; c < channels.Length; c++)
            {
                float v = samples[c];
                float gl = gainL[c], gr = gainR[c];
                if (gl == 0f && gr == 0f) continue;
                if (eqOn[c]) v = eq[c].Process(v);
                if (duck[c] > 0f) v *= 1f - duck[c] * envelope;
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
