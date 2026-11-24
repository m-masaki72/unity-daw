using System;
using System.Collections.Generic;

namespace UniDAW.Editor
{
    /// <summary>ピアノロール上の 1 音：開始行・長さ・高さと、発音セルの中身（楽器・音量・エフェクト）。</summary>
    public struct RollNote
    {
        public int start, length, pitch;
        public Cell cell;

        public RollNote(int start, int length, int pitch, Cell cell)
        {
            this.start = start;
            this.length = length;
            this.pitch = pitch;
            this.cell = cell;
        }

        public int End => start + length;
        public bool Contains(int row) => row >= start && row < End;
        public override string ToString() => $"{pitch}@{start}+{length}";
    }

    /// <summary>
    /// トラッカーのセル列 ⇔ ノート一覧 の変換と、ノート一覧に対する純粋な変形。
    /// セル列は単音（次のノート／オフ／トラック終端まで鳴る）なので、
    /// 書き戻しでは重なりを後勝ちで詰め、終端にオフを置く。
    /// </summary>
    public static class RollNotes
    {
        /// <summary>チャンネルのノートをトラック長の範囲で取り出す（開始行の昇順）。</summary>
        public static List<RollNote> Extract(Pattern p, int ch)
        {
            var list = new List<RollNote>();
            var cells = p.tracks[ch].cells;
            int tl = p.TrackLength(ch);
            int start = -1;
            for (int r = 0; r <= tl; r++)
            {
                bool end = r == tl || cells[r].HasNote || cells[r].IsOff;
                if (end && start >= 0)
                {
                    list.Add(new RollNote(start, r - start, cells[start].note, cells[start]));
                    start = -1;
                }
                if (r < tl && cells[r].HasNote) start = r;
            }
            return list;
        }

        /// <summary>
        /// 範囲内に収め、同じ開始行は後ろの要素を優先し、次のノートとの重なりを切り詰める。
        /// 戻り値は開始行の昇順。
        /// </summary>
        public static List<RollNote> Normalize(IEnumerable<RollNote> notes, int trackLength)
        {
            var byStart = new SortedDictionary<int, RollNote>();
            foreach (var n in notes)
            {
                if (n.start < 0 || n.start >= trackLength) continue;
                byStart[n.start] = n;
            }
            var list = new List<RollNote>(byStart.Values);
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                int limit = i + 1 < list.Count ? list[i + 1].start : trackLength;
                n.length = Math.Max(1, Math.Min(n.length, limit - n.start));
                list[i] = n;
            }
            return list;
        }

        /// <summary>
        /// ノート一覧をセル列へ書き戻す。既存のノート／オフは消し（ノートだけのセルは丸ごと、
        /// オフはノート欄だけ）、エフェクトだけのセルは残す。
        /// </summary>
        public static void Write(Pattern p, int ch, IEnumerable<RollNote> notes)
        {
            var cells = p.tracks[ch].cells;
            int tl = p.TrackLength(ch);
            var list = Normalize(notes, tl);
            for (int r = 0; r < tl; r++)
            {
                if (cells[r].HasNote) cells[r] = Cell.Blank;
                else if (cells[r].IsOff) cells[r].note = Cell.Empty;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                var c = n.cell;
                c.note = n.pitch;
                // 置き先にエフェクトだけのセルがあれば、ノート側が空の列は引き継ぐ
                var under = cells[n.start];
                if (c.fx1.IsEmpty) c.fx1 = under.fx1;
                if (c.fx2.IsEmpty) c.fx2 = under.fx2;
                cells[n.start] = c;
                int end = n.End;
                bool nextStartsHere = i + 1 < list.Count && list[i + 1].start == end;
                if (end < tl && !nextStartsHere) cells[end].note = Cell.Off;
            }
        }

