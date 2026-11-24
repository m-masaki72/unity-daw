using System;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// セル単位の編集操作（ノート入力・ステップ切替・削除・数値入力・移調・トラックコピー）。
    /// キー・マウス・MIDI など入力手段に依存しない「何をするか」だけを持つ。
    /// </summary>
    public sealed class CellCommands
    {
        /// <summary>トラックに音が無いときの既定ノート（ch 毎）。</summary>
        static readonly int[] DefaultNotes = { 72, 67, 48, 9 };

        /// <summary>Ctrl+C/V のトラッククリップボード（ウィンドウ間で共有）。</summary>
        static Cell[] clipboard;
        static int clipboardLength;

        readonly ComposerSession s;

        public CellCommands(ComposerSession session) => s = session;

        ComposerState State => s.State;

        /// <summary>セルにノートを置き、チャンネルの楽器を設定して試聴する（全ビュー共通）。</summary>
        public void PlaceNote(string undoName, int ch, int note, double previewSeconds)
        {
            int inst = s.ChannelInstrument(ch);
            s.ModifyCell(undoName, (ref Cell c) =>
            {
                c.note = note;
                c.instrument = inst;
            });
            s.Transport.PreviewNote(ch, note, inst, previewSeconds);
        }

        /// <summary>カーソル位置へノート（またはオフ）を入力して進む。</summary>
        public void EnterNote(int note)
        {
            if (!s.CanEdit()) return;
            if (note >= 0) PlaceNote("Enter Note", State.cursorCh, note, 0.25);
            else s.ModifyCell("Enter Note", (ref Cell c) => c.note = note);
            s.Advance();
        }

        /// <summary>鍵盤上の位置（オクターブ基準の半音）からノート番号を決めて入力する。</summary>
        public void EnterPianoKey(int offset)
        {
            int note = Song.Kinds[State.cursorCh] == ChannelKind.Noise
                ? offset % 16
                : Mathf.Clamp((State.octave + 1) * 12 + offset, NoteText.MinNote, NoteText.MaxNote);
            EnterNote(note);
        }

        /// <summary>ステップクリック。toggle なら置く／消す、そうでなければ選択のみ。</summary>
        public void StepClick(int ch, int row, bool toggle)
        {
            if (s.Song == null) return;
            s.SetCursor(row, ch, 0);
            var p = s.CurrentPattern;
            if (!toggle || row >= p.TrackLength(ch)) return;
            var cell = p.tracks[ch].cells[row];
            if (cell.HasNote || cell.IsOff) s.ModifyCell("Toggle Step", (ref Cell c) => c = Cell.Blank);
            else PlaceNote("Toggle Step", ch, LastNoteInTrack(p, ch, row), 0.15);
        }

        static int LastNoteInTrack(Pattern p, int ch, int row)
        {
            var cells = p.tracks[ch].cells;
            for (int i = row - 1; i >= 0; i--)
                if (cells[i].HasNote) return cells[i].note;
            for (int i = cells.Length - 1; i > row; i--)
                if (cells[i].HasNote) return cells[i].note;
            return DefaultNotes[ch];
        }

        /// <summary>トラッカー列（0 = ノート…6 = Fx2 パラメータ）の内容を消す。ノート列なら進む。</summary>
        public void Delete(int col)
        {
            s.ModifyCell("Delete", (ref Cell c) =>
            {
                switch (col)
                {
                    case 0: c.note = Cell.Empty; c.instrument = -1; c.volume = -1; break;
                    case 1: c.instrument = -1; break;
                    case 2: c.volume = -1; break;
                    case 3: case 4: c.fx1 = Fx.None; break;
                    default: c.fx2 = Fx.None; break;
                }
            });
            if (col == 0) s.Advance();
        }

        /// <summary>エフェクト種別列（3 または 5）へ種別を入力し、パラメータ列へ移る。</summary>
        public void SetFxType(int col, FxType t)
        {
            bool first = col == 3;
            s.ModifyCell("Edit Fx", (ref Cell c) =>
            {
                if (first) c.fx1 = new Fx(t, c.fx1.param);
                else c.fx2 = new Fx(t, c.fx2.param);
            });
            s.SetCursor(State.cursorRow, State.cursorCh, col + 1);
        }

        /// <summary>数値列（1 楽器 / 2 音量 / 4・6 Fx パラメータ）へ 16 進 1 桁を入力する。</summary>
        public void TypeHex(int col, int d)
        {
            switch (col)
            {
                case 1:
                    s.ModifyCell("Edit Instrument", (ref Cell c) => c.instrument = ((Math.Max(0, c.instrument) << 4) | d) & 0xFF);
                    break;
                case 2:
                    s.ModifyCell("Edit Volume", (ref Cell c) => c.volume = d);
                    s.Advance();
                    break;
                case 4:
                    s.ModifyCell("Edit Fx", (ref Cell c) =>
                    {
                        if (!c.fx1.IsEmpty) c.fx1 = new Fx(c.fx1.type, ((c.fx1.param << 4) | d) & 0xFF);
                    });
                    break;
                case 6:
                    s.ModifyCell("Edit Fx", (ref Cell c) =>
                    {
                        if (!c.fx2.IsEmpty) c.fx2 = new Fx(c.fx2.type, ((c.fx2.param << 4) | d) & 0xFF);
                    });
                    break;
            }
        }

        /// <summary>カーソルのノートを移調（ノイズは周期を 1 段ずつ）して試聴する。</summary>
        public void Transpose(int semis)
        {
            int ch = State.cursorCh;
            bool noise = Song.Kinds[ch] == ChannelKind.Noise;
            s.ModifyCell("Transpose", (ref Cell c) =>
            {
                if (!c.HasNote) return;
                c.note = noise
                    ? Mathf.Clamp(c.note + Math.Sign(semis), 0, 15)
                    : Mathf.Clamp(c.note + semis, NoteText.MinNote, NoteText.MaxNote);
            });
            var cell = State.CursorCell;
            if (cell.HasNote) s.Transport.PreviewNote(ch, cell.note, s.ChannelInstrument(ch), 0.2);
        }

        public void CopyTrack()
        {
            var p = s.CurrentPattern;
            if (p == null) return;
            int ch = State.cursorCh;
            clipboard = (Cell[])p.tracks[ch].cells.Clone();
            clipboardLength = p.tracks[ch].length;
            s.Notify($"{Song.ChannelNames[ch]} をコピーしましたわ");
        }

        public void PasteTrack()
        {
            if (clipboard == null) return;
            int ch = State.cursorCh;
            s.ModifyPattern("Paste Track", pat =>
            {
                var dst = pat.tracks[ch].cells;
                for (int i = 0; i < dst.Length && i < clipboard.Length; i++) dst[i] = clipboard[i];
                pat.tracks[ch].length = clipboardLength < pat.length ? clipboardLength : 0;
            });
        }

        // ---- インスペクタ用（カーソルのセルを直接編集） ---------------------------

        public void ClearCell() => s.ModifyCell("Clear Cell", (ref Cell c) => c = Cell.Blank);

        /// <summary>パラメータロック（音量・エフェクト・確率・条件）だけを外す。</summary>
        public void ClearLocks() => s.ModifyCell("Clear Locks", (ref Cell c) =>
        {
            c.volume = -1;
            c.fx1 = Fx.None;
            c.fx2 = Fx.None;
            c.probability = 100;
            c.condA = c.condB = 0;
        });
    }
}
