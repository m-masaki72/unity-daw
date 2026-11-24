using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UniDAW
{
    public struct NoteEvent
    {
        public int channel;
        public int note;
        public float volume;
        public bool live;
        public int row;
    }

    /// <summary>
    /// シーケンサー＋4ch チップ音源。UnityEngine API に依存しないので
    /// オーディオスレッド・オフラインレンダリング・テストのどこからでも使える。
    /// メインスレッドからの操作はコマンドキュー経由（スレッドセーフ）。
    /// </summary>
    public sealed class ChipEngine
    {
        public const int Channels = Song.ChannelCount;

        struct ChannelState
        {
            public bool active;
            public bool released;
            public int instrument;
            public int baseNote;
            public float pitch;
            public float volume;
            public int volPos, arpPos, dutyPos;
            public int arpX, arpY, arpStep;
            public int slide;
            public int portaSpeed;
            public float portaTarget;
            public int vibSpeed, vibDepth;
            public float vibPhase;
            public int volSlide;
            public int dutyOverride;
            public int cutTick;
            public float outPitch;
        }

        enum CmdType { Play, PlayPattern, Stop, NoteOn, NoteOff, AllNotesOff }

        struct Command
        {
            public CmdType type;
            public int a, b, c, d;
        }

        readonly ChipVoice[] voices = new ChipVoice[Channels];
        readonly ChannelState[] st = new ChannelState[Channels];
        readonly ConcurrentQueue<Command> commands = new ConcurrentQueue<Command>();

        /// <summary>ノート発音イベント（映像連携用）。メインスレッドで取り出す。</summary>
        public readonly ConcurrentQueue<NoteEvent> NoteEvents = new ConcurrentQueue<NoteEvent>();
        public bool EmitNoteEvents = true;

        public readonly bool[] Mute = new bool[Channels];
        public readonly bool[] Solo = new bool[Channels];
        /// <summary>チャンネル毎の出力レベル（0..1、オーディオスレッドが更新）</summary>
        public readonly float[] Levels = new float[Channels];
        public float MasterVolume = 0.8f;

        volatile Song song;
        readonly double sampleRate;
        double samplesToTick;

        bool playing;
        bool loopPattern;
        int loopPatternIndex;
        int orderPos;
        int row;
        int tickInRow;
        int patternLoop;
        int songLoops;
        int speedOverride = -1;
        float bpmOverride = -1;
        uint rngSeed = 0x9E3779B9u;

        float dcIn, dcOut;
        readonly float[] levelAcc = new float[Channels];

        public volatile int PlayingOrder;
        public volatile int PlayingRow;
        public volatile int PlayingPattern = -1;
        public volatile int PatternLoopCount;
        public volatile int SongLoopCount;
        public volatile bool IsPlaying;

        public ChipEngine(double sampleRate)
        {
            this.sampleRate = sampleRate > 0 ? sampleRate : 48000;
            for (int c = 0; c < Channels; c++)
            {
                voices[c] = ChipVoice.Create(Song.Kinds[c]);
                st[c].instrument = -1;
                st[c].dutyOverride = -1;
                st[c].cutTick = -1;
                st[c].portaTarget = -1;
            }
        }

        public double SampleRate => sampleRate;

        public Song Song
        {
            get => song;
            set => song = value;
        }

        /// <summary>プロバビリティ判定の乱数シード（同じシードなら同じ結果）。</summary>
        public uint RandomSeed { get => rngSeed; set => rngSeed = value; }

        // ---- メインスレッド用 API -------------------------------------------------

        public void Play(int order = 0, int row = 0) =>
            commands.Enqueue(new Command { type = CmdType.Play, a = order, b = row });

        public void PlayPattern(int patternIndex, int row = 0) =>
            commands.Enqueue(new Command { type = CmdType.PlayPattern, a = patternIndex, b = row });

        public void Stop() => commands.Enqueue(new Command { type = CmdType.Stop });

        public void NoteOn(int channel, int note, int volume = 15, int instrument = -1) =>
            commands.Enqueue(new Command { type = CmdType.NoteOn, a = channel, b = note, c = volume, d = instrument });

        public void NoteOff(int channel) => commands.Enqueue(new Command { type = CmdType.NoteOff, a = channel });

        public void AllNotesOff() => commands.Enqueue(new Command { type = CmdType.AllNotesOff });

        /// <summary>デバッグ・テスト用：チャンネルの最終ピッチ（半音、小数あり）。</summary>
        public float GetChannelPitch(int ch) => st[ch].outPitch;
        public bool IsChannelActive(int ch) => st[ch].active;
        public float GetChannelVolume(int ch) => voices[ch].gate ? voices[ch].volume : 0f;

        // ---- オーディオスレッド ----------------------------------------------------

        /// <summary>interleaved バッファに書き込む（加算ではなく上書き）。</summary>
        public void Render(float[] data, int offset, int frames, int outChannels)
        {
            DrainCommands();
            var s = song;
            bool anySolo = false;
            for (int c = 0; c < Channels; c++) anySolo |= Solo[c];
            for (int c = 0; c < Channels; c++) levelAcc[c] = 0f;

            int idx = offset;
            for (int i = 0; i < frames; i++)
            {
                samplesToTick -= 1.0;
                if (samplesToTick <= 0)
                {
                    Tick(s);
                    samplesToTick += SamplesPerTick(s);
                }

                float mix = 0f;
                for (int c = 0; c < Channels; c++)
                {
                    float v = voices[c].Next(sampleRate);
                    if (Mute[c] || (anySolo && !Solo[c])) continue;
                    float g = c == 3 ? 0.8f : 1f;
                    v *= g;
                    mix += v;
                    float a = v < 0 ? -v : v;
                    if (a > levelAcc[c]) levelAcc[c] = a;
                }

                // DC ブロッカー（パルスのデューティによる直流分を除去）
                float y = mix - dcIn + 0.995f * dcOut;
                dcIn = mix;
                dcOut = y;
                y *= 0.25f * MasterVolume;
                if (y > 1f) y = 1f;
                else if (y < -1f) y = -1f;

                for (int ch = 0; ch < outChannels; ch++) data[idx++] = y;
            }

            for (int c = 0; c < Channels; c++) Levels[c] = levelAcc[c];
        }

        /// <summary>曲を1周オフラインでレンダリング（モノラル）。</summary>
        public static float[] RenderSong(Song song, int sampleRate, double maxSeconds = 600)
        {
            var e = new ChipEngine(sampleRate) { Song = song, EmitNoteEvents = false };
            e.Play();
            const int block = 512;
            var buf = new float[block];
            var outList = new List<float>(sampleRate * 10);
            long maxFrames = (long)(maxSeconds * sampleRate);
            while (outList.Count < maxFrames)
            {
                e.Render(buf, 0, block, 1);
                if (e.SongLoopCount >= 1)
                {
                    // 最後の行が終わったところで止める（ブロック単位なので軽く余韻が乗る）
                    outList.AddRange(buf);
                    break;
                }
                outList.AddRange(buf);
            }
            // 短いフェードアウト
            int fade = Math.Min(outList.Count, sampleRate / 50);
            for (int i = 0; i < fade; i++)
                outList[outList.Count - 1 - i] *= i / (float)fade;
            return outList.ToArray();
        }

        void DrainCommands()
        {
            while (commands.TryDequeue(out var cmd))
            {
                switch (cmd.type)
                {
                    case CmdType.Play:
                        StartPlayback(false, cmd.a, cmd.b);
                        break;
                    case CmdType.PlayPattern:
                        StartPlayback(true, cmd.a, cmd.b);
                        break;
                    case CmdType.Stop:
                        playing = false;
                        IsPlaying = false;
                        PlayingPattern = -1;
                        CutAll();
                        break;
                    case CmdType.NoteOn:
                        if (cmd.a >= 0 && cmd.a < Channels)
                        {
                            if (cmd.d >= 0) st[cmd.a].instrument = cmd.d;
                            Trigger(cmd.a, cmd.b, cmd.c, true, -1);
                            UpdateChannel(cmd.a, song); // 即時に発音させてレイテンシを減らす
                        }
                        break;
                    case CmdType.NoteOff:
                        if (cmd.a >= 0 && cmd.a < Channels) Release(cmd.a, song);
                        break;
                    case CmdType.AllNotesOff:
                        CutAll();
                        break;
                }
            }
        }

        void StartPlayback(bool patternMode, int a, int startRow)
        {
            CutAll();
            playing = true;
            IsPlaying = true;
            loopPattern = patternMode;
            loopPatternIndex = patternMode ? a : -1;
            orderPos = patternMode ? 0 : Math.Max(0, a);
            row = Math.Max(0, startRow);
            tickInRow = 0;
            patternLoop = 0;
            songLoops = 0;
            PatternLoopCount = 0;
            SongLoopCount = 0;
            speedOverride = -1;
            bpmOverride = -1;
            samplesToTick = 1; // 最初のサンプルで tick → 以降ちょうど SamplesPerTick 間隔
        }

        void CutAll()
        {
            for (int c = 0; c < Channels; c++)
            {
                st[c].active = false;
                voices[c].gate = false;
            }
        }

        double SamplesPerTick(Song s)
        {
            float bpm = bpmOverride > 0 ? bpmOverride : (s != null ? s.bpm : 150f);
            int rpb = s != null ? Math.Max(1, s.rowsPerBeat) : 4;
            int spd = CurrentSpeed(s);
            if (bpm < 1f) bpm = 1f;
            // tick/秒 = bpm * rowsPerBeat * speed / 60
            return sampleRate * 60.0 / (bpm * rpb * spd);
        }

        int CurrentSpeed(Song s) => speedOverride > 0 ? speedOverride : (s != null ? Math.Max(1, s.speed) : 6);

        int CurrentPatternIndex(Song s)
        {
            if (s == null) return -1;
            if (loopPattern) return loopPatternIndex >= 0 && loopPatternIndex < s.patterns.Count ? loopPatternIndex : -1;
            return s.PatternAtOrder(orderPos);
        }

        void Tick(Song s)
        {
            if (playing && s != null && tickInRow == 0) ProcessRow(s);
            for (int c = 0; c < Channels; c++) UpdateChannel(c, s);
            if (playing)
            {
                tickInRow++;
                if (tickInRow >= CurrentSpeed(s))
                {
                    tickInRow = 0;
                    AdvanceRow(s);
                }
            }
        }

        void ProcessRow(Song s)
        {
            try
            {
                int pi = CurrentPatternIndex(s);
                var p = s.GetPattern(pi);
                if (p == null)
                {
                    playing = false;
                    IsPlaying = false;
                    CutAll();
                    return;
                }
                if (row >= p.length) row = 0;
                PlayingRow = row;
                PlayingOrder = orderPos;
                PlayingPattern = pi;
                int cycle = loopPattern ? patternLoop : songLoops;
                for (int c = 0; c < Channels && c < p.tracks.Length; c++)
                {
                    var t = p.tracks[c];
                    int idx = p.CellIndex(c, row, loopPattern ? patternLoop : 0);
                    if (t.cells == null || idx >= t.cells.Length) continue;
                    ApplyCell(c, t.cells[idx], s, cycle, idx, pi);
                }
            }
            catch (Exception)
            {
                // 編集中の構造変更と競合しても音声スレッドは止めない
            }
        }

        void AdvanceRow(Song s)
        {
            row++;
            var p = s?.GetPattern(CurrentPatternIndex(s));
            int len = p != null ? p.length : 1;
            if (row < len) return;
            row = 0;
            if (loopPattern)
            {
                patternLoop++;
                PatternLoopCount = patternLoop;
            }
            else
            {
                orderPos++;
                if (s == null || orderPos >= s.order.Count)
                {
                    orderPos = 0;
                    songLoops++;
                    SongLoopCount = songLoops;
                }
            }
        }

        bool PassesConditions(in Cell cell, int c, int cycle, int idx, int pi)
        {
            if (cell.condB > 1)
            {
                int a = Math.Max(1, Math.Min(cell.condB, cell.condA));
                if (cycle % cell.condB != a - 1) return false;
            }
            if (cell.probability < 100)
            {
                if (cell.probability <= 0) return false;
                uint h = Hash(rngSeed, (uint)cycle, (uint)idx, (uint)(c * 1024 + pi));
                if (h % 100u >= (uint)cell.probability) return false;
            }
            return true;
        }

        static uint Hash(uint a, uint b, uint c, uint d)
        {
            uint h = a ^ 0x85EBCA6Bu;
            h = (h ^ b) * 0x9E3779B1u;
            h = (h ^ (h >> 15) ^ c) * 0x85EBCA77u;
            h = (h ^ (h >> 13) ^ d) * 0xC2B2AE3Du;
            return h ^ (h >> 16);
        }

        void ApplyCell(int c, in Cell cell, Song s, int cycle, int idx, int pi)
        {
            if (cell.IsBlank) return;
            if ((cell.HasNote || cell.IsOff) && !PassesConditions(cell, c, cycle, idx, pi)) return;

            ref var ch = ref st[c];
            if (cell.instrument >= 0) ch.instrument = cell.instrument;

            if (cell.IsOff)
            {
                Release(c, s);
            }
            else if (cell.HasNote)
            {
                if (cell.HasFx(FxType.Portamento) && ch.active)
                {
                    ch.portaTarget = cell.note;
                    ch.baseNote = cell.note;
                    if (cell.volume >= 0) ch.volume = cell.volume;
                }
                else
                {
                    Trigger(c, cell.note, cell.volume >= 0 ? cell.volume : 15, false, row);
                }
            }
            else if (cell.volume >= 0)
            {
                ch.volume = cell.volume;
            }

            ApplyFx(c, cell.fx1);
            ApplyFx(c, cell.fx2);
        }

        void ApplyFx(int c, Fx fx)
        {
            if (fx.IsEmpty) return;
            ref var ch = ref st[c];
            switch (fx.type)
            {
                case FxType.Arpeggio:
                    ch.arpX = fx.X;
                    ch.arpY = fx.Y;
                    ch.arpStep = 0;
                    break;
                case FxType.SlideUp:
                    ch.slide = fx.param;
                    break;
                case FxType.SlideDown:
                    ch.slide = -fx.param;
                    break;
                case FxType.Portamento:
                    ch.portaSpeed = fx.param;
                    break;
                case FxType.Vibrato:
                    ch.vibSpeed = fx.X;
                    ch.vibDepth = fx.Y;
                    break;
                case FxType.VolumeSlide:
                    ch.volSlide = fx.X - fx.Y;
                    break;
                case FxType.Duty:
                    ch.dutyOverride = fx.param & 3;
                    break;
                case FxType.Speed:
                    if (fx.param == 0) break;
                    if (fx.param < 0x20) speedOverride = fx.param;
                    else bpmOverride = fx.param;
                    break;
                case FxType.NoteCut:
                    ch.cutTick = fx.param;
                    break;
            }
        }

        void Trigger(int c, int note, int volume, bool live, int atRow)
        {
            ref var ch = ref st[c];
            ch.active = true;
            ch.released = false;
            ch.baseNote = note;
            ch.pitch = note;
            ch.volume = Math.Max(0, Math.Min(15, volume));
            ch.volPos = ch.arpPos = ch.dutyPos = 0;
            ch.arpX = ch.arpY = ch.arpStep = 0;
            ch.slide = 0;
            ch.portaSpeed = 0;
            ch.portaTarget = -1;
            ch.vibSpeed = ch.vibDepth = 0;
            ch.vibPhase = 0;
            ch.volSlide = 0;
            ch.dutyOverride = -1;
            ch.cutTick = -1;
            voices[c].gate = true;

            if (EmitNoteEvents && NoteEvents.Count < 512)
                NoteEvents.Enqueue(new NoteEvent { channel = c, note = note, volume = ch.volume / 15f, live = live, row = atRow });
        }

        void Release(int c, Song s)
        {
            ref var ch = ref st[c];
            if (!ch.active) return;
            var inst = s?.GetInstrument(ch.instrument);
            if (inst != null && !inst.volume.IsEmpty && inst.volume.release >= 0)
            {
                ch.released = true;
                ch.volPos = inst.volume.release;
                if (inst.arpeggio.release >= 0) ch.arpPos = inst.arpeggio.release;
                if (inst.duty.release >= 0) ch.dutyPos = inst.duty.release;
            }
            else
            {
                ch.active = false;
                voices[c].gate = false;
            }
        }

        void UpdateChannel(int c, Song s)
        {
            ref var ch = ref st[c];
            ref var v = ref voices[c];
            if (!ch.active)
            {
                v.gate = false;
                return;
            }

            if (ch.cutTick >= 0 && playing && tickInRow >= ch.cutTick)
            {
                ch.active = false;
                v.gate = false;
                return;
            }

            int seqVol = 15, seqArp = 0, seqDuty = -1;
            var inst = s?.GetInstrument(ch.instrument);
            if (inst != null)
            {
                if (!inst.volume.IsEmpty) seqVol = inst.volume.Step(ref ch.volPos, ch.released);
                if (!inst.arpeggio.IsEmpty) seqArp = inst.arpeggio.Step(ref ch.arpPos, ch.released);
                if (!inst.duty.IsEmpty) seqDuty = inst.duty.Step(ref ch.dutyPos, ch.released);
            }

            if (ch.volSlide != 0)
                ch.volume = Math.Max(0f, Math.Min(15f, ch.volume + ch.volSlide / 8f));

            if (ch.portaTarget >= 0 && ch.portaSpeed > 0)
            {
                float step = ch.portaSpeed / 16f;
                if (ch.pitch < ch.portaTarget) ch.pitch = Math.Min(ch.pitch + step, ch.portaTarget);
                else if (ch.pitch > ch.portaTarget) ch.pitch = Math.Max(ch.pitch - step, ch.portaTarget);
            }
            if (ch.slide != 0) ch.pitch += ch.slide / 16f;

            float arpOff = 0f;
            if (ch.arpX != 0 || ch.arpY != 0)
            {
                int k = ch.arpStep % 3;
                arpOff = k == 0 ? 0 : (k == 1 ? ch.arpX : ch.arpY);
                ch.arpStep++;
            }

            float vib = 0f;
            if (ch.vibDepth > 0)
            {
                vib = (float)Math.Sin(ch.vibPhase * Math.PI * 2.0) * ch.vibDepth / 8f;
                ch.vibPhase += ch.vibSpeed / 64f;
                if (ch.vibPhase >= 1f) ch.vibPhase -= 1f;
            }

            float p = ch.pitch + arpOff + seqArp + vib;
            ch.outPitch = p;

            int duty = ch.dutyOverride >= 0 ? ch.dutyOverride : (seqDuty >= 0 ? seqDuty : (v.kind == ChannelKind.Noise ? 0 : 2));
            v.volume = ch.volume / 15f * (Math.Max(0, Math.Min(15, seqVol)) / 15f);
            v.gate = true;

            if (v.kind == ChannelKind.Noise)
            {
                int pitch = (int)Math.Round(p);
                v.noiseRate = ChipVoice.NoiseRateForPitch(pitch);
                v.shortMode = (duty & 1) == 1;
            }
            else
            {
                v.freq = NoteText.MidiToHz(p);
                v.duty = duty & 3;
            }
        }
    }
}
