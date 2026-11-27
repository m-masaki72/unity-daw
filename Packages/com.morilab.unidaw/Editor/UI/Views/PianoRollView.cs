using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// ピアノロール表示（トラッカー／ステップと同じパターンデータの第3の見え方）。
    /// カーソルのあるチャンネルを 1 本ずつ表示し、Cubase / Studio One のキーエディタと同じ要領で編集する。
    /// 編集そのものは <see cref="RollEditor"/> が行い、ここはマウス操作の解釈を、描画は PianoRollView.Draw.cs が受け持つ。
    /// </summary>
    public partial class PianoRollView : VisualElement, IPatternView, ITooltipSource
    {
        public const float CellW = 18f, RowH = 12f;
        const int DefaultLo = 36, DefaultHi = 84, NoiseHi = 15;
        /// <summary>ノート右端のこの幅（px）を掴むと長さ変更。</summary>
        const float EdgeGrab = 5f;
        static readonly string[] ToolNames = { "Select", "Draw", "Erase" };

        enum Drag { None, Move, Resize, Marquee, Erase }

        readonly ComposerContext ctx;
        readonly IconButton[] chButtons = new IconButton[Song.ChannelCount];
        static readonly Glyph[] ChGlyphs = { Glyph.PulseWave, Glyph.PulseWave, Glyph.TriangleWave, Glyph.NoiseWave };
        static readonly string[] ChCaptions = { "PULSE 1", "PULSE 2", "TRIANGLE", "NOISE" };
        readonly SegmentedControl toolSeg;
        readonly Label info;
        readonly VisualElement keys;
        readonly VisualElement canvas;
        // 再生位置・カーソル列・範囲選択枠は重ね置きの要素にして、移動のたびにキャンバス全体を描き直さない
        readonly VisualElement playhead, cursorCol, marquee;
        readonly CellLaneView lane;
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

        public VisualElement HeaderTools { get; }

        public PianoRollView(ComposerContext context)
        {
            ctx = context;
            style.flexGrow = 1;

            // チャンネルとツールはエディタ見出しに並べる（HeaderTools）
            HeaderTools = new VisualElement();
            HeaderTools.AddToClassList("cd-roll-headtools");
            var chGroup = new VisualElement();
            chGroup.AddToClassList("cd-roll-chs");
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var b = new IconButton(ChGlyphs[c], ChCaptions[c], () => { ctx.Session.SetCursor(ctx.State.cursorRow, ch, 0); ctx.FocusMain(); },
                    $"{Song.ChannelNames[c]} を編集する");
                b.AddToClassList("cd-roll-ch");
                b.AddToClassList("ch" + c);
                chButtons[c] = b;
                chGroup.Add(b);
            }
            HeaderTools.Add(chGroup);
            toolSeg = new SegmentedControl(ToolNames, i => { ctx.Session.SetRollTool((RollTool)i); ctx.FocusMain(); });
            toolSeg.AddToClassList("cd-roll-tools");
            HeaderTools.Add(toolSeg);
            var head = new VisualElement();
            head.AddToClassList("cd-chhead");
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
            lane = new CellLaneView(ctx);
            Add(lane);
            scroll.horizontalScroller.valueChanged += lane.SyncScroll;
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

        public string TooltipAt(VisualElement picked, Vector2 worldPos)
        {
            if (picked != canvas || ctx.State.song == null || !NoteAt(canvas.WorldToLocal(worldPos), out var note, out _)) return null;
            return CellDescription.Describe(ctx.State.song, ctx.State.cursorCh, note.cell, note.length);
        }

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
            var m = new DawMenu();
            foreach (RollTool t in Enum.GetValues(typeof(RollTool)))
            {
                var tool = t;
                m.AddItem("Tool/" + DawKeys.ToolLabel(preset, tool), ctx.State.rollTool == tool, () => s.SetRollTool(tool));
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

        static void Item(DawMenu m, string label, bool enabled, System.Action action)
        {
            if (enabled) m.AddItem(label, false, action);
            else m.AddDisabledItem(label);
        }
    }
}
