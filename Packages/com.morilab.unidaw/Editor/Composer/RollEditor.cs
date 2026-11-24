using System;
using System.Collections.Generic;
using System.Linq;

namespace UniDAW.Editor
{
    /// <summary>
    /// ピアノロールの編集（選択・作成・移動・長さ変更・削除・複製・コピー＆ペースト）。
    /// 対象はカーソルのあるチャンネル。UI に依存せず、マウスでもキーでもテストでも同じ操作になる。
    /// 選択はノートの開始行で持ち、パターンかチャンネルが変わると解除する。
    /// </summary>
    public sealed class RollEditor
    {
        /// <summary>ノートのコピー（ウィンドウ間で共有）。</summary>
        static List<RollNote> clipboard;

        readonly ComposerSession s;
        readonly HashSet<int> selected = new HashSet<int>();
        (Song song, int pattern, int ch) owner;

        public RollEditor(ComposerSession session) => s = session;

        ComposerState State => s.State;
        public int Channel => State.cursorCh;
        public bool IsNoise => Song.Kinds[Channel] == ChannelKind.Noise;
        public int MinPitch => IsNoise ? 0 : NoteText.MinNote;
        public int MaxPitch => IsNoise ? 15 : NoteText.MaxNote;
        public int TrackLength => s.CurrentPattern?.TrackLength(Channel) ?? 0;

        /// <summary>今のノート一覧（開始行の昇順）。</summary>
        public List<RollNote> Notes()
        {
            var p = s.CurrentPattern;
            return p != null ? RollNotes.Extract(p, Channel) : new List<RollNote>();
        }

        /// <summary>選択中の開始行（存在しないノートは除く）。</summary>
        public HashSet<int> Selection
        {
            get
            {
                Sync();
                var starts = new HashSet<int>(Notes().Select(n => n.start));
                selected.IntersectWith(starts);
                return new HashSet<int>(selected);
            }
        }

        public bool IsSelected(int start)
        {
            Sync();
            return selected.Contains(start);
        }

        void Sync()
        {
            var key = (State.song, State.patternIndex, State.cursorCh);
            if (owner == key) return;
            owner = key;
            selected.Clear();
        }

        /// <summary>行（トラック長で折り返し済み）と高さにあるノート。</summary>
        public bool TryGetNote(int row, int pitch, out RollNote note)
        {
            foreach (var n in Notes())
            {
                if (n.pitch == pitch && n.Contains(row))
                {
                    note = n;
                    return true;
                }
            }
            note = default;
            return false;
        }

        // ---- 選択 ------------------------------------------------------------------

        public void Select(int start, bool additive)
        {
            Sync();
            if (!additive) selected.Clear();
            selected.Add(start);
            s.NotifyChanged();
        }

        public void ToggleSelect(int start)
        {
            Sync();
            if (!selected.Remove(start)) selected.Add(start);
            s.NotifyChanged();
        }

        public void ClearSelection()
        {
            Sync();
            if (selected.Count == 0) return;
            selected.Clear();
            s.NotifyChanged();
        }

        public void SelectAll()
        {
            Sync();
            selected.Clear();
            foreach (var n in Notes()) selected.Add(n.start);
            s.NotifyChanged();
        }

        /// <summary>範囲選択（行 r0..r1 に掛かり、高さ p0..p1 のノート）。</summary>
        public void SelectRect(int r0, int r1, int p0, int p1, bool additive)
        {
            Sync();
            if (!additive) selected.Clear();
            int rl = Math.Min(r0, r1), rh = Math.Max(r0, r1), pl = Math.Min(p0, p1), ph = Math.Max(p0, p1);
            foreach (var n in Notes())
                if (n.pitch >= pl && n.pitch <= ph && n.start <= rh && n.End - 1 >= rl) selected.Add(n.start);
            s.NotifyChanged();
        }

        /// <summary>← → で前後のノートを選ぶ（extend なら選択に足す）。</summary>
        public void SelectNeighbor(int dir, bool extend)
        {
            var notes = Notes();
            if (notes.Count == 0) return;
            var sel = Selection;
            int idx;
            if (sel.Count == 0) idx = dir > 0 ? 0 : notes.Count - 1;
            else
            {
                int edge = dir > 0 ? sel.Max() : sel.Min();
                int cur = notes.FindIndex(n => n.start == edge);
                idx = Math.Max(0, Math.Min(notes.Count - 1, cur + Math.Sign(dir)));
            }
            var target = notes[idx];
            Select(target.start, extend);
            s.SetCursor(target.start, Channel, 0);
            Preview(target.pitch);
        }