        /// <summary>
        /// 選択したノートを (dRow, dPitch) ずらす。copy なら元を残す。
        /// 群として範囲からはみ出さないよう移動量を詰める。結果と、動かしたノートの開始行を返す。
        /// </summary>
        public static List<RollNote> Move(IReadOnlyList<RollNote> notes, ICollection<int> selected, int dRow, int dPitch,
            bool copy, int trackLength, int minPitch, int maxPitch, out HashSet<int> movedStarts)
        {
            int lo = int.MaxValue, hi = int.MinValue, pLo = int.MaxValue, pHi = int.MinValue;
            foreach (var n in notes)
            {
                if (!selected.Contains(n.start)) continue;
                lo = Math.Min(lo, n.start);
                hi = Math.Max(hi, n.start);
                pLo = Math.Min(pLo, n.pitch);
                pHi = Math.Max(pHi, n.pitch);
            }
            movedStarts = new HashSet<int>();
            if (lo == int.MaxValue) return new List<RollNote>(notes);
            dRow = Math.Max(-lo, Math.Min(trackLength - 1 - hi, dRow));
            dPitch = Math.Max(minPitch - pLo, Math.Min(maxPitch - pHi, dPitch));

            // 動かさないノートを先に、動かしたノートを後に並べる（後勝ちで上書き）
            var result = new List<RollNote>();
            var moved = new List<RollNote>();
            foreach (var n in notes)
            {
                bool sel = selected.Contains(n.start);
                if (!sel || copy) result.Add(n);
                if (!sel) continue;
                var m = n;
                m.start += dRow;
                m.pitch += dPitch;
                moved.Add(m);
                movedStarts.Add(m.start);
            }
            if (!copy || dRow != 0 || dPitch != 0) result.AddRange(moved);
            else movedStarts = new HashSet<int>(selected);
            return Normalize(result, trackLength);
        }

        /// <summary>選択したノートの長さを delta 行だけ変える（最短 1 行、トラック終端まで）。</summary>
        public static List<RollNote> Resize(IReadOnlyList<RollNote> notes, ICollection<int> selected, int delta, int trackLength)
        {
            var result = new List<RollNote>(notes.Count);
            foreach (var n in notes)
            {
                var m = n;
                if (selected.Contains(n.start))
                    m.length = Math.Max(1, Math.Min(trackLength - n.start, n.length + delta));
                result.Add(m);
            }
            // 伸ばしたノートは後ろのノートを押しのけず、そこで止まる
            return Normalize(result, trackLength);
        }

        /// <summary>選択範囲（最初の開始〜最後の終端）のすぐ後ろへ複製する。</summary>
        public static List<RollNote> Duplicate(IReadOnlyList<RollNote> notes, ICollection<int> selected, int trackLength, out HashSet<int> copies)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var n in notes)
            {
                if (!selected.Contains(n.start)) continue;
                lo = Math.Min(lo, n.start);
                hi = Math.Max(hi, n.End);
            }
            copies = new HashSet<int>();
            if (lo == int.MaxValue) return new List<RollNote>(notes);
            int span = hi - lo;
            var result = new List<RollNote>(notes);
            foreach (var n in notes)
            {
                if (!selected.Contains(n.start) || n.start + span >= trackLength) continue;
                var m = n;
                m.start += span;
                result.Add(m);
                copies.Add(m.start);
            }
            return Normalize(result, trackLength);
        }

        /// <summary>貼り付け：クリップボードの先頭を row に合わせて置く（重なった既存ノートは上書き）。</summary>
        public static List<RollNote> Paste(IReadOnlyList<RollNote> notes, IReadOnlyList<RollNote> clip, int row, int trackLength, out HashSet<int> pasted)
        {
            pasted = new HashSet<int>();
            var result = new List<RollNote>(notes);
            if (clip == null || clip.Count == 0) return result;
            int origin = int.MaxValue;
            foreach (var n in clip) origin = Math.Min(origin, n.start);
            foreach (var n in clip)
            {
                var m = n;
                m.start = row + (n.start - origin);
                if (m.start >= trackLength) continue;
                result.Add(m);
                pasted.Add(m.start);
            }
            return Normalize(result, trackLength);
        }
    }
}
