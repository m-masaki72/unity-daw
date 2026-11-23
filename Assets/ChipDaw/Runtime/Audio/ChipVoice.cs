using System;

namespace ChipDaw
{
    /// <summary>
    /// 1チャンネル分の発振器（2A03 風）。値型・アロケーションなしで Burst 移植しやすい形にしてある。
    /// </summary>
    public struct ChipVoice
    {
        public const double NesCpuClock = 1789773.0;
        static readonly double[] DutyTable = { 0.125, 0.25, 0.5, 0.75 };
        /// <summary>NTSC ノイズ周期テーブル（index 0 が最も高い）</summary>
        public static readonly int[] NoisePeriods = { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };

        public ChannelKind kind;
        public bool gate;
        public float volume;      // 0..1
        public double freq;       // Hz (Pulse/Triangle)
        public int duty;          // 0..3
        public double noiseRate;  // LFSR クロック Hz
        public bool shortMode;

        double phase;
        uint lfsr;
        double noiseAcc;

        public static ChipVoice Create(ChannelKind kind) => new ChipVoice { kind = kind, lfsr = 1, duty = 2 };

        public float Next(double sampleRate)
        {
            if (!gate || volume <= 0f) return 0f;
            switch (kind)
            {
                case ChannelKind.Pulse: return NextPulse(sampleRate) * volume;
                case ChannelKind.Triangle: return NextTriangle(sampleRate) * volume;
                default: return NextNoise(sampleRate) * volume;
            }
        }

        float NextPulse(double sr)
        {
            double dt = freq / sr;
            if (dt <= 0 || dt >= 0.5) return 0f;
            double d = DutyTable[duty & 3];
            double t = phase;
            double v = t < d ? 1.0 : -1.0;
            // PolyBLEP で立ち上がり／立ち下がりのエイリアスを抑える
            v += PolyBlep(t, dt);
            double t2 = t - d;
            if (t2 < 0) t2 += 1.0;
            v -= PolyBlep(t2, dt);
            phase += dt;
            if (phase >= 1.0) phase -= 1.0;
            return (float)v;
        }

        float NextTriangle(double sr)
        {
            double dt = freq / sr;
            if (dt <= 0 || dt >= 0.5) return 0f;
            int step = (int)(phase * 32.0) & 31; // 32段の階段三角波
            int val = step < 16 ? 15 - step : step - 16;
            phase += dt;
            if (phase >= 1.0) phase -= 1.0;
            return (float)(val / 7.5 - 1.0);
        }

        float NextNoise(double sr)
        {
            noiseAcc += noiseRate / sr;
            int guard = 64;
            while (noiseAcc >= 1.0 && guard-- > 0)
            {
                noiseAcc -= 1.0;
                uint bit = (lfsr ^ (lfsr >> (shortMode ? 6 : 1))) & 1u;
                lfsr = (lfsr >> 1) | (bit << 14);
            }
            if (lfsr == 0) lfsr = 1;
            return (lfsr & 1u) == 0 ? 1f : -1f;
        }

        static double PolyBlep(double t, double dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1.0;
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return t * t + t + t + 1.0;
            }
            return 0.0;
        }

        public static double NoiseRateForPitch(int pitch)
        {
            int idx = Math.Max(0, Math.Min(15, pitch));
            return NesCpuClock / NoisePeriods[15 - idx];
        }
    }
}
