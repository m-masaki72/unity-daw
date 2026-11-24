using System;
using System.Collections.Generic;
using System.Linq;

namespace UniDAW.Editor
{
    /// <summary>
    /// 入力支援（ネット不要）：キーへの音の補正、コード進行からの伴奏生成、メロディの続きの提案。
    /// 理論と生成は Runtime の <see cref="Scale"/> / <see cref="Accompaniment"/> / <see cref="MelodyModel"/> に任せ、
    /// ここは対象の決定と Undo 付きの書き込みだけを行う。
    /// </summary>
    public sealed class AssistCommands
    {
        /// <summary>伴奏を書き込むチャンネル。</summary>
        public const int ChordChannel = 1, BassChannel = 2, DrumChannel = 3;

        readonly ComposerSession s;
        readonly RollEditor roll;
        (Song song, int pattern, int ch, int from, int rows) last;

        public AssistCommands(ComposerSession session, RollEditor roll)
        {
            s = session;
            this.roll = roll;
        }

        ComposerState State => s.State;

        public Scale Key => s.Song != null ? s.Song.Key : default;

        // ---- キー・コード ------------------------------------------------------------

        public void SetKey(int root, ScaleMode mode) => s.ModifySong("Key", x =>
        {
            x.keyRoot = Scale.Pc(root);
            x.scaleMode = mode;
        });

        public string ChordText => s.CurrentPattern?.chords ?? "";

        public void SetChords(string text)
        {
            text = text?.Trim() ?? "";
            if (text == ChordText) return;
            s.ModifyPattern("Chords", p => p.chords = text);
        }

        public List<Chord> Chords(out string error) => ChordProgression.Parse(ChordText, Key, out error);

        /// <summary>今のパターンの row にあるコード。</summary>
        public Chord? ChordAt(int row)
        {
            var p = s.CurrentPattern;
            return p != null ? ChordProgression.At(Chords(out _), row, p.length) : null;
        }

        // ---- スケール補正 ------------------------------------------------------------

        /// <summary>
        /// スケール外の音を最寄りの音へ。wholePattern でなければ、ピアノロールの選択（なければカーソルのチャンネル全体）。
        /// 戻り値は直した音の数。
        /// </summary>
        public int ConformToScale(bool wholePattern)
        {
            var key = Key;
            if (s.Song == null) return 0;
            if (key.IsChromatic)
            {
                s.Notify("先にキー（スケール）を選んでくださいまし");
                return 0;
            }
            if (!s.CanEdit()) return 0;
            int cur = State.cursorCh;
            HashSet<int> sel = null;
            if (!wholePattern && State.view == PatternViewKind.PianoRoll)
            {
                sel = roll.Selection;
                if (sel.Count == 0) sel = null;
            }

            int Visit(Pattern p, bool write)
            {
                int n = 0;
                for (int ch = 0; ch < Song.ChannelCount; ch++)
                {
                    if (Song.Kinds[ch] == ChannelKind.Noise || (!wholePattern && ch != cur)) continue;
                    var cells = p.tracks[ch].cells;
                    for (int r = 0; r < cells.Length; r++)
                    {
                        if (!cells[r].HasNote || key.Contains(cells[r].note) || (sel != null && !sel.Contains(r))) continue;
                        n++;
                        if (write) cells[r].note = Math.Max(NoteText.MinNote, Math.Min(NoteText.MaxNote, key.Snap(cells[r].note)));
                    }
                }
                return n;
            }

            int count = Visit(s.CurrentPattern, false);
            if (count > 0) s.ModifyPattern("Conform To Scale", p => Visit(p, true));
            s.Notify(count > 0 ? $"{count} 音を {key} に合わせましたわ" : $"すべて {key} の音ですわ");
            return count;
        }

        // ---- 伴奏 --------------------------------------------------------------------

        public void SetBassStyle(BassStyle v) => SetOption(() => State.assistBass = v);
        public void SetChordStyle(ChordStyle v) => SetOption(() => State.assistChord = v);
        public void SetDrumStyle(DrumStyle v) => SetOption(() => State.assistDrums = v);
        public void SetFill(bool v) => SetOption(() => State.assistFill = v);
        public void SetRows(int v) => SetOption(() => State.assistRows = Math.Max(1, Math.Min(Pattern.MaxRows, v)));
        public void SetTemperature(float v) => SetOption(() => State.assistTemperature = Math.Max(0.2f, Math.Min(2f, v)));

        void SetOption(Action set)
        {
            set();
            s.NotifyChanged();
        }

        static int Inst(Song song, int index) => song.instruments.Count == 0 ? -1 : Math.Min(index, song.instruments.Count - 1);

