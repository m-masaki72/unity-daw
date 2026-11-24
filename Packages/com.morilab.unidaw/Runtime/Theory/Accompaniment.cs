using System;
using System.Collections.Generic;

namespace UniDAW
{
    public enum BassStyle { None = 0, Sustain = 1, Beat = 2, RootFifth = 3, Octave = 4, Walking = 5 }
    public enum ChordStyle { None = 0, Arpeggio = 1, Offbeat = 2, ArpUp = 3, Broken = 4 }
    public enum DrumStyle { None = 0, Basic = 1, FourOnFloor = 2, HalfTime = 3, Breakbeat = 4 }

    /// <summary>
    /// コード進行から伴奏（ベース・和音・ドラム）のセル列を作る。行数はパターン長、コードは均等割り付け。
    /// 拍は rowsPerBeat 行、1 小節は 4 拍として扱う。
    /// </summary>
    public static class Accompaniment
    {
        public static readonly string[] BassNames = { "なし", "白玉", "4分", "ルート-5度", "オクターブ", "ウォーキング" };
        public static readonly string[] ChordNames = { "なし", "アルペジオ (0xy)", "裏拍 (0xy)", "分散 ↑", "分散 (1-5-3-5)" };
        public static readonly string[] DrumNames = { "なし", "8ビート", "4つ打ち", "ハーフタイム", "ブレイクビーツ" };

        /// <summary>ベースの根音を置く範囲の下端（E-2 〜 D#3）と、和音の範囲の下端（A-3 〜 G#4）。</summary>
        public const int BassLow = 40, ChordLow = 57;
        /// <summary>ノイズのピッチ（DrumMap / デモと同じ）。</summary>
        public const int KickPitch = 3, SnarePitch = 9, HatPitch = 14;

        static Cell[] Blank(int length)
        {
            var cells = new Cell[length];
            for (int i = 0; i < length; i++) cells[i] = Cell.Blank;
            return cells;
        }

        /// <summary>row にノートを置き、len 行後が空いていればノートオフ。</summary>
        static void Hit(Cell[] cells, int row, int note, int inst, int len, int volume = -1)
        {
            if (row < 0 || row >= cells.Length) return;
            cells[row] = Cell.NoteCell(note, inst, volume);
            int off = row + len;
            if (len > 0 && off < cells.Length && cells[off].note == Cell.Empty) cells[off].note = Cell.Off;
        }

        static int Half(int rpb) => Math.Max(1, rpb / 2);

        // ---- ベース ----------------------------------------------------------------

        public static Cell[] Bass(IReadOnlyList<Chord> chords, int length, int rpb, BassStyle style, int inst = Song.InstBass)
        {
            var cells = Blank(length);
            int n = chords.Count;
            if (n == 0 || style == BassStyle.None) return cells;
            rpb = Math.Max(1, rpb);
            for (int i = 0; i < n; i++)
            {
                var c = chords[i];
                int s = ChordProgression.StartRow(i, n, length), e = ChordProgression.StartRow(i + 1, n, length);
                int root = Chord.Place(c.BassPc, BassLow);
                int fifth = root + 7 > BassLow + 14 ? root - 5 : root + 7;
                switch (style)
                {
                    case BassStyle.Sustain:
                        Hit(cells, s, root, inst, 0);
                        break;
                    case BassStyle.Beat:
                        for (int r = s; r < e; r += rpb) Hit(cells, r, root, inst, Math.Max(1, rpb * 3 / 4));
                        break;
                    case BassStyle.RootFifth:
                        for (int r = s, k = 0; r < e; r += rpb, k++) Hit(cells, r, k % 2 == 0 ? root : fifth, inst, Math.Max(1, rpb * 3 / 4));
                        break;
                    case BassStyle.Octave:
                        for (int r = s, k = 0; r < e; r += Half(rpb), k++) Hit(cells, r, k % 2 == 0 ? root : root + 12, inst, 0);
                        break;
                    case BassStyle.Walking:
                        Walk(cells, c, root, s, e, rpb, i + 1 < n ? chords[i + 1] : chords[0], inst);
                        break;
                }
            }
            return cells;
        }

        /// <summary>拍ごとに 根音 → 構成音 → … → 次の根音への半音アプローチ。</summary>
        static void Walk(Cell[] cells, Chord c, int root, int s, int e, int rpb, Chord next, int inst)
        {
            var iv = c.Intervals;
            int nextRoot = Chord.Place(next.BassPc, BassLow);
            int beats = Math.Max(1, (e - s + rpb - 1) / rpb);
            for (int b = 0; b < beats; b++)
            {
                int r = s + b * rpb;
                int note;
                if (b == 0) note = root;
                else if (b == beats - 1 && beats > 1) note = nextRoot + (nextRoot > root ? -1 : 1);
                else note = root + iv[Math.Min(iv.Length - 1, b % iv.Length == 0 ? 1 : b % iv.Length)];
                Hit(cells, r, note, inst, 0);
            }
        }