        // ---- 編集 ------------------------------------------------------------------

        void Apply(string undoName, List<RollNote> notes, HashSet<int> newSelection)
        {
            int ch = Channel;
            Sync();
            selected.Clear();
            if (newSelection != null) selected.UnionWith(newSelection);
            s.ModifyPattern(undoName, p => RollNotes.Write(p, ch, notes));
        }

        void Preview(int pitch) => s.Transport.PreviewNote(Channel, pitch, s.ChannelInstrument(Channel), 0.15);

        /// <summary>ノートを作る（長さは行数）。作ったノートだけを選択する。</summary>
        public void Create(int row, int pitch, int length)
        {
            if (!s.CanEdit() || row < 0 || row >= TrackLength) return;
            pitch = Math.Max(MinPitch, Math.Min(MaxPitch, pitch));
            var notes = Notes();
            notes.Add(new RollNote(row, Math.Max(1, length), pitch, Cell.NoteCell(pitch, s.ChannelInstrument(Channel))));
            Apply("Roll Create", notes, new HashSet<int> { row });
            s.SetCursor(row, Channel, 0);
            Preview(pitch);
        }

        public void Delete(int start)
        {
            if (!s.CanEdit()) return;
            Apply("Roll Delete", Notes().Where(n => n.start != start).ToList(), null);
        }

        public void DeleteSelected()
        {
            var sel = Selection;
            if (sel.Count == 0 || !s.CanEdit()) return;
            Apply("Roll Delete", Notes().Where(n => !sel.Contains(n.start)).ToList(), null);
        }

        /// <summary>選択中のノートの移動結果（ドラッグ中の表示にも使う）。</summary>
        public List<RollNote> PreviewMove(int dRow, int dPitch, bool copy, out HashSet<int> moved) =>
            RollNotes.Move(Notes(), Selection, dRow, dPitch, copy, TrackLength, MinPitch, MaxPitch, out moved);

        public void MoveSelected(int dRow, int dPitch, bool copy)
        {
            if ((dRow == 0 && dPitch == 0) || Selection.Count == 0 || !s.CanEdit()) return;
            var notes = PreviewMove(dRow, dPitch, copy, out var moved);
            Apply(copy ? "Roll Copy" : dRow == 0 ? "Roll Transpose" : "Roll Move", notes, moved);
            if (dPitch != 0)
            {
                var first = notes.FirstOrDefault(n => moved.Contains(n.start));
                Preview(first.pitch);
            }
        }

        public List<RollNote> PreviewResize(int delta) => RollNotes.Resize(Notes(), Selection, delta, TrackLength);

        public void ResizeSelected(int delta)
        {
            if (delta == 0 || Selection.Count == 0 || !s.CanEdit()) return;
            Apply("Roll Length", PreviewResize(delta), Selection);
        }

        public void Duplicate()
        {
            if (Selection.Count == 0 || !s.CanEdit()) return;
            var notes = RollNotes.Duplicate(Notes(), Selection, TrackLength, out var copies);
            Apply("Roll Duplicate", notes, copies);
        }

        public void Copy()
        {
            var sel = Selection;
            clipboard = Notes().Where(n => sel.Contains(n.start)).ToList();
            s.Notify($"{clipboard.Count} ノートをコピーしましたわ");
        }

        public void Cut()
        {
            Copy();
            DeleteSelected();
        }

        /// <summary>カーソル行へ貼り付ける。</summary>
        public void Paste()
        {
            if (clipboard == null || clipboard.Count == 0 || !s.CanEdit()) return;
            var notes = RollNotes.Paste(Notes(), clipboard, State.cursorRow % Math.Max(1, TrackLength), TrackLength, out var pasted);
            Apply("Roll Paste", notes, pasted);
        }

        /// <summary>行 from..to に始まるノートを notes で置き換え、置いたノートを選択する（生成結果の書き込み）。</summary>
        public void ReplaceRange(string undoName, int from, int to, IEnumerable<RollNote> notes)
        {
            if (!s.CanEdit()) return;
            var list = Notes().Where(n => n.start < from || n.start >= to).ToList();
            var added = notes.ToList();
            list.AddRange(added);
            Apply(undoName, list, new HashSet<int>(added.Select(n => n.start)));
        }

        public bool HasClipboard => clipboard != null && clipboard.Count > 0;
    }
}
