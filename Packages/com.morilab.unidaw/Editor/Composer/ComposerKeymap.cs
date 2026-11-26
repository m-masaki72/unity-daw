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

        /// <summary>画面に出すキー操作の説明（ピアノロールの割り当てはプリセットで変わる）。</summary>
        public static string Help(DawKeyPreset preset)
        {
            bool s1 = preset == DawKeyPreset.StudioOne;
            return
                "キー操作（" + DawKeys.DisplayName(preset) + " 準拠）\n" +
                "Space : 再生/停止   Enter : 再生\n" +
                "Num0 / Esc : 停止   Num. : 先頭へ\n" +
                "Num/ : サイクル   Num* : 録音\n" +
                "Alt+C : メトロノーム   F1 : この一覧\n" +
                "F3 : ミキサー   Ctrl+Alt+E : 下ゾーン   Ctrl+Alt+A : アレンジ帯\n" +
                "Ctrl+Alt+L / R : 左 / 右ゾーン\n" +
                "Num- / Num+ : 前/次の順序\n" +
                "\n順序表（どのビューでも）\n" +
                "Alt+Ins : 追加   Alt+Del : 外す\n" +
                "Alt+PgUp / PgDn : 前後へ移動\n" +
                "Alt+[ ] : パターン番号 ±1\n" +
                "Ctrl+Alt+↑↓ : 移調 (+Shift 1oct)\n" +
                "Ctrl+Alt+←→ : Claude 候補の切替\n" +
                "\nトラッカー / ステップ\n" +
                "Z〜M / Q〜P : ノート入力（2段鍵盤）\n" +
                "1 : ノートオフ   Del : 消去\n" +
                "0-9 A-F : 数値列（16進）\n" +
                "Fx列: 0 1 2 3 4 A V F S\n" +
                "[ ] : オクターブ   Tab : 次ch\n" +
                "Ctrl+↑↓ : 半音移調 (+Shift 1oct)\n" +
                "Ctrl+C / Ctrl+V : トラックコピー\n" +
                "Ctrl+I : 補間（音量列・Fx列の上下の値の間を直線で）\n" +
                "\nピアノロール\n" +
                "ダブルクリック : 作成 / 削除\n" +
                "ドラッグ : 移動（Alt で複製）・右端で長さ\n" +
                "空き地ドラッグ : 範囲選択\n" +
                (s1 ? "1 矢印 / 4 消しゴム / 5 ペイント\n" : "1 選択 / 5 消しゴム / 8 鉛筆\n") +
                "↑↓ : 移調 (+Shift 1oct)   ←→ : 前後のノート\n" +
                (s1 ? "Alt+←→ : ナッジ   D : 複製   Ctrl+D : 選択解除\n" : "Ctrl+←→ : ナッジ   Ctrl+D : 複製\n") +
                "Ctrl+A / C / X / V   Del : 削除\n" +
                "\n.mid をドロップでインポート\n" +
                "再生・テンキー・F1 / F3・Alt+C・Ctrl+Alt+E / A / L / R は\nウィンドウ内のどこでも効く（Edit > Shortcuts の UniDAW で変更可）";
        }

        const int PageRows = 16;

        readonly ComposerSession s;
        readonly CellCommands cells;
        readonly RollEditor roll;
        readonly OrderCommands order;
        readonly ClaudeAssist claude;

        public ComposerKeymap(ComposerSession session, CellCommands cells, RollEditor roll, OrderCommands order = null, ClaudeAssist claude = null)
        {
            s = session;
            this.cells = cells;
            this.roll = roll;
            this.order = order;
            this.claude = claude;
        }

        /// <summary>
        /// ウィンドウ全体で効かせるキー（再生・トランスポート・表示の開閉）。エディタでは Shortcut Manager に登録され
        /// （<c>UniDAWShortcuts</c>）、ウィンドウ内のどこにフォーカスがあっても効く。割り当ては Edit &gt; Shortcuts で変えられる。
        /// </summary>
        public static readonly (KeyCode key, bool ctrl, bool alt)[] WindowCommands =
        {
            (KeyCode.Space, false, false), (KeyCode.KeypadEnter, false, false),
            (KeyCode.Keypad0, false, false), (KeyCode.KeypadPeriod, false, false), (KeyCode.KeypadDivide, false, false),
            (KeyCode.KeypadMultiply, false, false), (KeyCode.KeypadMinus, false, false), (KeyCode.KeypadPlus, false, false),
            (KeyCode.F1, false, false), (KeyCode.F3, false, false), (KeyCode.C, false, true),
            (KeyCode.E, true, true), (KeyCode.A, true, true), (KeyCode.L, true, true), (KeyCode.R, true, true),
        };

        /// <summary>既定の割り当てでウィンドウ全体のキーか（Shift 付きは対象外）。</summary>
        public static bool IsWindowCommand(KeyCode k, bool ctrl, bool shift, bool alt) =>
            !shift && Array.IndexOf(WindowCommands, (k, ctrl, alt)) >= 0;

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
            // テンキー 0 は DAW と同じく「停止」に使うので 1..9 だけ
            if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad0;
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
        public bool HandleKey(KeyCode k, bool ctrl, bool shift, bool alt = false)
        {
            // F1 = キー操作の一覧（Song が無くても開ける）。開いている間の Esc は一覧を閉じるだけ
            if (k == KeyCode.F1)
            {
                s.SetKeyHelp(!s.State.keyHelpOpen);
                return true;
            }
            if (k == KeyCode.Escape && s.State.keyHelpOpen)
            {
                s.SetKeyHelp(false);
                return true;
            }
            if (s.Song == null) return false;
            if (HandleZone(k, ctrl, alt) || alt && HandleOrder(k, ctrl, shift)) return true;
            if (HandleTransport(k) || HandleGlobal(k, shift)) return true;
            if (s.State.view == PatternViewKind.PianoRoll) return HandleRoll(k, ctrl, shift, alt);
            return ctrl ? HandleCtrl(k, shift) : HandleCursor(k) || HandleEdit(k);
        }

        /// <summary>トランスポート。Cubase / Studio One 共通のテンキー配置に合わせる。</summary>
        bool HandleZone(KeyCode k, bool ctrl, bool alt)
        {
            // F3 = ミキサー（Cubase / Studio One 共通）、Ctrl+Alt+E = 下ゾーンの開閉（Cubase）
            if (k == KeyCode.F3)
            {
                s.ToggleLowerTab(LowerTab.Mixer);
                return true;
            }
            if (ctrl && alt)
            {
                switch (k)
                {
                    case KeyCode.E: s.ToggleLowerZone(); return true;
                    case KeyCode.A: s.ToggleBandZone(); return true;
                    case KeyCode.L: s.ToggleLeftZone(); return true;
                    case KeyCode.R: s.ToggleRightZone(); return true;
                }
            }
            // Alt+C = メトロノーム（Cubase の C はノート入力と重なるので Alt 付き）
            if (k == KeyCode.C && alt && !ctrl)
            {
                s.Transport.ToggleMetronome();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Alt 付き：順序表（アレンジ）の編集と Claude 候補の切替。どのビューでも同じキーで、
        /// トラッカーの入力キーやピアノロールのナッジ（Ctrl / Alt+←→）とは修飾キーの組で区別する。
        /// </summary>
        bool HandleOrder(KeyCode k, bool ctrl, bool shift)
        {
            if (ctrl)
            {
                switch (k)
                {
                    case KeyCode.UpArrow: order?.TransposeSelected(shift ? 12 : 1); return order != null;
                    case KeyCode.DownArrow: order?.TransposeSelected(shift ? -12 : -1); return order != null;
                    case KeyCode.LeftArrow:
                    case KeyCode.RightArrow:
                        if (claude == null) return false;
                        claude.Choose(claude.Chosen + (k == KeyCode.LeftArrow ? -1 : 1));
                        return true;
                }
                return false;
            }
            if (order == null) return false;
            switch (k)
            {
                case KeyCode.Insert: order.Insert(); return true;
                case KeyCode.Delete: order.RemoveSelected(); return true;
                case KeyCode.PageUp: order.MoveSelected(-1); return true;
                case KeyCode.PageDown: order.MoveSelected(1); return true;
                case KeyCode.LeftBracket: order.ShiftSelectedPattern(-1); return true;
                case KeyCode.RightBracket: order.ShiftSelectedPattern(1); return true;
            }
            return false;
        }

        bool HandleTransport(KeyCode k)
        {
            var t = s.Transport;
            switch (k)
            {
                case KeyCode.Space: t.TogglePlay(); return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: t.Start(); return true;
                case KeyCode.Keypad0:
                case KeyCode.Escape: t.Stop(); return true;
                case KeyCode.KeypadPeriod: s.GoToStart(); return true;
                case KeyCode.KeypadDivide: t.ToggleLoop(); return true;
                case KeyCode.KeypadMultiply: s.SetRecording(!s.State.recording); return true;
                case KeyCode.KeypadMinus: s.StepOrder(-1); return true;
                case KeyCode.KeypadPlus: s.StepOrder(1); return true;
            }
            return false;
        }

        /// <summary>オクターブ・チャンネル/ページ移動（どのビューでも共通）。</summary>
        bool HandleGlobal(KeyCode k, bool shift)
        {
            var st = s.State;
            int last = s.CurrentPattern.length - 1;
            switch (k)
            {
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

        /// <summary>ピアノロール：DAW のキーエディタと同じ編集キー。ノート入力の文字キーは使わない。</summary>
        bool HandleRoll(KeyCode k, bool ctrl, bool shift, bool alt)
        {
            var preset = DawKeys.Preset;
            if (DawKeys.IsDeselect(preset, k, ctrl))
            {
                roll.ClearSelection();
                return true;
            }
            if (DawKeys.IsDuplicate(preset, k, ctrl))
            {
                roll.Duplicate();
                return true;
            }
            int semis = shift ? 12 : 1;
            switch (k)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace: roll.DeleteSelected(); return true;
                case KeyCode.UpArrow: roll.MoveSelected(0, semis, false); return true;
                case KeyCode.DownArrow: roll.MoveSelected(0, -semis, false); return true;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    int dir = k == KeyCode.LeftArrow ? -1 : 1;
                    if (DawKeys.IsNudgeModifier(preset, ctrl, alt)) roll.MoveSelected(dir, 0, false);
                    else roll.SelectNeighbor(dir, shift);
                    return true;
            }
            if (ctrl)
            {
                switch (k)
                {
                    case KeyCode.A: roll.SelectAll(); return true;
                    case KeyCode.C: roll.Copy(); return true;
                    case KeyCode.X: roll.Cut(); return true;
                    case KeyCode.V: roll.Paste(); return true;
                }
                return false;
            }
            var tool = DawKeys.ToolForKey(preset, k);
            if (tool == null) return false;
            s.SetRollTool(tool.Value);
            return true;
        }

        bool HandleCtrl(KeyCode k, bool shift)
        {
            switch (k)
            {
                case KeyCode.UpArrow: cells.Transpose(shift ? 12 : 1); return true;
                case KeyCode.DownArrow: cells.Transpose(shift ? -12 : -1); return true;
                case KeyCode.C: cells.CopyTrack(); return true;
                case KeyCode.V: cells.PasteTrack(); return true;
                case KeyCode.I: cells.Ramp(s.State.CellMode ? 2 : s.State.cursorCol); return true;
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