        // ---- 和音 ------------------------------------------------------------------

        /// <summary>0xy の値（構成音の 2 番目・3 番目、15 半音まで）。</summary>
        public static Fx ArpFx(Chord c)
        {
            var iv = c.Intervals;
            int x = iv.Length > 1 ? Math.Min(15, iv[1]) : 0;
            int y = iv.Length > 2 ? Math.Min(15, iv[2]) : (iv.Length > 1 ? 12 : 0);
            return new Fx(FxType.Arpeggio, (x << 4) | y);
        }

        public static Cell[] Chords(IReadOnlyList<Chord> chords, int length, int rpb, ChordStyle style, int inst = Song.InstSoft)
        {
            var cells = Blank(length);
            int n = chords.Count;
            if (n == 0 || style == ChordStyle.None) return cells;
            rpb = Math.Max(1, rpb);
            for (int i = 0; i < n; i++)
            {
                var c = chords[i];
                int s = ChordProgression.StartRow(i, n, length), e = ChordProgression.StartRow(i + 1, n, length);
                int root = Chord.Place(c.root, ChordLow);
                var iv = c.Intervals;
                switch (style)
                {
                    case ChordStyle.Arpeggio:
                        Hit(cells, s, root, inst, 0);
                        cells[s].fx1 = ArpFx(c);
                        break;
                    case ChordStyle.Offbeat:
                        for (int r = s + Half(rpb); r < e; r += rpb)
                        {
                            Hit(cells, r, root, inst, 1);
                            cells[r].fx1 = ArpFx(c);
                        }
                        break;
                    case ChordStyle.ArpUp:
                        for (int r = s, k = 0; r < e; r++, k++)
                        {
                            int t = k % (iv.Length + 1);
                            Hit(cells, r, t < iv.Length ? root + iv[t] : root + 12, inst, 0);
                        }
                        break;
                    case ChordStyle.Broken:
                        int[] order = { 0, 2, 1, 2 };
                        for (int r = s, k = 0; r < e; r += Half(rpb), k++)
                            Hit(cells, r, root + iv[Math.Min(iv.Length - 1, order[k % order.Length])], inst, 0);
                        break;
                }
            }
            return cells;
        }

        // ---- ドラム ----------------------------------------------------------------

        public static Cell[] Drums(int length, int rpb, DrumStyle style, bool fill)
        {
            var cells = Blank(length);
            if (style == DrumStyle.None) return cells;
            rpb = Math.Max(1, rpb);
            int bar = rpb * 4, half = Half(rpb);
            for (int r = 0; r < length; r++)
            {
                int inBar = r % bar, beat = inBar / rpb, sub = inBar % rpb;
                bool onBeat = sub == 0, eighth = sub % half == 0;
                int pitch = -1, inst = -1, vol = -1;
                bool kick = false, snare = false;
                switch (style)
                {
                    case DrumStyle.Basic:
                        kick = onBeat && (beat == 0 || beat == 2) || (beat == 2 && sub == half && half < rpb);
                        snare = onBeat && (beat == 1 || beat == 3);
                        break;
                    case DrumStyle.FourOnFloor:
                        kick = onBeat;
                        snare = false;
                        if (onBeat && (beat == 1 || beat == 3)) { kick = false; snare = true; }
                        break;
                    case DrumStyle.HalfTime:
                        kick = onBeat && beat == 0 || (beat == 1 && sub == half && half < rpb);
                        snare = onBeat && beat == 2;
                        break;
                    case DrumStyle.Breakbeat:
                        kick = onBeat && beat == 0 || (beat == 2 && sub == half && half < rpb);
                        snare = onBeat && (beat == 1 || beat == 3);
                        if (!snare && beat == 3 && sub == half && half < rpb) { snare = true; vol = 7; }
                        break;
                }
                if (kick) { pitch = KickPitch; inst = Song.InstKick; }
                else if (snare) { pitch = SnarePitch; inst = Song.InstSnare; }
                else if (eighth)
                {
                    pitch = HatPitch;
                    inst = Song.InstHat;
                    // 4 つ打ちは裏拍を強く
                    if (style == DrumStyle.FourOnFloor && onBeat) pitch = -1;
                }
                if (pitch >= 0) cells[r] = Cell.NoteCell(pitch, inst, vol);
            }

            // 最後の 1 拍をスネアのフィルに（2 小節以上あるとき）
            if (fill && length >= bar * 2)
            {
                int from = length - rpb;
                for (int r = from, k = 0; r < length; r++, k++)
                    cells[r] = Cell.NoteCell(SnarePitch, Song.InstSnare, Math.Min(15, 8 + k * 7 / Math.Max(1, rpb - 1)));
            }
            return cells;
        }
    }
}
