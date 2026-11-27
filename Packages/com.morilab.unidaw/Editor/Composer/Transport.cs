using System;

namespace UniDAW.Editor
{
    /// <summary>
    /// 再生・停止・試聴・再生位置の監視。UI には依存しない。
    /// エンジンへは <see cref="IAudioHost"/> 越しに触る。
    /// </summary>
    public sealed class Transport
    {
        /// <summary>キーを離してから試聴音を止めるまでの猶予（秒）。</summary>
        const double ReleaseDelay = 0.02;

        readonly ComposerState state;
        readonly IAudioHost host;
        readonly double[] previewOff = new double[Song.ChannelCount];
        int lastPlayRow = -2, lastPlayPattern = -2;

        /// <summary>表示を更新すべき変化（停止・サイクル切替など）。</summary>
        public event Action Changed;

        public Transport(ComposerState state, IAudioHost host = null)
        {
            this.state = state;
            this.host = host ?? UniDAWPreview.AudioHost;
            for (int c = 0; c < previewOff.Length; c++) previewOff[c] = -1;
        }

        public ChipEngine Engine => host.Engine;
        public bool IsPlaying => Engine != null && Engine.IsPlaying;

        /// <summary>表示中パターンでの再生行（別パターン再生中・停止中は -1）。</summary>
        public int PlayheadRow
        {
            get
            {
                var e = Engine;
                if (e == null || !e.IsPlaying || e.PlayingPattern != state.patternIndex) return -1;
                return e.PlayingRow;
            }
        }

        public int PlayheadLoop
        {
            get
            {
                var e = Engine;
                return e != null && e.IsPlaying ? e.PatternLoopCount : 0;
            }
        }

        /// <summary>曲の差し替え：再生を止めてエンジンに新しい曲を渡す。</summary>
        public void Attach(Song song)
        {
            var e = Engine;
            if (e == null) return;
            e.Stop();
            e.Song = song;
        }

        ChipEngine Prepare()
        {
            var e = Engine;
            host.EnsureRunning();
            e.Song = state.song;
            e.Metronome = state.metronome;
            return e;
        }

        /// <summary>メトロノームの切り替え（再生中もすぐ効く）。</summary>
        public void ToggleMetronome()
        {
            state.metronome = !state.metronome;
            var e = Engine;
            if (e != null) e.Metronome = state.metronome;
            Changed?.Invoke();
        }

        public void ToggleCountIn()
        {
            state.countIn = !state.countIn;
            Changed?.Invoke();
        }

        /// <summary>順序表の位置から曲を再生する（アレンジ表示のダブルクリック）。</summary>
        public void PlayFromOrder(int order)
        {
            if (state.song == null) return;
            state.loop = false;
            Prepare().Play(order, 0, CountIn);
            Changed?.Invoke();
        }

        /// <summary>プリカウントの拍数（4/4 の 1 小節）。</summary>
        public const int CountInBeats = 4;

        /// <summary>録音オン・プリカウントオンなら 1 小節数えてから始める。</summary>
        int CountIn => state.recording && state.countIn ? CountInBeats : 0;

        public void PlayPattern()
        {
            if (state.song != null) Prepare().PlayPattern(state.patternIndex, 0, CountIn);
        }

        /// <summary>今のパターンを指定行からループ再生（試聴用。プリカウントなし）。</summary>
        public void PlayPatternFrom(int row)
        {
            if (state.song != null) Prepare().PlayPattern(state.patternIndex, Math.Max(0, row), 0);
        }

        public void PlaySong()
        {
            if (state.song != null) Prepare().Play(state.orderIndex, 0, CountIn);
        }

        public void Stop()
        {
            Engine?.Stop();
            lastPlayRow = -2;
            Changed?.Invoke();
        }

        public void TogglePatternPlay()
        {
            if (IsPlaying) Stop();
            else PlayPattern();
        }

        // ---- DAW 流のトランスポート（Cubase / Studio One のテンキー操作に合わせる） ----

        /// <summary>再生開始：サイクルがオンならパターンをループ、オフなら順序表に沿って曲を再生。</summary>
        public void Start()
        {
            if (state.loop) PlayPattern();
            else PlaySong();
        }

        /// <summary>Space：再生／停止の切り替え。</summary>
        public void TogglePlay()
        {
            if (IsPlaying) Stop();
            else Start();
        }

        /// <summary>サイクルの切り替え。再生中なら今の位置から新しいモードで続ける。</summary>
        public void ToggleLoop()
        {
            state.loop = !state.loop;
            var e = Engine;
            if (e != null && e.IsPlaying && !e.CountingIn && state.song != null)
            {
                int pat = e.PlayingPattern, order = e.PlayingOrder, row = e.PlayingRow;
                if (state.loop) Prepare().PlayPattern(pat, row);
                else Prepare().Play(order, row);
            }
            Changed?.Invoke();
        }

        /// <summary>ノートを一定時間だけ鳴らす（再生中はシーケンスを邪魔しないので鳴らさない）。</summary>
        public void PreviewNote(int ch, int note, int inst, double seconds)
        {
            var e = Engine;
            if (e == null || e.IsPlaying || state.song == null) return;
            Prepare().NoteOn(ch, note, 15, inst);
            previewOff[ch] = DawPlatform.Current.Now + seconds;
        }

        /// <summary>曲に入っていない楽器（プリセット）を一定時間だけ鳴らす。</summary>
        public void PreviewInstrument(int ch, int note, Instrument inst, double seconds)
        {
            var e = Engine;
            if (e == null || inst == null) return;
            e.PreviewInstrument = inst;
            PreviewNote(ch, note, ChipEngine.PreviewSlot, seconds);
        }

        /// <summary>鍵盤を離したとき：試聴音をすぐ止める。</summary>
        public void ReleasePreview(int ch)
        {
            var e = Engine;
            if (e != null && !e.IsPlaying) previewOff[ch] = DawPlatform.Current.Now + ReleaseDelay;
        }

        /// <summary>
        /// 毎フレームの処理：エンジンとの同期、試聴音の解放、再生位置の変化検出。
        /// 再生位置が変わったら true（<paramref name="patternChanged"/> はパターンまで変わったか）。
        /// </summary>
        public bool Poll(out bool patternChanged)
        {
            patternChanged = false;
            var e = Engine;
            UniDAWMidiBridge.Engine = e;
            UniDAWMidiBridge.TargetChannel = state.cursorCh;
            if (e == null) return false;
            if (state.song != null && e.Song != state.song) e.Song = state.song;
            e.Metronome = state.metronome;
            host.DispatchEvents();

            double now = DawPlatform.Current.Now;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                if (previewOff[c] > 0 && now >= previewOff[c])
                {
                    previewOff[c] = -1;
                    if (!e.IsPlaying) e.NoteOff(c);
                }
            }

            int pr = e.IsPlaying ? e.PlayingRow : -1;
            int pp = e.IsPlaying ? e.PlayingPattern : -1;
            if (pr == lastPlayRow && pp == lastPlayPattern) return false;
            patternChanged = pp != lastPlayPattern;
            lastPlayRow = pr;
            lastPlayPattern = pp;
            return true;
        }
    }
}
