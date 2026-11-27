using System.Text;

namespace UniDAW.Composer
{
    /// <summary>セルの中身を文章にする（トラッカーのセル・ピアノロールのノートのツールチップ用）。</summary>
    public static class CellDescription
    {
        /// <summary>空のセルは null。<paramref name="length"/> は 0 以上ならノートの長さ（行）として添える。</summary>
        public static string Describe(Song song, int ch, Cell cell, int length = -1)
        {
            if (cell.IsBlank && !cell.HasLocks && length < 0) return null;
            var kind = Song.Kinds[ch];
            var sb = new StringBuilder();
            if (cell.HasNote || cell.IsOff)
            {
                sb.Append(cell.IsOff ? "ノートオフ" : NoteText.Format(cell.note, kind));
                if (length > 0) sb.Append("　長さ ").Append(length).Append(" 行");
                sb.Append('\n');
            }
            if (cell.instrument >= 0)
            {
                var inst = song?.GetInstrument(cell.instrument);
                sb.Append("楽器 ").Append(cell.instrument.ToString("X2"));
                if (inst != null) sb.Append(' ').Append(inst.name);
                sb.Append('\n');
            }
            if (cell.volume >= 0) sb.Append("音量 ").Append(cell.volume.ToString("X")).Append(" / F\n");
            AppendFx(sb, cell.fx1);
            AppendFx(sb, cell.fx2);
            if (cell.probability < 100) sb.Append("発音確率 ").Append(cell.probability).Append("%\n");
            if (cell.HasCondition) sb.Append("条件 ").Append(cell.condA).Append(':').Append(cell.condB)
                .Append("（").Append(cell.condB).Append(" 回に 1 回、").Append(cell.condA).Append(" 回目だけ鳴らす）\n");
            return sb.Length == 0 ? null : sb.ToString().TrimEnd('\n');
        }

        static void AppendFx(StringBuilder sb, Fx fx)
        {
            if (fx.IsEmpty) return;
            sb.Append(FxText.Format(fx)).Append(" = ").Append(FxText.Describe(fx.type)).Append('\n');
        }
    }
}
