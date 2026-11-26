using System;
using System.Collections.Concurrent;

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

    /// <summary>拍の頭の通知（ゲーム側で音楽に合わせた演出をするため）。拍子は 4/4 として数える。</summary>
    public struct BeatEvent
    {
        /// <summary>再生開始からの拍・小節の通し番号（0 始まり）。</summary>
        public int beat, bar;
        /// <summary>小節の中の拍（0..3）。0 なら小節頭。</summary>
        public int beatInBar;
        public int order, pattern, row;
        /// <summary>パターンの頭（区間の変わり目）か。</summary>
        public bool patternStart;
        public bool BarStart => beatInBar == 0;
    }

    /// <summary>
    /// シーケンサー＋4ch チップ音源。UnityEngine API に依存しないので
    /// オーディオスレッド・オフラインレンダリング・テストのどこからでも使える。
    /// メインスレッドからの操作はコマンドキュー経由（スレッドセーフ）。
    /// このクラスは行・tick の進行（シーケンサー）を受け持ち、発音は <see cref="ChipChannel"/>、
    /// ミックスは <see cref="ChipMixer"/>、トリガー条件は <see cref="TriggerConditions"/> に任せる。
    /// </summary>
    public sealed class ChipEngine
    {
        public const int Channels = Song.ChannelCount;
        const int MaxQueuedNoteEvents = 512;

        /// <summary>行の 1 セルと、その適用に要る文脈（周回数・セル位置・移調）。Gxx ではそのまま保留する。</summary>
        struct RowCell
        {
            public Cell cell;
            public int cycle, idx, pi, transpose;
        }

        /// <summary>Gxx ノートディレイ待ちのセル。</summary>
        struct DelayedCell
        {
            public bool pending;
            public int tick;
            public RowCell cell;
        }

        enum CmdType { Play, PlayPattern, Stop, NoteOn, NoteOff, AllNotesOff, Queue }

        struct Command
        {
            public CmdType type;
            public int a, b, c, d;
        }

        readonly ChipChannel[] channels = new ChipChannel[Channels];
        readonly DelayedCell[] delayed = new DelayedCell[Channels];
        readonly ChipMixer mixer;
        readonly ConcurrentQueue<Command> commands = new ConcurrentQueue<Command>();

        /// <summary>ノート発音イベント（映像連携用）。メインスレッドで取り出す。</summary>
        public readonly ConcurrentQueue<NoteEvent> NoteEvents = new ConcurrentQueue<NoteEvent>();
        public bool EmitNoteEvents = true;

        /// <summary>拍の頭の通知。メインスレッドで取り出す（溜まりすぎたら捨てる）。</summary>
        public readonly ConcurrentQueue<BeatEvent> BeatEvents = new ConcurrentQueue<BeatEvent>();
        public bool EmitBeatEvents = true;

        /// <summary>曲に入っていない楽器を試聴するための番号（NoteOn の instrument にこれを渡すと <see cref="PreviewInstrument"/> で鳴る）。</summary>
        public const int PreviewSlot = -2;
        public volatile Instrument PreviewInstrument;
        public const int BeatsPerBar = 4;
        /// <summary>予約中の移動先（曲再生なら順序表の位置、パターンループならパターン番号。なければ -1）。</summary>
        public volatile int Queued = -1;

        /// <summary>ゲーム側からの一時的なミュート/ソロ（曲のミキサー設定 <see cref="Song.mixer"/> に重ねて効く）。</summary>
        public readonly bool[] Mute = new bool[Channels];
        public readonly bool[] Solo = new bool[Channels];
        /// <summary>チャンネル毎の出力レベル（フェーダー後、0..1、オーディオスレッドが更新）</summary>
        public readonly float[] Levels = new float[Channels];
        /// <summary>マスター出力の L/R ピーク（0..1）。</summary>
        public readonly float[] OutputLevels = new float[2];
        public float MasterVolume = 0.8f;
        /// <summary>マスターチェーンを通さない（ステム書き出し・チャンネル単位の解析用）。</summary>
        public bool BypassMaster;
        /// <summary>曲のマスターチェーンの代わりに使う設定（解析・候補の試聴用。null なら曲の設定）。</summary>
        public volatile SongMaster MasterOverride;
        /// <summary>直前のブロックでマスターチェーンが下げた最大量（dB、0 以下）。</summary>
        public float MasterReductionDb;
        /// <summary>再生中のラウドネスメーター（マスターチェーンの前と後）。null なら測らない。</summary>
        public volatile LiveLoudness Loudness;
        /// <summary>
        /// A/B 試聴：マスターチェーンを通した音の代わりに、通す前の音を <see cref="AuditionGainDb"/> だけ上げ下げして鳴らす。
        /// チェーンは裏で動き続けるので、メーターとゲインリダクションはそのまま測れる。
        /// </summary>
        public volatile bool AuditionBypass;
        public float AuditionGainDb;

        /// <summary>メトロノーム（拍ごとにクリック、小節頭は高い音）。エディタの試聴用で、書き出しには入らない。</summary>
        public volatile bool Metronome;
        public float MetronomeVolume = 0.5f;
        /// <summary>プリカウント中か（この間は曲を進めない。<see cref="PlayingPattern"/> は -1）。</summary>
        public bool CountingIn => countInRows > 0 && playing;

        readonly ClickVoice click;
        volatile int countInRows;
        int countInTotal;

        volatile Song song;
        readonly double sampleRate;
        double samplesToTick;

        // ---- シーケンサーの位置
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
        int beatCount, barCount;
        bool queuedAtBar;

        public volatile int PlayingOrder;
        public volatile int PlayingRow;
        public volatile int PlayingPattern = -1;
        /// <summary>ポリメーターのセル位置計算用ループ回数（パターンループ再生時のみ非 0）。</summary>
        public volatile int PlayingLoop;
        /// <summary>パターンループ再生中か（false なら曲再生）。</summary>
        public bool IsPatternMode => loopPattern;
        public volatile int PatternLoopCount;
        public volatile int SongLoopCount;
        public volatile bool IsPlaying;

        public ChipEngine(double sampleRate)
        {
            this.sampleRate = sampleRate > 0 ? sampleRate : 48000;
            mixer = new ChipMixer(this.sampleRate);
            click = new ClickVoice(this.sampleRate);
            for (int c = 0; c < Channels; c++) channels[c] = new ChipChannel(Song.Kinds[c]);
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

        /// <summary>順序表に沿って再生。<paramref name="countInBeats"/> 拍のプリカウント（クリック）の後に始める。</summary>
        public void Play(int order = 0, int row = 0, int countInBeats = 0) =>
            commands.Enqueue(new Command { type = CmdType.Play, a = order, b = row, c = countInBeats });

        public void PlayPattern(int patternIndex, int row = 0, int countInBeats = 0) =>
            commands.Enqueue(new Command { type = CmdType.PlayPattern, a = patternIndex, b = row, c = countInBeats });

        public void Stop() => commands.Enqueue(new Command { type = CmdType.Stop });

        public void NoteOn(int channel, int note, int volume = 15, int instrument = -1) =>
            commands.Enqueue(new Command { type = CmdType.NoteOn, a = channel, b = note, c = volume, d = instrument });

        public void NoteOff(int channel) => commands.Enqueue(new Command { type = CmdType.NoteOff, a = channel });

        public void AllNotesOff() => commands.Enqueue(new Command { type = CmdType.AllNotesOff });

        /// <summary>
        /// 再生位置の移動を予約する。曲再生なら順序表の位置、パターンループならパターン番号へ、
        /// <paramref name="atNextBar"/> なら次の小節の頭で、でなければ今のパターンの終わりで移る（ゲームの場面転換向け）。-1 で取り消し。
        /// </summary>
        public void Queue(int target, bool atNextBar = true) =>
            commands.Enqueue(new Command { type = CmdType.Queue, a = target, b = atNextBar ? 1 : 0 });

        /// <summary>デバッグ・テスト用：チャンネルの最終ピッチ（半音、小数あり）。</summary>
        public float GetChannelPitch(int ch) => channels[ch].OutPitch;
        public bool IsChannelActive(int ch) => channels[ch].Active;
        public float GetChannelVolume(int ch) => channels[ch].OutputVolume;

        /// <summary>曲を1周オフラインでレンダリング（モノラル）。<see cref="OfflineRenderer"/> への転送。</summary>
        public static float[] RenderSong(Song song, int sampleRate, double maxSeconds = 600) =>
            OfflineRenderer.RenderSong(song, sampleRate, maxSeconds);

        /// <summary>曲を1周オフラインでレンダリング（ステレオ interleaved）。</summary>
        public static float[] RenderSongStereo(Song song, int sampleRate, double maxSeconds = 600) =>
            OfflineRenderer.RenderSongStereo(song, sampleRate, maxSeconds);

        // ---- オーディオスレッド ----------------------------------------------------

        /// <summary>
        /// interleaved バッファに書き込む（加算ではなく上書き）。
        /// 1ch ならモノラルに畳み、2ch 以上なら先頭 2ch に L/R（残りはモノラル）。
        /// </summary>
        public void Render(float[] data, int offset, int frames, int outChannels)
        {
            DrainCommands();
            var s = song;
            mixer.BeginBlock(s, Mute, Solo, MasterVolume, CurrentBpm(s), frames, BypassMaster ? null : (MasterOverride ?? s?.master));
            mixer.Meter = playing ? Loudness : null;
            mixer.SetAudition(AuditionBypass, AuditionGainDb);

            int idx = offset;
            for (int i = 0; i < frames; i++)
            {
                samplesToTick -= 1.0;
                if (samplesToTick <= 0)
                {
                    Tick(s);
                    samplesToTick += SamplesPerTick(s);
                }

                mixer.Mix(channels, sampleRate, out float l, out float r);
                float k = click.Next(MetronomeVolume);
                l += k;
                r += k;
                if (outChannels == 1)
                {
                    data[idx++] = (l + r) * 0.5f;
                    continue;
                }
                data[idx++] = l;
                data[idx++] = r;
                for (int ch = 2; ch < outChannels; ch++) data[idx++] = (l + r) * 0.5f;
            }

            mixer.EndBlock(Levels, OutputLevels);
            MasterReductionDb = mixer.MasterReductionDb;
        }

        void DrainCommands()
        {
            while (commands.TryDequeue(out var cmd))
            {
                switch (cmd.type)
                {
                    case CmdType.Play:
                        StartPlayback(false, cmd.a, cmd.b, cmd.c);
                        break;
                    case CmdType.PlayPattern:
                        StartPlayback(true, cmd.a, cmd.b, cmd.c);
                        break;
                    case CmdType.Stop:
                        playing = false;
                        IsPlaying = false;
                        PlayingPattern = -1;
                        countInRows = 0;
                        Queued = -1;
                        click.Silence();
                        CutAll();
                        mixer.ResetAutomation();
                        break;
                    case CmdType.NoteOn:
                        if (cmd.a >= 0 && cmd.a < Channels)
                        {
                            if (cmd.d >= 0 || cmd.d == PreviewSlot) channels[cmd.a].Instrument = cmd.d;
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
                    case CmdType.Queue:
                        Queued = playing ? Math.Max(-1, cmd.a) : -1;
                        queuedAtBar = cmd.b != 0;
                        break;
                }
            }
        }

        void StartPlayback(bool patternMode, int a, int startRow, int countInBeats)
        {
            CutAll();
            mixer.ResetAutomation();
            Loudness?.Restart();
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
            beatCount = 0;
            barCount = 0;
            Queued = -1;
            countInTotal = Math.Max(0, Math.Min(16, countInBeats)) * RowsPerBeat(song);
            countInRows = countInTotal;
            if (countInRows > 0) PlayingPattern = -1;
            samplesToTick = 1; // 最初のサンプルで tick → 以降ちょうど SamplesPerTick 間隔
        }

        void CutAll()
        {
            for (int c = 0; c < Channels; c++)
            {
                channels[c].Cut();
                delayed[c].pending = false;
            }
        }

        static int RowsPerBeat(Song s) => s != null ? Math.Max(1, s.rowsPerBeat) : 4;

        float CurrentBpm(Song s) => Math.Max(1f, bpmOverride > 0 ? bpmOverride : (s != null ? s.bpm : 150f));

        double SamplesPerTick(Song s)
        {
            float bpm = CurrentBpm(s);
            int rpb = RowsPerBeat(s);
            int spd = CurrentSpeed(s);
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
            if (playing && countInRows > 0)
            {
                if (tickInRow == 0) click.OnRow(countInTotal - countInRows, RowsPerBeat(s));
                for (int c = 0; c < Channels; c++) UpdateChannel(c, s);
                if (++tickInRow >= CurrentSpeed(s))
                {
                    tickInRow = 0;
                    countInRows--;
                }
                return;
            }
            if (playing && s != null)
            {
                if (tickInRow == 0) ProcessRow(s);
                else FireDelayed(s);
            }
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
                if (Metronome) click.OnRow(row, RowsPerBeat(s));
                PlayingRow = row;
                PlayingOrder = orderPos;
                PlayingPattern = pi;
                EmitBeat(s, pi);
                int cycle = loopPattern ? patternLoop : SongCycle(s, pi);
                PlayingLoop = loopPattern ? patternLoop : 0;
                int transpose = loopPattern ? 0 : s.TransposeAtOrder(orderPos);
                int speed = CurrentSpeed(s);
                for (int c = 0; c < Channels && c < p.tracks.Length; c++)
                {
                    delayed[c].pending = false;
                    var t = p.tracks[c];
                    int idx = p.CellIndex(c, row, loopPattern ? patternLoop : 0);
                    if (t.cells == null || idx >= t.cells.Length) continue;
                    var rc = new RowCell { cell = t.cells[idx], cycle = cycle, idx = idx, pi = pi, transpose = transpose };
                    int delay = rc.cell.DelayTicks(speed);
                    if (delay < 0) continue; // 行の長さ以上のディレイはその行では鳴らない（FamiTracker 互換）
                    if (delay > 0)
                    {
                        delayed[c] = new DelayedCell { pending = true, tick = delay, cell = rc };
                        continue;
                    }
                    ApplyCell(c, rc, s);
                }
            }
            catch (Exception)
            {
                // 編集中の構造変更と競合しても音声スレッドは止めない
            }
        }

        void EmitBeat(Song s, int pi)
        {
            int rpb = RowsPerBeat(s);
            if (row % rpb != 0) return;
            int inBar = row / rpb % BeatsPerBar;
            if (inBar == 0 && beatCount > 0) barCount++;
            var ev = new BeatEvent { beat = beatCount++, bar = barCount, beatInBar = inBar, order = orderPos, pattern = pi, row = row, patternStart = row == 0 };
            if (EmitBeatEvents && BeatEvents.Count < MaxQueuedNoteEvents) BeatEvents.Enqueue(ev);
        }

        Instrument InstrumentOf(Song s, int index) => index == PreviewSlot ? PreviewInstrument : s?.GetInstrument(index);

        void FireDelayed(Song s)
        {
            for (int c = 0; c < Channels; c++)
            {
                ref var d = ref delayed[c];
                if (!d.pending || tickInRow < d.tick) continue;
                d.pending = false;
                ApplyCell(c, d.cell, s);
            }
        }

        void AdvanceRow(Song s)
        {
            row++;
            var p = s?.GetPattern(CurrentPatternIndex(s));
            int len = p != null ? p.length : 1;
            int target = Queued;
            if (target >= 0 && s != null && (row >= len || queuedAtBar && row % (RowsPerBeat(s) * BeatsPerBar) == 0))
            {
                Queued = -1;
                row = 0;
                if (loopPattern)
                {
                    loopPatternIndex = Math.Min(target, s.patterns.Count - 1);
                    patternLoop = 0;
                    PatternLoopCount = 0;
                }
                else
                {
                    orderPos = Math.Min(target, Math.Max(0, s.order.Count - 1));
                }
                return;
            }
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

        /// <summary>曲モードでのパターン出現回数（何周目・順序表で何度目の出現か）。確率・条件トリガーの周回数に使う。</summary>
        int SongCycle(Song s, int pi)
        {
            int total = 0, before = 0;
            for (int i = 0; i < s.order.Count; i++)
            {
                if (s.PatternAtOrder(i) != pi) continue;
                if (i < orderPos) before++;
                total++;
            }
            return songLoops * Math.Max(1, total) + before;
        }

        void ApplyCell(int c, in RowCell rc, Song s)
        {
            ref readonly var cell = ref rc.cell;
            if (cell.IsBlank) return;
            if ((cell.HasNote || cell.IsOff) && !TriggerConditions.Passes(cell, rngSeed, c, rc.cycle, rc.idx, rc.pi)) return;

            var ch = channels[c];
            if (cell.instrument >= 0) ch.Instrument = cell.instrument;

            if (cell.IsOff)
            {
                Release(c, s);
            }
            else if (cell.HasNote)
            {
                int note = Song.TransposeNote(c, cell.note, rc.transpose);
                // リリース中の音へのポルタメントは新規発音として扱う（減衰したまま無音にならないように）
                if (cell.HasFx(FxType.Portamento) && ch.Sustaining)
                {
                    ch.GlideTo(note);
                    if (cell.volume >= 0) ch.SetVolume(cell.volume);
                }
                else
                {
                    Trigger(c, note, cell.volume >= 0 ? cell.volume : 15, false, row);
                }
            }
            else if (cell.volume >= 0)
            {
                ch.SetVolume(cell.volume);
            }

            ApplyFx(c, cell.fx1);
            ApplyFx(c, cell.fx2);
        }

        void ApplyFx(int c, Fx fx)
        {
            if (fx.IsEmpty) return;
            if (fx.type == FxType.Speed)
            {
                // テンポはシーケンサー全体の状態
                if (fx.param == 0) return;
                if (fx.param < 0x20) speedOverride = fx.param;
                else bpmOverride = fx.param;
                return;
            }
            if (fx.TryGetMix(out var mp, out float mv))
            {
                // EQ / ダッキングのオートメーションはミキサー側の状態（停止・再生開始でミキサーの値へ戻る）
                mixer.SetAutomation(c, mp, mv);
                return;
            }
            channels[c].ApplyFx(fx);
        }

        void Trigger(int c, int note, int volume, bool live, int atRow)
        {
            var ch = channels[c];
            ch.Trigger(note, volume);
            if (EmitNoteEvents && NoteEvents.Count < MaxQueuedNoteEvents)
                NoteEvents.Enqueue(new NoteEvent { channel = c, note = note, volume = ch.Volume / 15f, live = live, row = atRow });
        }

        void Release(int c, Song s)
        {
            var ch = channels[c];
            ch.Release(InstrumentOf(s, ch.Instrument));
        }

        void UpdateChannel(int c, Song s)
        {
            var ch = channels[c];
            ch.Update(InstrumentOf(s, ch.Instrument), playing ? tickInRow : -1);
        }
    }
}
