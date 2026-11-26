using System;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>ステレオ音声の測定結果。無音の値は <see cref="float.NegativeInfinity"/>。</summary>
    public struct LoudnessStats
    {
        /// <summary>統合ラウドネス（ITU-R BS.1770 / EBU R128、ゲート付き）。</summary>
        public float integratedLufs;
        /// <summary>ショートターム（3 秒）の最大。</summary>
        public float shortTermMaxLufs;
        /// <summary>モーメンタリー（0.4 秒）の最大。</summary>
        public float momentaryMaxLufs;
        /// <summary>サンプルピーク（dBFS）。</summary>
        public float peakDb;
        /// <summary>帯域ごとのエネルギーの割合（低 &lt; 200 Hz、高 &gt; 4 kHz、残りが中）。</summary>
        public float lowShare, midShare, highShare;
        /// <summary>L/R の相関（1 = モノラル、0 = 無相関、負 = 逆相）。</summary>
        public float correlation;
        public float seconds;

        /// <summary>ピークと平均の差（PLR、dB）。大きいほどダイナミクスがある。</summary>
        public float Plr => peakDb - integratedLufs;
        public bool Silent => float.IsNegativeInfinity(integratedLufs);
    }

    /// <summary>ラウドネスメーター（K 特性・400 ms ブロック・-70 LUFS 絶対ゲート・-10 LU 相対ゲート）と簡易の帯域・相関の測定。</summary>
    public static class LoudnessMeter
    {
        public const float LowSplitHz = 200f, HighSplitHz = 4000f;

        public static string Format(float lufs) =>
            float.IsNegativeInfinity(lufs) || float.IsNaN(lufs) ? "—" : lufs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>interleaved ステレオ（L, R, …）を測る。</summary>
        public static LoudnessStats Measure(float[] stereo, int sampleRate)
        {
            var st = new LoudnessStats
            {
                integratedLufs = float.NegativeInfinity,
                shortTermMaxLufs = float.NegativeInfinity,
                momentaryMaxLufs = float.NegativeInfinity,
                peakDb = float.NegativeInfinity,
            };
            int frames = stereo == null ? 0 : stereo.Length / 2;
            if (frames == 0) return st;
            st.seconds = frames / (float)sampleRate;

            var shL = Biquad.KShelf(sampleRate); var hpL = Biquad.KHighPass(sampleRate);
            var shR = Biquad.KShelf(sampleRate); var hpR = Biquad.KHighPass(sampleRate);
            var lo = Biquad.Pass(false, LowSplitHz, sampleRate);
            var hi = Biquad.Pass(true, HighSplitHz, sampleRate);
            int hop = Math.Max(1, sampleRate / 10);
            var sub = new List<double>(frames / hop + 1);
            double acc = 0, eAll = 0, eLow = 0, eHigh = 0, sLR = 0, sLL = 0, sRR = 0;
            float peak = 0f;
            int n = 0;
            for (int i = 0; i < frames; i++)
            {
                float l = stereo[i * 2], r = stereo[i * 2 + 1];
                float a = Math.Max(Math.Abs(l), Math.Abs(r));
                if (a > peak) peak = a;
                double kl = hpL.Process(shL.Process(l)), kr = hpR.Process(shR.Process(r));
                acc += kl * kl + kr * kr;
                if (++n == hop)
                {
                    sub.Add(acc);
                    acc = 0;
                    n = 0;
                }
                double m = (l + r) * 0.5;
                double ml = lo.Process(m), mh = hi.Process(m);
                eAll += m * m;
                eLow += ml * ml;
                eHigh += mh * mh;
                sLR += l * r;
                sLL += l * l;
                sRR += r * r;
            }
            st.peakDb = peak > 0f ? 20f * (float)Math.Log10(peak) : float.NegativeInfinity;
            if (eAll > 1e-12)
            {
                st.lowShare = (float)Math.Min(1.0, eLow / eAll);
                st.highShare = (float)Math.Min(1.0 - st.lowShare, eHigh / eAll);
                st.midShare = Math.Max(0f, 1f - st.lowShare - st.highShare);
            }
            st.correlation = sLL > 1e-12 && sRR > 1e-12 ? (float)(sLR / Math.Sqrt(sLL * sRR)) : 1f;

            // 400 ms ブロック（100 ms ずつずらす）
            var blocks = Windows(sub, 4, hop);
            foreach (var z in blocks) st.momentaryMaxLufs = Math.Max(st.momentaryMaxLufs, Lufs(z));
            foreach (var z in Windows(sub, 30, hop)) st.shortTermMaxLufs = Math.Max(st.shortTermMaxLufs, Lufs(z));

            // ゲート
            double sum = 0;
            int cnt = 0;
            foreach (var z in blocks)
                if (Lufs(z) > -70f) { sum += z; cnt++; }
            if (cnt > 0)
            {
                float rel = Lufs(sum / cnt) - 10f;
                sum = 0;
                cnt = 0;
                foreach (var z in blocks)
                {
                    float lz = Lufs(z);
                    if (lz > -70f && lz > rel) { sum += z; cnt++; }
                }
                if (cnt > 0) st.integratedLufs = Lufs(sum / cnt);
            }
            if (sub.Count < 30) st.shortTermMaxLufs = st.momentaryMaxLufs;
            return st;
        }

        static float Lufs(double meanSquare) => meanSquare > 1e-20 ? -0.691f + 10f * (float)Math.Log10(meanSquare) : float.NegativeInfinity;

        /// <summary>100 ms の小区間 <paramref name="len"/> 個ぶんの平均二乗を 1 区間ずつずらして並べる。</summary>
        static List<double> Windows(List<double> sub, int len, int hop)
        {
            var w = new List<double>();
            if (sub.Count < len)
            {
                if (sub.Count > 0)
                {
                    double t = 0;
                    foreach (var e in sub) t += e;
                    w.Add(t / (sub.Count * hop));
                }
                return w;
            }
            double s = 0;
            for (int i = 0; i < sub.Count; i++)
            {
                s += sub[i];
                if (i >= len) s -= sub[i - len];
                if (i >= len - 1) w.Add(Math.Max(0, s) / (len * hop));
            }
            return w;
        }

        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, z1, z2;

            Biquad(double n0, double n1, double n2, double d0, double d1, double d2)
            {
                b0 = n0 / d0; b1 = n1 / d0; b2 = n2 / d0; a1 = d1 / d0; a2 = d2 / d0;
            }

            public double Process(double x)
            {
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }

            /// <summary>K 特性の 1 段目（頭部の影響を模したハイシェルフ、+4 dB）。</summary>
            public static Biquad KShelf(double fs)
            {
                double A = Math.Pow(10, 3.99984385397 / 40), w = 2 * Math.PI * 1681.9744509555319 / fs;
                double cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.7071752369554193), sa = 2 * Math.Sqrt(A) * alpha;
                return new Biquad(
                    A * ((A + 1) + (A - 1) * cos + sa), -2 * A * ((A - 1) + (A + 1) * cos), A * ((A + 1) + (A - 1) * cos - sa),
                    (A + 1) - (A - 1) * cos + sa, 2 * ((A - 1) - (A + 1) * cos), (A + 1) - (A - 1) * cos - sa);
            }

            /// <summary>K 特性の 2 段目（RLB ハイパス、約 38 Hz）。</summary>
            public static Biquad KHighPass(double fs) => Pass(true, 38.13547087613982, fs, 0.5003270373253953);

            public static Biquad Pass(bool high, double hz, double fs, double q = 0.7071067811865476)
            {
                double w = 2 * Math.PI * Math.Min(hz, fs * 0.45) / fs, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
                return high
                    ? new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha)
                    : new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
            }
        }
    }
}
