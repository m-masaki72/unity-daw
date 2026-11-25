using System;

namespace UniDAW
{
    /// <summary>
    /// 1 チャンネルの 3 バンド EQ（ローシェルフ / ピーキング / ハイシェルフ、RBJ の双二次フィルタ）。
    /// パンの前のモノラル信号に掛ける。0 dB のバンドは素通し。オーディオスレッド専用。
    /// </summary>
    internal sealed class ChipEq
    {
        public const double LowHz = 120.0, MidHz = 1000.0, HighHz = 6000.0;
        const double MidQ = 0.8;

        readonly Biquad low = new Biquad(), mid = new Biquad(), high = new Biquad();
        float lowDb = float.NaN, midDb = float.NaN, highDb = float.NaN;

        /// <summary>いずれかのバンドが 0 dB でないか。</summary>
        public bool Active { get; private set; }

        /// <summary>ブロックの始めに呼ぶ。値が変わったバンドだけ係数を計算し直す（状態は保つ）。</summary>
        public void Set(float lo, float md, float hi, double sampleRate)
        {
            if (lo != lowDb) low.Set(Band.LowShelf, LowHz, 0, lowDb = lo, sampleRate);
            if (md != midDb) mid.Set(Band.Peak, MidHz, MidQ, midDb = md, sampleRate);
            if (hi != highDb) high.Set(Band.HighShelf, HighHz, 0, highDb = hi, sampleRate);
            Active = lo != 0f || md != 0f || hi != 0f;
        }

        public float Process(float x)
        {
            if (lowDb != 0f) x = low.Process(x);
            if (midDb != 0f) x = mid.Process(x);
            if (highDb != 0f) x = high.Process(x);
            return x;
        }

        enum Band { LowShelf, Peak, HighShelf }

        sealed class Biquad
        {
            float b0 = 1f, b1, b2, a1, a2, z1, z2;

            public void Set(Band band, double hz, double q, float db, double fs)
            {
                double A = Math.Pow(10.0, db / 40.0);
                double w = 2.0 * Math.PI * Math.Min(hz, fs * 0.45) / fs;
                double cos = Math.Cos(w), sin = Math.Sin(w);
                double n0, n1, n2, d0, d1, d2;
                if (band == Band.Peak)
                {
                    double alpha = sin / (2.0 * q);
                    n0 = 1 + alpha * A; n1 = -2 * cos; n2 = 1 - alpha * A;
                    d0 = 1 + alpha / A; d1 = -2 * cos; d2 = 1 - alpha / A;
                }
                else
                {
                    double k = 2.0 * Math.Sqrt(A) * (sin / 2.0 * Math.Sqrt(2.0)); // シェルフの傾き S = 1
                    double p = A + 1, m = A - 1;
                    if (band == Band.LowShelf)
                    {
                        n0 = A * (p - m * cos + k); n1 = 2 * A * (m - p * cos); n2 = A * (p - m * cos - k);
                        d0 = p + m * cos + k; d1 = -2 * (m + p * cos); d2 = p + m * cos - k;
                    }
                    else
                    {
                        n0 = A * (p + m * cos + k); n1 = -2 * A * (m + p * cos); n2 = A * (p + m * cos - k);
                        d0 = p - m * cos + k; d1 = 2 * (m - p * cos); d2 = p - m * cos - k;
                    }
                }
                b0 = (float)(n0 / d0); b1 = (float)(n1 / d0); b2 = (float)(n2 / d0);
                a1 = (float)(d1 / d0); a2 = (float)(d2 / d0);
            }

            public float Process(float x)
            {
                // 転置直接形 II
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }
    }
}
