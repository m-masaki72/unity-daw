using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>
    /// 再生中のラウドネスの集計（1 系統ぶん）。100 ms ごとの K 特性の平均二乗を受け取り、
    /// モーメンタリー（0.4 秒）・ショートターム（3 秒）・統合（ゲート付き）・サンプルピークと、ショートタームの推移を持つ。
    /// UI スレッドから使う。
    /// </summary>
    public sealed class LoudnessTrack
    {
        public const float HopSeconds = 0.1f;

        readonly List<double> sub = new List<double>();
        readonly List<double> blocks = new List<double>();
        readonly List<float> history = new List<float>();
        float integrated = float.NegativeInfinity;
        bool dirty;

        public float MomentaryLufs { get; private set; } = float.NegativeInfinity;
        public float ShortTermLufs { get; private set; } = float.NegativeInfinity;
        /// <summary>測り始めてからのサンプルピークの最大（dBFS）。</summary>
        public float PeakDb { get; private set; } = float.NegativeInfinity;
        public float Seconds => sub.Count * HopSeconds;
        /// <summary>100 ms ごとのショートターム（3 秒未満の間はそこまでの平均）。</summary>
        public IReadOnlyList<float> History => history;

        public float IntegratedLufs
        {
            get
            {
                if (dirty)
                {
                    integrated = Gate(blocks);
                    dirty = false;
                }
                return integrated;
            }
        }

        /// <summary>直近 <paramref name="seconds"/> 秒の K 特性の平均二乗（ゲートなし。区間が無ければ 0）。</summary>
        public double RecentMeanSquare(float seconds)
        {
            int n = sub.Count;
            if (n == 0) return 0;
            return Mean(n, Math.Max(1, (int)Math.Round(seconds / HopSeconds)));
        }

        public void Clear()
        {
            sub.Clear();
            blocks.Clear();
            history.Clear();
            integrated = MomentaryLufs = ShortTermLufs = PeakDb = float.NegativeInfinity;
            dirty = false;
        }

        /// <summary>100 ms 区間の K 特性の平均二乗（L + R）とピーク（0..1）を足す。</summary>
        public void Add(double meanSquare, float peak)
        {
            sub.Add(meanSquare);
            if (peak > 0f) PeakDb = Math.Max(PeakDb, 20f * (float)Math.Log10(peak));
            int n = sub.Count;
            double m = Mean(n, 4);
            MomentaryLufs = Lufs(m);
            ShortTermLufs = Lufs(Mean(n, 30));
            history.Add(ShortTermLufs);
            if (n >= 4)
            {
                blocks.Add(m);
                dirty = true;
            }
        }

        double Mean(int n, int len)
        {
            int k = Math.Min(n, len);
            double s = 0;
            for (int i = n - k; i < n; i++) s += sub[i];
            return s / k;
        }

        static float Lufs(double meanSquare) => meanSquare > 1e-20 ? -0.691f + 10f * (float)Math.Log10(meanSquare) : float.NegativeInfinity;

        /// <summary>BS.1770 のゲート（-70 LUFS の絶対ゲートと -10 LU の相対ゲート）。</summary>
        static float Gate(List<double> z)
        {
            double sum = 0;
            int cnt = 0;
            foreach (var b in z)
                if (Lufs(b) > -70f) { sum += b; cnt++; }
            if (cnt == 0) return float.NegativeInfinity;
            float rel = Lufs(sum / cnt) - 10f;
            sum = 0;
            cnt = 0;
            foreach (var b in z)
            {
                float l = Lufs(b);
                if (l > -70f && l > rel) { sum += b; cnt++; }
            }
            return cnt > 0 ? Lufs(sum / cnt) : float.NegativeInfinity;
        }
    }

    /// <summary>
    /// 再生中のラウドネスメーター。マスターチェーンの前（<see cref="Pre"/>）と後（<see cref="Post"/>）を同時に測る。
    /// オーディオスレッドが <see cref="Feed"/> で K 特性を掛けて 100 ms ごとに区切り、UI スレッドが <see cref="Poll"/> で集計する。
    /// </summary>
    public sealed class LiveLoudness
    {
        struct Block
        {
            public int generation;
            public double pre, post;
            public float prePeak, postPeak;
        }

        sealed class Tap
        {
            readonly LoudnessMeter.Biquad shL, hpL, shR, hpR;
            public double acc;
            public float peak;

            public Tap(double fs)
            {
                shL = LoudnessMeter.Biquad.KShelf(fs);
                hpL = LoudnessMeter.Biquad.KHighPass(fs);
                shR = LoudnessMeter.Biquad.KShelf(fs);
                hpR = LoudnessMeter.Biquad.KHighPass(fs);
            }

            public void Process(float l, float r)
            {
                double kl = hpL.Process(shL.Process(l)), kr = hpR.Process(shR.Process(r));
                acc += kl * kl + kr * kr;
                float a = Math.Max(Math.Abs(l), Math.Abs(r));
                if (a > peak) peak = a;
            }
        }

        readonly ConcurrentQueue<Block> queue = new ConcurrentQueue<Block>();
        readonly Tap pre, post;
        readonly int hop;
        int n;
        volatile int generation;
        int seen;

        /// <summary>マスターチェーンの前（ミックスそのまま）。UI スレッド用。</summary>
        public readonly LoudnessTrack Pre = new LoudnessTrack();
        /// <summary>マスターチェーンの後（出力）。UI スレッド用。</summary>
        public readonly LoudnessTrack Post = new LoudnessTrack();

        public LiveLoudness(double sampleRate)
        {
            pre = new Tap(sampleRate);
            post = new Tap(sampleRate);
            hop = Math.Max(1, (int)Math.Round(sampleRate * LoudnessTrack.HopSeconds));
        }

        /// <summary>オーディオスレッド：測り直す（再生開始時）。</summary>
        public void Restart()
        {
            n = 0;
            pre.acc = post.acc = 0;
            pre.peak = post.peak = 0f;
            generation++;
        }

        /// <summary>オーディオスレッド：1 サンプルぶん。</summary>
        public void Feed(float preL, float preR, float postL, float postR)
        {
            pre.Process(preL, preR);
            post.Process(postL, postR);
            if (++n < hop) return;
            queue.Enqueue(new Block
            {
                generation = generation,
                pre = pre.acc / hop, post = post.acc / hop,
                prePeak = pre.peak, postPeak = post.peak,
            });
            n = 0;
            pre.acc = post.acc = 0;
            pre.peak = post.peak = 0f;
        }

        /// <summary>UI スレッド：届いた区間を集計する。新しく足されたら true。</summary>
        public bool Poll()
        {
            bool any = false;
            while (queue.TryDequeue(out var b))
            {
                if (b.generation != seen)
                {
                    seen = b.generation;
                    Pre.Clear();
                    Post.Clear();
                }
                Pre.Add(b.pre, b.prePeak);
                Post.Add(b.post, b.postPeak);
                any = true;
            }
            return any;
        }

        /// <summary>A/B の音量差を測る区間（秒）。チェーンを動かしたら、この時間でなじむ。</summary>
        public const float ChainWindowSeconds = 8f;

        /// <summary>
        /// マスターチェーンで変わった音量（後 − 前、直近 <see cref="ChainWindowSeconds"/> 秒の K 特性ラウドネスの差 dB）。
        /// まだ測れていなければ（1 秒未満・ほぼ無音）null。
        /// </summary>
        public float? ChainGainDb
        {
            get
            {
                if (Post.Seconds < 1f) return null;
                double a = Post.RecentMeanSquare(ChainWindowSeconds), b = Pre.RecentMeanSquare(ChainWindowSeconds);
                // -70 LUFS 未満は無音とみなす
                if (b < 1e-7 || a < 1e-7) return null;
                return 10f * (float)Math.Log10(a / b);
            }
        }
    }
}
