using System;

namespace UniDAW
{
    /// <summary>メトロノーム／プリカウントのクリック音（短く減衰するサイン波、小節頭は高い音）。</summary>
    sealed class ClickVoice
    {
        const float DecaySeconds = 0.012f;
        const float Freq = 1000f, AccentFreq = 1600f;

        readonly double sampleRate;
        readonly float decay;
        float phase, freq, env;

        public ClickVoice(double sampleRate)
        {
            this.sampleRate = sampleRate;
            decay = (float)Math.Exp(-1.0 / (DecaySeconds * sampleRate));
        }

        public void Trigger(bool accent)
        {
            freq = accent ? AccentFreq : Freq;
            phase = 0f;
            env = 1f;
        }

        /// <summary>行番号が拍頭ならクリックする（4 拍ごとにアクセント）。</summary>
        public void OnRow(int row, int rowsPerBeat)
        {
            if (row % rowsPerBeat == 0) Trigger(row % (rowsPerBeat * 4) == 0);
        }

        public void Silence() => env = 0f;

        /// <summary>次の 1 サンプル（鳴っていなければ 0）。</summary>
        public float Next(float volume)
        {
            if (env <= 1e-4f) return 0f;
            float k = (float)Math.Sin(phase * 2.0 * Math.PI) * env * volume * 0.5f;
            phase += (float)(freq / sampleRate);
            if (phase >= 1f) phase -= 1f;
            env *= decay;
            return k;
        }
    }
}