        /// <summary>コード進行から Pulse 2（和音）・Triangle（ベース）・Noise（ドラム）を作り直す（「なし」のパートは触らない）。</summary>
        public bool GenerateAccompaniment()
        {
            var song = s.Song;
            var p = s.CurrentPattern;
            if (song == null || p == null) return false;
            var chords = Chords(out string error);
            var st = State;
            bool needChords = st.assistBass != BassStyle.None || st.assistChord != ChordStyle.None;
            if (needChords && chords.Count == 0)
            {
                s.Notify(error ?? "コード進行を入力してくださいまし（例: C G Am F / I V vi IV）");
                return false;
            }
            if (!needChords && st.assistDrums == DrumStyle.None)
            {
                s.Notify("作るパートを選んでくださいまし");
                return false;
            }
            if (!s.CanEdit()) return false;

            int len = p.length, rpb = song.rowsPerBeat;
            s.ModifyPattern("Generate Accompaniment", pat =>
            {
                if (st.assistChord != ChordStyle.None) Put(pat, ChordChannel, Accompaniment.Chords(chords, len, rpb, st.assistChord, Inst(song, Song.InstSoft)));
                if (st.assistBass != BassStyle.None) Put(pat, BassChannel, Accompaniment.Bass(chords, len, rpb, st.assistBass, Inst(song, Song.InstBass)));
                if (st.assistDrums != DrumStyle.None) Put(pat, DrumChannel, Accompaniment.Drums(len, rpb, st.assistDrums, st.assistFill));
            });
            s.Notify(error != null ? $"伴奏を作りましたわ（{error}）" : $"伴奏を作りましたわ（{chords.Count} コード）");
            return true;
        }

        static void Put(Pattern p, int ch, Cell[] cells)
        {
            p.tracks[ch].cells = cells;
            p.tracks[ch].length = 0;
        }

        // ---- メロディ ----------------------------------------------------------------

        /// <summary>チャンネルの音域（提案で使う範囲）と、文脈がないときの中心。</summary>
        static (int min, int max, int center) Range(int ch) =>
            Song.Kinds[ch] == ChannelKind.Triangle ? (28, 67, 45) : (48, 96, 72);

        /// <summary>
        /// カーソルのチャンネルで、続きのメロディを提案する。開始はピアノロールの選択の直後（なければカーソル行）。
        /// reroll なら直前に提案した区間を別の案で作り直す。書いたノートは選択状態になる。
        /// </summary>
        public int ContinueMelody(bool reroll)
        {
            var song = s.Song;
            var p = s.CurrentPattern;
            if (song == null || p == null) return 0;
            int ch = State.cursorCh;
            if (Song.Kinds[ch] == ChannelKind.Noise)
            {
                s.Notify("メロディの提案は音程のあるチャンネル（Pulse / Triangle）で使ってくださいまし");
                return 0;
            }
            if (!s.CanEdit()) return 0;

            int tl = p.TrackLength(ch), from, rows;
            if (reroll && last.song == song && last.pattern == State.patternIndex && last.ch == ch && last.from < tl)
            {
                from = last.from;
                rows = Math.Min(last.rows, tl - from);
            }
            else
            {
                var sel = roll.Selection;
                from = sel.Count > 0 ? roll.Notes().Where(n => sel.Contains(n.start)).Max(n => n.End) : State.cursorRow % Math.Max(1, tl);
                rows = Math.Min(State.assistRows, tl - from);
            }
            if (rows <= 0)
            {
                s.Notify("トラックの終わりですわ。カーソルを手前に置いてくださいまし");
                return 0;
            }

            int pi = State.patternIndex, to = from + rows;
            var model = MelodyModel.FromSong(song, (xp, xc, n) => xp == pi && xc == ch && n.start >= from && n.start < to);
            var context = MelodyModel.Extract(p, ch).Where(n => n.start < from).ToList();
            if (context.Count > 8) context = context.GetRange(context.Count - 8, 8);
            var (min, max, center) = Range(ch);
            var chords = Chords(out _);
            State.assistSeed++;
            var opts = new MelodyModel.Options
            {
                key = Key,
                chordAt = row => ChordProgression.At(chords, row, p.length),
                rowsPerBeat = song.rowsPerBeat,
                temperature = State.assistTemperature,
                seed = State.assistSeed,
                minPitch = min,
                maxPitch = max,
                center = center,
            };
            var notes = model.Continue(context, from, rows, opts);
            int inst = s.ChannelInstrument(ch);
            roll.ReplaceRange(reroll ? "AI Reroll" : "AI Continue", from, to,
                notes.Select(n => new RollNote(n.start, n.length, n.pitch, Cell.NoteCell(n.pitch, inst))));
            last = (song, pi, ch, from, rows);
            s.Notify($"{notes.Count} 音を提案しましたわ（行 {from}〜{to - 1}、学習 {model.TrainedSteps} 手）。別案は Reroll");
            return notes.Count;
        }

        /// <summary>直前の提案を作り直せるか。</summary>
        public bool CanReroll => last.song != null && last.song == s.Song && last.pattern == State.patternIndex && last.ch == State.cursorCh;
    }
}
