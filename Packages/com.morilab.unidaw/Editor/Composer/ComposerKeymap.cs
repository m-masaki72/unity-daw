using System;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// キー入力 → 操作の対応表。UI イベントには依存せず KeyCode と修飾キーだけを受け取る
    /// （テストからも直接呼べる）。実際の編集は <see cref="CellCommands"/> / <see cref="ComposerSession"/> が行う。
    /// </summary>
    public sealed class ComposerKeymap
    {
        static readonly KeyCode[] LowerKeys =
        {
            KeyCode.Z, KeyCode.S, KeyCode.X, KeyCode.D, KeyCode.C, KeyCode.V, KeyCode.G, KeyCode.B, KeyCode.H, KeyCode.N, KeyCode.J, KeyCode.M,
            KeyCode.Comma, KeyCode.L, KeyCode.Period, KeyCode.Semicolon, KeyCode.Slash,
        };

        static readonly KeyCode[] UpperKeys =
        {
            KeyCode.Q, KeyCode.Alpha2, KeyCode.W, KeyCode.Alpha3, KeyCode.E, KeyCode.R, KeyCode.Alpha5, KeyCode.T, KeyCode.Alpha6, KeyCode.Y, KeyCode.Alpha7, KeyCode.U,
            KeyCode.I, KeyCode.Alpha9, KeyCode.O, KeyCode.Alpha0, KeyCode.P,
        };

        /// <summary>画面に出すキー操作の説明。</summary>
        public const string Help =
            "キー操作\n" +
            "Z〜M / Q〜P : ノート入力（2段鍵盤）\n" +
            "1 : ノートオフ   Del : 消去\n" +
            "0-9 A-F : 数値列（16進）\n" +
            "Fx列: 0 1 2 3 4 A V F S\n" +
            "Space : パターン再生/停止\n" +
            "Enter : 曲再生   Esc : 停止\n" +
            "[ ] : オクターブ   Tab : 次ch\n" +
            "Ctrl+↑↓ : 半音移調 (+Shift 1oct)\n" +
            "Ctrl+C / Ctrl+V : トラックコピー\n" +
            ".mid をドロップでインポート";

        const int PageRows = 16;

        readonly ComposerSession s;
        readonly CellCommands cells;

        public ComposerKeymap(ComposerSession session, CellCommands cells)
        {
            s = session;
            this.cells = cells;
        }

        /// <summary>鍵盤キーなら半音オフセット（0..28）、それ以外は -1。</summary>
        public static int PianoOffset(KeyCode k)
        {
            int i = Array.IndexOf(LowerKeys, k);
            if (i >= 0) return i;
            i = Array.IndexOf(UpperKeys, k);
            return i >= 0 ? 12 + i : -1;
        }

        static int HexDigit(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return k - KeyCode.Alpha0;
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad0;
            if (k >= KeyCode.A && k <= KeyCode.F) return 10 + (k - KeyCode.A);
            return -1;
        }

        /// <summary>キー → エフェクト種別（文字の対応は FxText の表に従う）。</summary>
        static FxType FxFromKey(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return FxText.TypeFromChar((char)('0' + (k - KeyCode.Alpha0)));
            if (k >= KeyCode.A && k <= KeyCode.Z) return FxText.TypeFromChar((char)('A' + (k - KeyCode.A)));
            return FxType.None;
        }

        /// <summary>キーを処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift)
        {
            if (s.Song == null) return false;
            return HandleGlobal(k, shift)
                || (ctrl ? HandleCtrl(k, shift) : HandleCursor(k) || HandleEdit(k));
        }

        /// <summary>再生・オクターブ・チャンネル/ページ移動（どの列でも共通）。</summary>
        bool HandleGlobal(KeyCode k, bool shift)
        {
            var st = s.State;
            int last = s.CurrentPattern.length - 1;
            switch (k)
            {
                case KeyCode.Space: s.Transport.TogglePatternPlay(); return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: s.Transport.PlaySong(); return true;
                case KeyCode.Escape: s.Transport.Stop(); return true;
                case KeyCode.LeftBracket: s.SetOctave(st.octave - 1); return true;
                case KeyCode.RightBracket: s.SetOctave(st.octave + 1); return true;
                case KeyCode.Tab:
                    s.SetCursor(st.cursorRow, (st.cursorCh + (shift ? Song.ChannelCount - 1 : 1)) % Song.ChannelCount, st.CellMode ? 0 : st.cursorCol);
                    return true;
                case KeyCode.PageUp: s.SetCursor(Math.Max(0, st.cursorRow - PageRows), st.cursorCh, st.cursorCol); return true;
                case KeyCode.PageDown: s.SetCursor(Math.Min(last, st.cursorRow + PageRows), st.cursorCh, st.cursorCol); return true;
                case KeyCode.Home: s.SetCursor(0, st.cursorCh, st.cursorCol); return true;
                case KeyCode.End: s.SetCursor(last, st.cursorCh, st.cursorCol); return true;
            }
            return false;
        }

        bool HandleCtrl(KeyCode k, bool shift)
        {
            switch (k)
            {
                case KeyCode.UpArrow: cells.Transpose(shift ? 12 : 1); return true;
                case KeyCode.DownArrow: cells.Transpose(shift ? -12 : -1); return true;
                case KeyCode.C: cells.CopyTrack(); return true;
                case KeyCode.V: cells.PasteTrack(); return true;
            }
            return false;
        }

        /// <summary>矢印キー。セル単位ビューは 左右 = 行 / 上下 = ch、トラッカーは 上下 = 行 / 左右 = 列。</summary>
        bool HandleCursor(KeyCode k)
        {
            var st = s.State;
            int len = s.CurrentPattern.length, row = st.cursorRow, ch = st.cursorCh, col = st.cursorCol;
            if (st.CellMode)
            {
                switch (k)
                {
                    case KeyCode.LeftArrow: s.SetCursor((row + len - 1) % len, ch, 0); return true;
                    case KeyCode.RightArrow: s.SetCursor((row + 1) % len, ch, 0); return true;
                    case KeyCode.UpArrow: s.SetCursor(row, Math.Max(0, ch - 1), 0); return true;
                    case KeyCode.DownArrow: s.SetCursor(row, Math.Min(Song.ChannelCount - 1, ch + 1), 0); return true;
                }
                return false;
            }
            switch (k)
            {
                case KeyCode.UpArrow: s.SetCursor((row + len - 1) % len, ch, col); return true;
                case KeyCode.DownArrow: s.SetCursor((row + 1) % len, ch, col); return true;
                case KeyCode.LeftArrow:
                    if (col > 0) s.SetCursor(row, ch, col - 1);
                    else if (ch > 0) s.SetCursor(row, ch - 1, ComposerState.TrackerColumns - 1);
                    return true;
                case KeyCode.RightArrow:
                    if (col < ComposerState.TrackerColumns - 1) s.SetCursor(row, ch, col + 1);
                    else if (ch < Song.ChannelCount - 1) s.SetCursor(row, ch + 1, 0);
                    return true;
            }
            return false;
        }

        /// <summary>削除と、カーソル列に応じた入力（ノート / Fx 種別 / 16 進）。</summary>
        bool HandleEdit(KeyCode k)
        {
            int col = s.State.CellMode ? 0 : s.State.cursorCol;
            if (k == KeyCode.Delete || k == KeyCode.Backspace)
            {
                cells.Delete(col);
                return true;
            }

            if (col == 0)
            {
                if (k == KeyCode.Alpha1)
                {
                    cells.EnterNote(Cell.Off);
                    return true;
                }
                int off = PianoOffset(k);
                if (off < 0) return false;
                cells.EnterPianoKey(off);
                return true;
            }

            if (col == 3 || col == 5)
            {
                var t = FxFromKey(k);
                if (t == FxType.None) return false;
                cells.SetFxType(col, t);
                return true;
            }

            int d = HexDigit(k);
            if (d < 0) return false;
            cells.TypeHex(col, d);
            return true;
        }
    }
}
