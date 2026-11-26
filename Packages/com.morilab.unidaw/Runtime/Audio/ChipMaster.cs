using System;

namespace UniDAW
{
    /// <summary>
    /// マスターチェーン（<see cref="SongMaster"/>）の処理：EQ → M/S 幅 → コンプ（L/R リンク）→ ゲイン → 先読みリミッター。
    /// リミッターは約 1.5 ms 先読みし、上限を超えるサンプルが出る前に下げ終える（その分だけ出力が遅れる）。オーディオスレッド専用。
    /// </summary>
    internal sealed class ChipMaster
    {
        const double LookaheadMs = 1.5;
        const double CompAttackMs = 10.0, CompReleaseMs = 150.0, LimReleaseMs = 80.0;

        readonly double sampleRate;
        readonly ChipEq eqL = new ChipEq(), eqR = new ChipEq();
        readonly float compAtk, compRel, limRel;
        readonly float[] delayL, delayR, need;
        int pos;
        float compEnv, limGain = 1f;
        bool eqOn, compOn, limOn, active;
        float width, thresholdDb, slope, gain, ceiling;
        float blockReduction;

        public ChipMaster(double sampleRate)
        {
            this.sampleRate = sampleRate;
            compAtk = Coef(CompAttackMs);
            compRel = Coef(CompReleaseMs);
            limRel = Coef(LimReleaseMs);
            int n = Math.Max(1, (int)(LookaheadMs * sampleRate / 1000.0));
            delayL = new float[n];
            delayR = new float[n];
            need = new float[n];
            for (int i = 0; i < n; i++) need[i] = 1f;
        }

        float Coef(double ms) => (float)Math.Exp(-1000.0 / (ms * sampleRate));

        public bool Active => active;

        /// <summary>このブロックで一番深かったゲインリダクション（dB、0 以下）。</summary>
        public float ReductionDb => blockReduction;

        public void Begin(SongMaster m)
        {
            bool was = active;
            active = m != null && m.enabled;
            blockReduction = 0f;
            if (!active)
            {
                if (was) Reset();
                return;
            }
            eqL.Set(m.eqLowDb, m.eqMidDb, m.eqHighDb, sampleRate);
            eqR.Set(m.eqLowDb, m.eqMidDb, m.eqHighDb, sampleRate);
            eqOn = eqL.Active;
            width = Math.Max(0f, Math.Min(2f, m.width));
            thresholdDb = m.compThresholdDb;
            compOn = m.compRatio > 1.01f;
            slope = compOn ? 1f - 1f / m.compRatio : 0f;
            gain = ChannelMix.DbToGain(m.gainDb);
            limOn = m.limiter;
            ceiling = ChannelMix.DbToGain(Math.Min(0f, m.ceilingDb));
        }

        void Reset()
        {
            Array.Clear(delayL, 0, delayL.Length);
            Array.Clear(delayR, 0, delayR.Length);
            for (int i = 0; i < need.Length; i++) need[i] = 1f;
            compEnv = 0f;
            limGain = 1f;
        }

        public void Process(ref float l, ref float r)
        {
            if (eqOn)
            {
                l = eqL.Process(l);
                r = eqR.Process(r);
            }
            if (width != 1f)
            {
                float mid = (l + r) * 0.5f, side = (l - r) * 0.5f * width;
                l = mid + side;
                r = mid - side;
            }
            float g = gain;
            if (compOn)
            {
                float x = Math.Max(Math.Abs(l), Math.Abs(r));
                compEnv = x > compEnv ? x + (compEnv - x) * compAtk : x + (compEnv - x) * compRel;
                if (compEnv > 1e-6f)
                {
                    float over = 20f * (float)Math.Log10(compEnv) - thresholdDb;
                    if (over > 0f)
                    {
                        float red = -over * slope;
                        g *= ChannelMix.DbToGain(red);
                        if (red < blockReduction) blockReduction = red;
                    }
                }
            }
            l *= g;
            r *= g;
            if (!limOn) return;

            // 先読み：入ってきたサンプルに必要なゲインを記録し、窓内の最小値まで即座に下げる
            float peak = Math.Max(Math.Abs(l), Math.Abs(r));
            float nn = peak > ceiling ? ceiling / peak : 1f;
            // 窓 = これから出ていくサンプル（need[pos]）〜いま入ったサンプル
            float target = nn;
            for (int i = 0; i < need.Length; i++) if (need[i] < target) target = need[i];
            need[pos] = nn;
            limGain = target < limGain ? target : target + (limGain - target) * limRel;
            float ol = delayL[pos], or = delayR[pos];
            delayL[pos] = l;
            delayR[pos] = r;
            if (++pos >= need.Length) pos = 0;
            l = Clamp(ol * limGain);
            r = Clamp(or * limGain);
            if (limGain < 1f)
            {
                float db = 20f * (float)Math.Log10(limGain);
                if (db < blockReduction) blockReduction = db;
            }
        }

        float Clamp(float v) => v > ceiling ? ceiling : (v < -ceiling ? -ceiling : v);
    }
}
