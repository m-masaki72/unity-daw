using System;
using System.Collections.Generic;

namespace UniDAW.Editor
{
    /// <summary>
    /// アレンジ表示の配置計算：順序表のエントリを時間軸（行）上のブロックに並べる。UI に依存しない。
    /// </summary>
    public static class ArrangeLayout
    {
        public struct Block
        {
            public int order, pattern, start, length, transpose;
            public int End => start + length;
        }

        public static List<Block> Blocks(Song song)
        {
            var list = new List<Block>();
            if (song == null) return list;
            int t = 0;
            for (int i = 0; i < song.order.Count; i++)
            {
                int pi = song.PatternAtOrder(i);
                int len = song.GetPattern(pi)?.length ?? 0;
                list.Add(new Block { order = i, pattern = pi, start = t, length = len, transpose = song.TransposeAtOrder(i) });
                t += len;
            }
            return list;
        }

        public static int TotalRows(List<Block> blocks) => blocks.Count == 0 ? 0 : blocks[blocks.Count - 1].End;

        /// <summary>行位置にあるブロックの番号（範囲外は -1）。</summary>
        public static int BlockAt(List<Block> blocks, float row)
        {
            for (int i = 0; i < blocks.Count; i++)
                if (row >= blocks[i].start && row < blocks[i].End) return i;
            return -1;
        }

        /// <summary>
        /// ドラッグで落とした位置から移動先の番号を決める（ブロックの中心より左なら前、右なら後ろ）。
        /// 戻り値は <see cref="OrderCommands.Move"/> にそのまま渡せる（取り除いた後の位置）。
        /// </summary>
        public static int DropIndex(List<Block> blocks, int from, float row)
        {
            int slot = SlotAt(blocks, row);
            return slot > from ? slot - 1 : slot;
        }

        /// <summary>行位置に落としたときの挿入位置（0..Count、取り除く前の番号）。</summary>
        public static int SlotAt(List<Block> blocks, float row)
        {
            for (int i = 0; i < blocks.Count; i++)
                if (row < blocks[i].start + blocks[i].length * 0.5f) return i;
            return blocks.Count;
        }

        /// <summary>行数を「小節.拍」で表す（4/4）。</summary>
        public static string BarBeat(int row, int rowsPerBeat)
        {
            int rpb = Math.Max(1, rowsPerBeat);
            int beat = row / rpb;
            return $"{beat / 4 + 1}.{beat % 4 + 1}";
        }

        /// <summary>行数を秒に（テンポ一定として）。</summary>
        public static double Seconds(int rows, Song song) =>
            song == null ? 0 : rows * 60.0 / (Math.Max(1f, song.bpm) * Math.Max(1, song.rowsPerBeat));
    }
}
