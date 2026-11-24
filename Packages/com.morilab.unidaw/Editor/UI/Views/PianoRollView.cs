using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// ピアノロール表示（トラッカー／ステップと同じパターンデータの第3の見え方）。
    /// カーソルのあるチャンネルを 1 本ずつ表示し、Cubase / Studio One のキーエディタと同じ要領で編集する。
    /// 編集そのものは <see cref="RollEditor"/> が行い、ここはマウス操作の解釈と描画だけを受け持つ。
    /// </summary>
    public class PianoRollView : VisualElement, IPatternView
    {
        public const float CellW = 18f, RowH = 12f;
        const int DefaultLo = 36, DefaultHi = 84, NoiseHi = 15;
        /// <summary>ノート右端のこの幅（px）を掴むと長さ変更。</summary>
        const float EdgeGrab = 5f;
        static readonly string[] ToolNames = { "Select", "Draw", "Erase" };

        enum Drag { None, Move, Resize, Marquee, Erase }

        readonly ComposerContext ctx;
        readonly Button[] chButtons = new Button[Song.ChannelCount];
        readonly SegmentedControl toolSeg;
        readonly Label info;
        readonly VisualElement keys;
        readonly VisualElement canvas;
        // 再生位置・カーソル列・範囲選択枠は重ね置きの要素にして、移動のたびにキャンバス全体を描き直さない
        readonly VisualElement playhead, cursorCol, marquee;
        int lo = DefaultLo, hi = DefaultHi;
        (int lo, int hi, bool noise) keysBuilt = (-1, -1, false);

        // ---- ドラッグ中の状態
        Drag drag;
        Vector2 dragFrom;
        int dragRow, dragPitch, dragDeltaRow, dragDeltaPitch, dragPointer;
        bool dragCopy, dragAdditive;
        List<RollNote> preview;
        HashSet<int> previewSelection;

        static readonly Color[] ChColors =
        {
            new Color(0.35f, 0.75f, 1f), new Color(0.45f, 0.95f, 0.55f),
            new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.5f, 0.9f),
        };

        RollEditor Roll => ctx.Roll;

        public PianoRollView(ComposerContext context)
        {
            ctx = context;
            style.flexGrow = 1;

            var head = new VisualElement();
            head.AddToClassList("cd-chhead");
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var b = new Button(() => { ctx.Session.SetCursor(ctx.State.cursorRow, ch, 0); ctx.FocusMain(); }) { text = Song.ChannelNames[c] };
                b.AddToClassList("cd-ms");
                chButtons[c] = b;
                head.Add(b);
            }
            toolSeg = new SegmentedControl(ToolNames, i => { ctx.Session.SetRollTool((RollTool)i); ctx.FocusMain(); });
            toolSeg.AddToClassList("cd-roll-tools");
            head.Add(toolSeg);
            info = new Label();
            info.AddToClassList("cd-chinfo");
            head.Add(info);
            Add(head);

            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            keys = new VisualElement();
            keys.style.width = 34;
            content.Add(keys);
            canvas = new VisualElement();
            canvas.AddToClassList("cd-roll-canvas");
            canvas.generateVisualContent += Draw;
            canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            canvas.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            cursorCol = Overlay(CellW, new Color(1f, 1f, 1f, 0.07f));
            playhead = Overlay(2, new Color(1f, 0.85f, 0.2f, 0.9f));
            marquee = new VisualElement { pickingMode = PickingMode.Ignore };
            marquee.AddToClassList("cd-roll-marquee");
            marquee.style.position = Position.Absolute;
            marquee.style.display = DisplayStyle.None;
            canvas.Add(marquee);
            content.Add(canvas);
            scroll.Add(content);
            Add(scroll);
        }

        VisualElement Overlay(float width, Color color)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.top = 0;
            e.style.bottom = 0;
            e.style.width = width;
            e.style.backgroundColor = color;
            canvas.Add(e);
            return e;
        }

        bool IsNoise => Song.Kinds[ctx.State.cursorCh] == ChannelKind.Noise;

        /// <summary>座標 → (表示上の行, ピッチ)。行はトラック長で折り返す前の値。</summary>
        public bool HitTest(Vector2 local, out int row, out int pitch)
        {
            row = Mathf.FloorToInt(local.x / CellW);
            pitch = hi - Mathf.FloorToInt(local.y / RowH);
            var p = ctx.State.CurrentPattern;
            return p != null && row >= 0 && row < p.length && pitch >= lo && pitch <= hi;
        }

        /// <summary>表示上の行 → 編集対象の行（ポリメーターの折り返し部分は元の行）。</summary>
        int Wrap(int row)
        {
            int tl = Roll.TrackLength;
            return tl > 0 ? row % tl : 0;
        }

        /// <summary>座標にあるノートと、右端（長さ変更の取っ手）を掴んだか。</summary>
        bool NoteAt(Vector2 local, out RollNote note, out bool edge)
        {
            edge = false;
            note = default;
            if (!HitTest(local, out int row, out int pitch) || !Roll.TryGetNote(Wrap(row), pitch, out note)) return false;
            int offset = row - Wrap(row);
            float endX = (note.End + offset) * CellW;
            edge = endX - local.x <= EdgeGrab;
            return true;
        }

        // =====================================================================
        // マウス
        // =====================================================================

        void OnPointerDown(PointerDownEvent e)
        {
            ctx.FocusMain();
            e.StopPropagation();
            if (ctx.State.song == null || !HitTest(e.localPosition, out int row, out int pitch)) return;

            if (e.button == 1)
            {
                if (NoteAt(e.localPosition, out var hitNote, out _) && !Roll.IsSelected(hitNote.start)) Roll.Select(hitNote.start, false);
                ShowContextMenu(e.localPosition);
                return;
            }
            if (e.button != 0) return;

            bool hit = NoteAt(e.localPosition, out var note, out bool edge);
            var tool = ctx.State.rollTool;
            if (tool == RollTool.Erase)
            {
                if (hit) Roll.Delete(note.start);
                BeginDrag(Drag.Erase, e, row, pitch);
                return;
            }
            // ダブルクリック（選択ツール）／クリック（鉛筆）：ノートの上なら消す、空き地なら作って長さをドラッグで決める
            if (tool == RollTool.Draw || e.clickCount == 2)
            {
                if (hit)
                {
                    Roll.Delete(note.start);
                    return;
                }
                Roll.Create(Wrap(row), pitch, Math.Max(1, ctx.State.editStep));
                BeginDrag(Drag.Resize, e, row, pitch);
                return;
            }

            bool additive = e.shiftKey || e.actionKey;
            if (!hit)
            {
                dragAdditive = additive;
                if (!additive) Roll.ClearSelection();
                BeginDrag(Drag.Marquee, e, row, pitch);
                return;
            }
            if (additive && Roll.IsSelected(note.start))
            {
                Roll.ToggleSelect(note.start);
                return;
            }
            if (!Roll.IsSelected(note.start)) Roll.Select(note.start, additive);
            ctx.Session.SetCursor(note.start, ctx.State.cursorCh, 0);
            ctx.Transport.PreviewNote(ctx.State.cursorCh, note.pitch, ctx.Session.ChannelInstrument(ctx.State.cursorCh), 0.15);
            BeginDrag(edge ? Drag.Resize : Drag.Move, e, row, pitch);
        }

        void BeginDrag(Drag kind, IPointerEvent e, int row, int pitch)
        {
            drag = kind;
            dragFrom = e.localPosition;
            dragRow = row;
            dragPitch = pitch;
            dragDeltaRow = dragDeltaPitch = 0;
            dragCopy = false;
            dragPointer = e.pointerId;
            canvas.CapturePointer(dragPointer);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (drag == Drag.None)
            {
                // 取っ手の上ではカーソルの形で長さ変更を知らせる
                bool edgeHover = ctx.State.rollTool == RollTool.Select && NoteAt(e.localPosition, out _, out bool edge) && edge;
                canvas.EnableInClassList("resize", edgeHover);
                return;
            }
            var pos = (Vector2)e.localPosition;
            int row = Mathf.FloorToInt(pos.x / CellW);
            int pitch = hi - Mathf.FloorToInt(pos.y / RowH);
            switch (drag)
            {
                case Drag.Move:
                    dragDeltaRow = row - dragRow;
                    dragDeltaPitch = pitch - dragPitch;
                    dragCopy = e.altKey;
                    preview = Roll.PreviewMove(dragDeltaRow, dragDeltaPitch, dragCopy, out previewSelection);
                    canvas.MarkDirtyRepaint();
                    break;
                case Drag.Resize:
                    dragDeltaRow = Mathf.RoundToInt((pos.x - dragFrom.x) / CellW);
                    preview = Roll.PreviewResize(dragDeltaRow);
                    previewSelection = null;
                    canvas.MarkDirtyRepaint();
                    break;
                case Drag.Marquee:
                    var r = Rect.MinMaxRect(Mathf.Min(dragFrom.x, pos.x), Mathf.Min(dragFrom.y, pos.y), Mathf.Max(dragFrom.x, pos.x), Mathf.Max(dragFrom.y, pos.y));
                    marquee.style.display = DisplayStyle.Flex;
                    marquee.style.left = r.x;
                    marquee.style.top = r.y;
                    marquee.style.width = r.width;
                    marquee.style.height = r.height;
                    dragDeltaRow = row - dragRow;
                    dragDeltaPitch = pitch - dragPitch;
                    break;
                case Drag.Erase:
                    if (NoteAt(pos, out var note, out _)) Roll.Delete(note.start);
                    break;
            }
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (drag == Drag.None || e.button != 0) return;
            var kind = drag;
            int dr = dragDeltaRow, dp = dragDeltaPitch;
            bool copy = e.altKey || dragCopy;
            EndDrag();
            switch (kind)
            {
                case Drag.Move: Roll.MoveSelected(dr, dp, copy); break;
                case Drag.Resize: Roll.ResizeSelected(dr); break;
                case Drag.Marquee:
                    if (dr != 0 || dp != 0 || Vector2.Distance(dragFrom, e.localPosition) > 3)
                        Roll.SelectRect(Wrap(dragRow), Wrap(dragRow + dr), dragPitch, dragPitch + dp, dragAdditive);
                    break;
            }
        }

        void EndDrag()
        {
            if (drag == Drag.None) return;
            drag = Drag.None;
            preview = null;
            previewSelection = null;
            marquee.style.display = DisplayStyle.None;
            if (canvas.HasPointerCapture(dragPointer)) canvas.ReleasePointer(dragPointer);
            canvas.MarkDirtyRepaint();
        }

        void ShowContextMenu(Vector2 local)
        {
            var s = ctx.Session;
            bool hasSel = Roll.Selection.Count > 0;
            var preset = DawKeys.Preset;
            var m = new GenericMenu();
            foreach (RollTool t in Enum.GetValues(typeof(RollTool)))
            {
                var tool = t;
                m.AddItem(new GUIContent("Tool/" + DawKeys.ToolLabel(preset, tool)), ctx.State.rollTool == tool, () => s.SetRollTool(tool));
            }
            m.AddSeparator("");
            Item(m, "Cut  (Ctrl+X)", hasSel, Roll.Cut);
            Item(m, "Copy  (Ctrl+C)", hasSel, Roll.Copy);
            Item(m, "Paste  (Ctrl+V)", Roll.HasClipboard, Roll.Paste);
            Item(m, "Duplicate  (" + (preset == DawKeyPreset.StudioOne ? "D" : "Ctrl+D") + ")", hasSel, Roll.Duplicate);
            Item(m, "Delete  (Del)", hasSel, Roll.DeleteSelected);
            m.AddSeparator("");
            Item(m, "Select All  (Ctrl+A)", true, Roll.SelectAll);
            Item(m, "Deselect", hasSel, Roll.ClearSelection);
            m.DropDown(new Rect(canvas.LocalToWorld(local), Vector2.zero));
        }

        static void Item(GenericMenu m, string label, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled) m.AddItem(new GUIContent(label), false, action);
            else m.AddDisabledItem(new GUIContent(label));
        }

        // =====================================================================
        // 表示
        // =====================================================================

        public void Refresh()
        {
            var p = ctx.State.CurrentPattern;
            if (p == null) return;
            int ch = ctx.State.cursorCh;
            for (int c = 0; c < Song.ChannelCount; c++) chButtons[c].EnableInClassList("on", c == ch);
            toolSeg.SetIndex((int)ctx.State.rollTool);

            // 表示する音域：ノイズは 0..15、それ以外は C2..C6 を基本に中身が収まるよう広げる
            bool noise = IsNoise;
            if (noise)
            {
                lo = 0;
                hi = NoiseHi;
            }
            else
            {
                lo = DefaultLo;
                hi = DefaultHi;
                foreach (var c in p.tracks[ch].cells)
                {
                    if (!c.HasNote) continue;
                    lo = Math.Min(lo, c.note - 2);
                    hi = Math.Max(hi, c.note + 2);
                }
                lo = Math.Max(NoteText.MinNote, lo);
                hi = Math.Min(NoteText.MaxNote, hi);
            }

            int tl = p.TrackLength(ch);
            int sel = Roll.Selection.Count;
            info.text = (tl < p.length ? $"poly {tl}/{p.length}   " : "") + (sel > 0 ? $"{sel} 選択   " : "") +
                        "ダブルクリック: 作成/削除   ドラッグ: 移動 (Alt 複製)   右端: 長さ   右クリック: メニュー";

            if (keysBuilt != (lo, hi, noise)) RebuildKeys(noise);
            canvas.style.width = p.length * CellW;
            canvas.style.height = (hi - lo + 1) * RowH;
            PlaceOverlay(cursorCol, ctx.State.cursorRow, p.length);
            UpdatePlayhead();
            canvas.MarkDirtyRepaint();
        }

        void RebuildKeys(bool noise)
        {
            keysBuilt = (lo, hi, noise);
            keys.Clear();
            for (int n = hi; n >= lo; n--)
            {
                var l = new Label(noise ? n.ToString("X") : NoteText.Format(n, ChannelKind.Pulse));
                l.style.height = RowH;
                l.style.fontSize = 9;
                l.style.paddingTop = l.style.paddingBottom = 0;
                l.style.unityTextAlign = TextAnchor.MiddleRight;
                l.style.color = !noise && NoteText.IsBlackKey(n) ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.85f, 0.85f, 0.85f);
                keys.Add(l);
            }
        }

        public void UpdatePlayhead() => PlaceOverlay(playhead, ctx.Transport.PlayheadRow, ctx.State.CurrentPattern?.length ?? 0);

        static void PlaceOverlay(VisualElement e, int row, int len)
        {
            bool show = row >= 0 && row < len;
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) e.style.left = row * CellW;
        }

        static void Fill(Painter2D p, float x, float y, float wd, float ht, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + wd, y));
            p.LineTo(new Vector2(x + wd, y + ht));
            p.LineTo(new Vector2(x, y + ht));
            p.ClosePath();
            p.Fill();
        }

        float YOf(int pitch) => (hi - pitch) * RowH;

        void Draw(MeshGenerationContext mgc)
        {
            var song = ctx.State.song;
            var pat = ctx.State.CurrentPattern;
            if (song == null || pat == null) return;
            var p = mgc.painter2D;
            int ch = ctx.State.cursorCh;
            bool noise = IsNoise;
            int len = pat.length, tl = pat.TrackLength(ch);
            int rpb = Math.Max(1, song.rowsPerBeat);
            float width = len * CellW, height = (hi - lo + 1) * RowH;

            // 背景（黒鍵の段を暗く、C の段に線）。キーがあればスケール外を暗く・主音を明るく
            var key = song.Key;
            bool scaled = !noise && !key.IsChromatic;
            for (int n = lo; n <= hi; n++)
            {
                bool black = !noise && NoteText.IsBlackKey(n);
                Color bg = black ? new Color(0.13f, 0.13f, 0.15f) : new Color(0.18f, 0.18f, 0.2f);
                if (scaled)
                    bg = !key.Contains(n) ? new Color(0.1f, 0.1f, 0.115f)
                        : Scale.Pc(n - key.root) == 0 ? new Color(0.22f, 0.21f, 0.26f) : new Color(0.18f, 0.18f, 0.2f);
                Fill(p, 0, YOf(n), width, RowH, bg);
                if (!noise && n % 12 == 0) Fill(p, 0, YOf(n) + RowH - 1, width, 1, new Color(0.3f, 0.3f, 0.34f));
            }
            // コード進行：区間ごとに構成音の段を色付けし、区切りに線
            if (!noise)
            {
                var chords = ChordProgression.Parse(pat.chords, key, out _);
                for (int i = 0; i < chords.Count; i++)
                {
                    int s = ChordProgression.StartRow(i, chords.Count, len), e = ChordProgression.StartRow(i + 1, chords.Count, len);
                    for (int n = lo; n <= hi; n++)
                        if (chords[i].ContainsPc(n))
                            Fill(p, s * CellW, YOf(n), (e - s) * CellW, RowH,
                                Scale.Pc(n - chords[i].root) == 0 ? new Color(0.35f, 0.6f, 1f, 0.16f) : new Color(0.35f, 0.6f, 1f, 0.08f));
                    if (i > 0) Fill(p, s * CellW - 1, 0, 2, height, new Color(0.45f, 0.65f, 1f, 0.5f));
                }
            }
            // 縦線（拍は濃く）とポリメーターの折り返し部分
            for (int r = 0; r <= len; r++)
                Fill(p, r * CellW, 0, 1, height, r % rpb == 0 ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.24f, 0.24f, 0.27f));
            if (tl < len) Fill(p, tl * CellW, 0, (len - tl) * CellW, height, new Color(0f, 0f, 0f, 0.3f));
            if (tl <= 0) return;

            // ノート：ドラッグ中は移動・長さ変更後の姿を描く。折り返し部分は薄く繰り返す
            var notes = preview ?? Roll.Notes();
            var selected = previewSelection ?? Roll.Selection;
            var col = ChColors[ch % ChColors.Length];
            for (int offset = 0; offset < len; offset += tl)
            {
                foreach (var n in notes)
                {
                    int from = n.start + offset;
                    if (from >= len) continue;
                    int to = Math.Min(len, n.End + offset);
                    DrawNote(p, from, to, n, col, offset > 0, selected.Contains(n.start));
                }
            }
        }

        void DrawNote(Painter2D p, int from, int to, RollNote n, Color col, bool ghost, bool selected)
        {
            if (n.pitch < lo || n.pitch > hi) return;
            float y = YOf(n.pitch) + 1;
            float x = from * CellW + 1;
            float wd = (to - from) * CellW - 2;
            float ht = RowH - 2;
            float vol = (n.cell.volume >= 0 ? n.cell.volume : 15) / 15f;
            if (selected) Fill(p, x - 1, y - 1, wd + 2, ht + 2, new Color(1f, 1f, 1f, ghost ? 0.4f : 0.95f));
            var c = selected ? Color.Lerp(col, Color.white, 0.35f) : col;
            c.a = (ghost ? 0.4f : 1f) * (0.45f + 0.55f * vol);
            Fill(p, x, y, wd, ht, c);
            Fill(p, x, y, 3, ht, new Color(1f, 1f, 1f, 0.6f)); // 発音位置
            if (!n.cell.fx1.IsEmpty || !n.cell.fx2.IsEmpty) Fill(p, x + wd - 4, y + 1, 3, 3, new Color(1f, 1f, 1f, 0.9f)); // エフェクト付き
        }
    }
}
