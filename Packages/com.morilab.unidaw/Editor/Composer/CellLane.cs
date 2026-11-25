using System;
using System.Collections.Generic;

namespace UniDAW.Editor
{
    public enum LaneKind { Volume, Effect }

    /// <summary>
    /// ピアノロール下のレーンが読み書きする値。トラッカーの音量列・エフェクト列と同じセルの値そのもの。
    /// エフェクトは種類を 1 つ選び、その種類が入っている列（無ければ空いている列）の値を扱う。
    /// </summary>
    public static class CellLane
    {
        /// <summary>レーンの値の上限（下限は 0、値なしは -1）。</summary>
        public static int Max(LaneKind kind, FxType fx)
        {
            if (kind == LaneKind.Volume) return 15;
            switch (fx)
            {
                case FxType.Duty: return 3;
                case FxType.EqLow:
                case FxType.EqMid:
                case FxType.EqHigh: return Fx.EqParamMax;
                case FxType.NoteCut:
                case FxType.NoteDelay: return 15;
                default: return 255;
            }
        }

        /// <summary>セルの値（無ければ -1）。</summary>
        public static int Get(in Cell c, LaneKind kind, FxType fx)
        {
            if (kind == LaneKind.Volume) return c.volume;
            if (c.fx1.type == fx) return c.fx1.param;
            if (c.fx2.type == fx) return c.fx2.param;
            return -1;
        }

        /// <summary>値を書く（負なら消す）。エフェクトは同じ種類の列 → 空いている列（second なら 2 列目を先に見る）→ 1 列目の順に書く。</summary>
        public static void Set(ref Cell c, LaneKind kind, FxType fx, int value, bool second = false)
        {
            if (kind == LaneKind.Volume)
            {
                c.volume = value < 0 ? -1 : Math.Min(15, value);
                return;
            }
            var v = value < 0 ? Fx.None : new Fx(fx, Math.Min(Max(kind, fx), value));
            if (c.fx1.type == fx) c.fx1 = v;
            else if (c.fx2.type == fx) c.fx2 = v;
            else if (value < 0) return;
            else if (second && c.fx2.IsEmpty) c.fx2 = v;
            else if (c.fx1.IsEmpty) c.fx1 = v;
            else if (c.fx2.IsEmpty) c.fx2 = v;
            else c.fx1 = v;
        }

        /// <summary>行ごとの値をまとめて書く（1 回の Undo）。行は表示上の行で、ポリメーターでは折り返した元のセルへ書く。</summary>
        public static void Apply(ComposerSession s, int ch, LaneKind kind, FxType fx, IReadOnlyDictionary<int, int> values,
            bool second = false, string undoName = null)
        {
            if (values == null || values.Count == 0) return;
            s.ModifyPattern(undoName ?? (kind == LaneKind.Volume ? "Volume Lane" : "Effect Lane"), p =>
            {
                foreach (var kv in values)
                {
                    if (kv.Key < 0 || kv.Key >= p.length) continue;
                    Set(ref p.tracks[ch].cells[p.CellIndex(ch, kv.Key)], kind, fx, kv.Value, second);
                }
            });
        }

        /// <summary>
        /// 補間（オートメーションのランプ）：row のセルに値があればそこから次の値まで、無ければ上下の値の間を直線で埋める値。
        /// 埋める行が無いときは null。行はトラック上のセル番号。
        /// </summary>
        public static Dictionary<int, int> Ramp(Pattern p, int ch, LaneKind kind, FxType fx, int row)
        {
            var cells = p.tracks[ch].cells;
            int tl = p.TrackLength(ch), a = -1, b = -1;
            row = p.CellIndex(ch, row);
            for (int r = row; r >= 0; r--)
                if (Get(cells[r], kind, fx) >= 0) { a = r; break; }
            for (int r = row + 1; r < tl; r++)
                if (Get(cells[r], kind, fx) >= 0) { b = r; break; }
            if (a < 0 || b < 0 || b - a < 2) return null;
            int va = Get(cells[a], kind, fx), vb = Get(cells[b], kind, fx);
            var values = new Dictionary<int, int>();
            for (int r = a + 1; r < b; r++)
                values[r] = (int)Math.Round(va + (vb - va) * (double)(r - a) / (b - a), MidpointRounding.AwayFromZero);
            return values;
        }
    }
}
