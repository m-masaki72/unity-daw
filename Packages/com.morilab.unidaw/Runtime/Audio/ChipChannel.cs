using System;

namespace UniDAW
{
    /// <summary>
    /// 1 チャンネル分の発音処理：インストゥルメントのシーケンス・ノート単位のエフェクト・音源ボイス。
    /// シーケンサー（行・tick の進行）とは独立しており、<see cref="ChipEngine"/> から tick ごとに駆動される。
    /// オーディオスレッドで動くので、生成後はアロケーションしない。
    /// </summary>
    internal sealed class ChipChannel
    {
        ChipVoice voice;

        bool active;
        bool released;
        int baseNote;
        float pitch;
        float volume;
        int volPos, arpPos, dutyPos, pitchPos;
        float pitchMacro;
        int arpX, arpY, arpStep;
        int slide;
        int portaSpeed;
        float portaTarget = -1;
        int vibSpeed, vibDepth;
        float vibPhase;
        int volSlide;
        int dutyOverride = -1;
        int cutTick = -1;

        /// <summary>使用中のインストゥルメント番号（-1 = 未設定）。</summary>
        public int Instrument = -1;

        public ChipChannel(ChannelKind kind) => voice = ChipVoice.Create(kind);

        public bool Active => active;
        /// <summary>鳴っていてリリース前か（ポルタメントで音程だけ変えられる状態）。</summary>
        public bool Sustaining => active && !released;
        public int BaseNote => baseNote;
        /// <summary>現在の音量（0-15）。</summary>
        public float Volume => volume;
        /// <summary>最終ピッチ（半音、小数あり）。</summary>
        public float OutPitch { get; private set; }
        public float OutputVolume => voice.gate ? voice.volume : 0f;

        public float NextSample(double sampleRate) => voice.Next(sampleRate);

        /// <summary>即座に無音にする。</summary>
        public void Cut()
        {
            active = false;
            voice.gate = false;
        }

        public void SetVolume(int v) => volume = v;

        /// <summary>新しいノートを発音する（ノート単位のエフェクトはリセット）。</summary>
        public void Trigger(int note, int vol)
        {
            active = true;
            released = false;
            baseNote = note;
            pitch = note;
            volume = Math.Max(0, Math.Min(15, vol));
            volPos = arpPos = dutyPos = pitchPos = 0;
            pitchMacro = 0;
            arpX = arpY = arpStep = 0;
            slide = 0;
            portaSpeed = 0;
            portaTarget = -1;
            vibSpeed = vibDepth = 0;
            vibPhase = 0;
            volSlide = 0;
            dutyOverride = -1;
            cutTick = -1;
            voice.gate = true;
        }

        /// <summary>発音中の音を再トリガーせず、目標音程へポルタメントさせる。</summary>
        public void GlideTo(int note)
        {
            portaTarget = note;
            baseNote = note;
        }

        /// <summary>ノートオフ：リリース区間があればそこへ、なければ止める。</summary>
        public void Release(Instrument inst)
        {
            if (!active) return;
            if (inst != null && !inst.volume.IsEmpty && inst.volume.release >= 0)
            {
                released = true;
                volPos = inst.volume.release;
                if (inst.arpeggio.release >= 0) arpPos = inst.arpeggio.release;
                if (inst.duty.release >= 0) dutyPos = inst.duty.release;
                if (inst.pitch != null && inst.pitch.release >= 0) pitchPos = inst.pitch.release;
            }
            else Cut();
        }

        /// <summary>チャンネル単位のエフェクトを適用する（Speed などシーケンサー側の効果は扱わない）。</summary>
        public void ApplyFx(Fx fx)
        {
            switch (fx.type)
            {
                case FxType.Arpeggio:
                    arpX = fx.X;
                    arpY = fx.Y;
                    arpStep = 0;
                    break;
                case FxType.SlideUp:
                    slide = fx.param;
                    break;
                case FxType.SlideDown:
                    slide = -fx.param;
                    break;
                case FxType.Portamento:
                    portaSpeed = fx.param;
                    break;
                case FxType.Vibrato:
                    vibSpeed = fx.X;
                    vibDepth = fx.Y;
                    break;
                case FxType.VolumeSlide:
                    volSlide = fx.X - fx.Y;
                    break;
                case FxType.Duty:
                    dutyOverride = fx.param & 3;
                    break;
                case FxType.NoteCut:
                    cutTick = fx.param;
                    break;
                case FxType.NoteSlideUp:
                case FxType.NoteSlideDown:
                    if (!active || fx.Y == 0) break;
                    int target = baseNote + (fx.type == FxType.NoteSlideUp ? fx.Y : -fx.Y);
                    portaTarget = Math.Max(0, Math.Min(127, target));
                    baseNote = (int)portaTarget;
                    portaSpeed = fx.X * 2 + 1;
                    break;
            }
        }

        /// <summary>
        /// 1 tick 進めてボイスのパラメータを更新する。
        /// <paramref name="rowTick"/> は行内の tick 位置（再生停止中は -1、ノートカット判定に使う）。
        /// </summary>
        public void Update(Instrument inst, int rowTick)
        {
            if (!active)
            {
                voice.gate = false;
                return;
            }

            if (cutTick >= 0 && rowTick >= cutTick)
            {
                Cut();
                return;
            }

            int seqVol = 15, seqArp = 0, seqDuty = -1;
            if (inst != null)
            {
                if (!inst.volume.IsEmpty) seqVol = inst.volume.Step(ref volPos, released);
                if (!inst.arpeggio.IsEmpty) seqArp = inst.arpeggio.Step(ref arpPos, released);
                if (!inst.duty.IsEmpty) seqDuty = inst.duty.Step(ref dutyPos, released);
                if (inst.pitch != null && !inst.pitch.IsEmpty) pitchMacro += inst.pitch.Step(ref pitchPos, released) / 16f;
            }

            if (volSlide != 0)
                volume = Math.Max(0f, Math.Min(15f, volume + volSlide / 8f));

            if (portaTarget >= 0 && portaSpeed > 0)
            {
                float step = portaSpeed / 16f;
                if (pitch < portaTarget) pitch = Math.Min(pitch + step, portaTarget);
                else if (pitch > portaTarget) pitch = Math.Max(pitch - step, portaTarget);
            }
            if (slide != 0) pitch += slide / 16f;

            float arpOff = 0f;
            if (arpX != 0 || arpY != 0)
            {
                int k = arpStep % 3;
                arpOff = k == 0 ? 0 : (k == 1 ? arpX : arpY);
                arpStep++;
            }

            float vib = 0f;
            if (vibDepth > 0)
            {
                vib = (float)Math.Sin(vibPhase * Math.PI * 2.0) * vibDepth / 8f;
                vibPhase += vibSpeed / 64f;
                if (vibPhase >= 1f) vibPhase -= 1f;
            }

            float p = pitch + arpOff + seqArp + vib + pitchMacro;
            OutPitch = p;

            int duty = dutyOverride >= 0 ? dutyOverride : (seqDuty >= 0 ? seqDuty : (voice.kind == ChannelKind.Noise ? 0 : 2));
            voice.volume = volume / 15f * (Math.Max(0, Math.Min(15, seqVol)) / 15f);
            voice.gate = true;

            if (voice.kind == ChannelKind.Noise)
            {
                voice.noiseRate = ChipVoice.NoiseRateForPitch((int)Math.Round(p));
                voice.shortMode = (duty & 1) == 1;
            }
            else
            {
                voice.freq = NoteText.MidiToHz(p);
                voice.duty = duty & 3;
            }
        }
    }
}
