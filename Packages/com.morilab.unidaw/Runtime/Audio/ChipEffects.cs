using System;

namespace UniDAW
{
    /// <summary>
    /// センドエフェクト：テンポ同期ステレオディレイ（ピンポン可、帰還路にローパス）と
    /// Freeverb 系の軽いリバーブ（コム 4 本 + オールパス 2 本 × 左右）。オーディオスレッド専用。
    /// </summary>
    internal sealed class ChipEffects
    {
        // Freeverb の調律（44.1kHz 基準のサンプル数）
        static readonly int[] CombTuning = { 1116, 1188, 1277, 1356 };
        static readonly int[] AllpassTuning = { 556, 441 };
        const int StereoSpread = 23;
        const float ReverbInputGain = 0.03f;
        const float AllpassFeedback = 0.5f;
        const float DelayTone = 0.35f;
        /// <summary>送りが止まってから処理を続ける長さ（秒）。残響・やまびこの尾を切らない。</summary>
        const double TailSeconds = 12.0;

        readonly double sampleRate;
        readonly float[] delayL, delayR;
        int delayPos;
        float toneL, toneR;

        readonly Comb[] combL, combR;
        readonly Allpass[] apL, apR;

        int delaySamples;
        float feedback, delayReturn, reverbReturn;
        bool pingPong;
        long tailRemaining;

        public ChipEffects(double sampleRate)
        {
            this.sampleRate = sampleRate;
            int maxDelay = (int)(sampleRate * 60.0 / 20.0 * SongEffects.MaxDelayBeats) + 2; // BPM 20 の 2 拍まで
            delayL = new float[maxDelay];
            delayR = new float[maxDelay];
            double scale = sampleRate / 44100.0;
            combL = new Comb[CombTuning.Length];
            combR = new Comb[CombTuning.Length];
            for (int i = 0; i < CombTuning.Length; i++)
            {
                combL[i] = new Comb(Math.Max(1, (int)(CombTuning[i] * scale)));
                combR[i] = new Comb(Math.Max(1, (int)((CombTuning[i] + StereoSpread) * scale)));
            }
            apL = new Allpass[AllpassTuning.Length];
            apR = new Allpass[AllpassTuning.Length];
            for (int i = 0; i < AllpassTuning.Length; i++)
            {
                apL[i] = new Allpass(Math.Max(1, (int)(AllpassTuning[i] * scale)));
                apR[i] = new Allpass(Math.Max(1, (int)((AllpassTuning[i] + StereoSpread) * scale)));
            }
        }

        /// <summary>処理が要るか（送りがある、または尾が残っている）。</summary>
        public bool Active => tailRemaining > 0;

        /// <summary>ブロックの始めに設定を確定する。<paramref name="anySend"/> は送りのあるチャンネルがあるか。</summary>
        public void Begin(SongEffects fx, float bpm, bool anySend, int blockFrames)
        {
            fx ??= new SongEffects();
            if (anySend) tailRemaining = (long)(sampleRate * TailSeconds);
            else tailRemaining -= blockFrames;

            double beats = Math.Max(1f / 16f, Math.Min(SongEffects.MaxDelayBeats, fx.delayBeats));
            delaySamples = (int)Math.Max(1, Math.Min(delayL.Length - 1, sampleRate * 60.0 / Math.Max(1f, bpm) * beats));
            feedback = Math.Max(0f, Math.Min(SongEffects.MaxFeedback, fx.delayFeedback));
            pingPong = fx.delayPingPong;
            delayReturn = ChannelMix.DbToGain(fx.delayReturnDb);
            reverbReturn = ChannelMix.DbToGain(fx.reverbReturnDb);

            float room = 0.7f + 0.28f * SongEffects.Clamp01(fx.reverbSize);
            float damp = 0.4f * SongEffects.Clamp01(fx.reverbDamp);
            for (int i = 0; i < combL.Length; i++)
            {
                combL[i].Set(room, damp);
                combR[i].Set(room, damp);
            }
        }

        /// <summary>1 サンプル処理し、ウェット音（L/R）を返す。</summary>
        public void Process(float dl, float dr, float rl, float rr, out float wetL, out float wetR)
        {
            // ---- ディレイ
            int read = delayPos - delaySamples;
            if (read < 0) read += delayL.Length;
            float outL = delayL[read], outR = delayR[read];
            toneL += (outL - toneL) * (1f - DelayTone);
            toneR += (outR - toneR) * (1f - DelayTone);
            if (pingPong)
            {
                delayL[delayPos] = (dl + dr) * 0.5f + toneR * feedback;
                delayR[delayPos] = toneL * feedback;
            }
            else
            {
                delayL[delayPos] = dl + toneL * feedback;
                delayR[delayPos] = dr + toneR * feedback;
            }
            if (++delayPos >= delayL.Length) delayPos = 0;

            // ---- リバーブ
            float input = (rl + rr) * ReverbInputGain;
            float revL = 0f, revR = 0f;
            for (int i = 0; i < combL.Length; i++)
            {
                revL += combL[i].Process(input);
                revR += combR[i].Process(input);
            }
            for (int i = 0; i < apL.Length; i++)
            {
                revL = apL[i].Process(revL);
                revR = apR[i].Process(revR);
            }

            wetL = outL * delayReturn + revL * reverbReturn;
            wetR = outR * delayReturn + revR * reverbReturn;
        }

        sealed class Comb
        {
            readonly float[] buf;
            int pos;
            float store, feedback, damp;

            public Comb(int size) => buf = new float[size];

            public void Set(float feedback, float damp)
            {
                this.feedback = feedback;
                this.damp = damp;
            }

            public float Process(float input)
            {
                float output = buf[pos];
                store = output * (1f - damp) + store * damp;
                buf[pos] = input + store * feedback;
                if (++pos >= buf.Length) pos = 0;
                return output;
            }
        }

        sealed class Allpass
        {
            readonly float[] buf;
            int pos;

            public Allpass(int size) => buf = new float[size];

            public float Process(float input)
            {
                float b = buf[pos];
                buf[pos] = input + b * AllpassFeedback;
                if (++pos >= buf.Length) pos = 0;
                return b - input;
            }
        }
    }
}
