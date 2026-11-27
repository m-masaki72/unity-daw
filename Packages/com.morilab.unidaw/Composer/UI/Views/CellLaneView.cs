using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// ピアノロール下の音量／エフェクトのレーン。トラッカーの列と同じセルを棒グラフで描き、ドラッグで描き込む。
    /// 右ドラッグ（または Alt）で消す。ドラッグ 1 回が Undo 1 回。
    /// </summary>
    public sealed class CellLaneView : VisualElement
    {
        const float LaneH = 64f;
        static readonly FxType[] FxChoices =
        {
            FxType.Duty, FxType.Vibrato, FxType.VolumeSlide, FxType.Arpeggio, FxType.NoteCut, FxType.NoteDelay,
            FxType.SlideUp, FxType.SlideDown, FxType.Portamento,
            FxType.EqLow, FxType.EqMid, FxType.EqHigh, FxType.Duck,
        };

        readonly ComposerContext ctx;
        readonly SegmentedControl kindSeg;
        readonly DropdownField fxField;
        readonly Label hint;
        readonly ScrollView scroll;
        readonly VisualElement canvas;
        LaneKind kind;
        FxType fx = FxType.Duty;

        // ドラッグ中の描き込み（離したときにまとめて書く）
        Dictionary<int, int> pending;
        /// <summary>Shift ドラッグ：押した点から今の点までの直線（ランプ）だけを描く。</summary>
        bool line;
        int anchorRow, anchorValue;
        int lastRow = -1, lastValue, pointer;
        bool erasing;

        public CellLaneView(ComposerContext context)
        {
            ctx = context;
            AddToClassList("cd-lane");

            var head = new VisualElement();
            head.AddToClassList("cd-chhead");
            kindSeg = new SegmentedControl(new[] { "Vol", "Fx" }, i => { kind = (LaneKind)i; Refresh(); });
            kindSeg.AddToClassList("cd-roll-tools");
            head.Add(kindSeg);
            fxField = new DropdownField(new List<string>(Array.ConvertAll(FxChoices, FxLabel)), 0);
            fxField.RegisterValueChangedCallback(e => { fx = ParseLabel(e.newValue); Refresh(); });
            fxField.AddToClassList("cd-lane-fx");
            head.Add(fxField);
            hint = new Label("ドラッグ: 描く   Shift: 直線   右ドラッグ / Alt: 消す");
            hint.AddToClassList("cd-chinfo");
            head.Add(hint);
            Add(head);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var gutter = new VisualElement();
            gutter.style.width = 34;
            row.Add(gutter);
            scroll = new ScrollView(ScrollViewMode.Horizontal)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            scroll.style.flexGrow = 1;
            scroll.style.height = LaneH;
            canvas = new VisualElement();
            canvas.AddToClassList("cd-lane-canvas");
            canvas.style.height = LaneH;
            canvas.generateVisualContent += Draw;
            canvas.RegisterCallback<PointerDownEvent>(OnDown);
            canvas.RegisterCallback<PointerMoveEvent>(OnMove);
            canvas.RegisterCallback<PointerUpEvent>(OnUp);
            canvas.RegisterCallback<PointerCaptureOutEvent>(_ => Cancel());
            canvas.RegisterCallback<ContextClickEvent>(e => e.StopPropagation());
            scroll.Add(canvas);
            row.Add(scroll);
            Add(row);
        }

        static string FxLabel(FxType t) => FxText.TypeChar(t) + " " + FxText.Describe(t);
        static FxType ParseLabel(string label) => string.IsNullOrEmpty(label) ? FxType.Duty : FxText.TypeFromChar(label[0]);

        /// <summary>ピアノロールの横スクロールに合わせる。</summary>
        public void SyncScroll(float x) => scroll.scrollOffset = new Vector2(x, 0);

        public void Refresh()
        {
            var p = ctx.State.CurrentPattern;
            kindSeg.SetIndex((int)kind);
            fxField.style.display = kind == LaneKind.Effect ? DisplayStyle.Flex : DisplayStyle.None;
            fxField.tooltip = FxText.Help(fx);
            canvas.style.width = (p?.length ?? 0) * PianoRollView.CellW;
            canvas.MarkDirtyRepaint();
        }

        int Max => CellLane.Max(kind, fx);
        int RowAt(float x) => Mathf.FloorToInt(x / PianoRollView.CellW);
        int ValueAt(float y) => Mathf.Clamp(Mathf.RoundToInt((1f - y / LaneH) * Max), 0, Max);

        void OnDown(PointerDownEvent e)
        {
            ctx.FocusMain();
            e.StopPropagation();
            if (ctx.State.CurrentPattern == null || (e.button != 0 && e.button != 1)) return;
            erasing = e.button == 1 || e.altKey;
            line = e.shiftKey;
            pending = new Dictionary<int, int>();
            pointer = e.pointerId;
            canvas.CapturePointer(pointer);
            lastRow = -1;
            Paint(e.localPosition);
            anchorRow = lastRow;
            anchorValue = lastValue;
        }

        void OnMove(PointerMoveEvent e)
        {
            if (pending != null) Paint(e.localPosition);
        }

        void OnUp(PointerUpEvent e)
        {
            if (pending == null) return;
            var values = pending;
            Cancel();
            CellLane.Apply(ctx.Session, ctx.State.cursorCh, kind, fx, values);
        }

        void Cancel()
        {
            pending = null;
            if (canvas.HasPointerCapture(pointer)) canvas.ReleasePointer(pointer);
            canvas.MarkDirtyRepaint();
        }

        /// <summary>前の位置から今の位置まで、飛ばした行も直線で埋める。</summary>
        void Paint(Vector2 pos)
        {
            int len = ctx.State.CurrentPattern?.length ?? 0;
            int row = Mathf.Clamp(RowAt(pos.x), 0, Math.Max(0, len - 1));
            int value = erasing ? -1 : ValueAt(pos.y);
            if (line && lastRow >= 0)
            {
                pending.Clear();
                lastRow = anchorRow;
                lastValue = anchorValue;
            }
            if (lastRow < 0) pending[row] = value;
            else
            {
                int step = row >= lastRow ? 1 : -1;
                for (int r = lastRow; r != row + step; r += step)
                {
                    float t = row == lastRow ? 1f : (float)(r - lastRow) / (row - lastRow);
                    pending[r] = erasing ? -1 : Mathf.RoundToInt(Mathf.Lerp(lastValue, value, t));
                }
            }
            lastRow = row;
            lastValue = value;
            canvas.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var song = ctx.State.song;
            var pat = ctx.State.CurrentPattern;
            if (song == null || pat == null) return;
            var p = mgc.painter2D;
            int ch = ctx.State.cursorCh, len = pat.length, rpb = Math.Max(1, song.rowsPerBeat), max = Max;
            float w = PianoRollView.CellW;
            p.FillRect(0, 0, len * w, LaneH, new Color(0.14f, 0.14f, 0.16f));
            for (int r = 0; r <= len; r++)
                p.FillRect(r * w, 0, 1, LaneH, r % rpb == 0 ? new Color(0.32f, 0.32f, 0.36f) : new Color(0.2f, 0.2f, 0.23f));

            var cells = pat.tracks[ch].cells;
            for (int r = 0; r < len; r++)
            {
                var c = cells[pat.CellIndex(ch, r)];
                if (c.HasNote) p.FillRect(r * w + 1, LaneH - 3, w - 2, 3, new Color(0.45f, 0.75f, 1f, 0.5f)); // 発音位置
                int v = pending != null && pending.TryGetValue(r, out int pv) ? pv : CellLane.Get(c, kind, fx);
                if (v < 0)
                {
                    // 音量は省略時 15 として鳴るので、発音行には薄く示す
                    if (kind == LaneKind.Volume && c.HasNote) p.FillRect(r * w + 4, 0, w - 8, LaneH - 4, new Color(1f, 1f, 1f, 0.06f));
                    continue;
                }
                float h = Mathf.Max(2f, (LaneH - 4) * v / max);
                var col = kind == LaneKind.Volume ? new Color(0.45f, 0.95f, 0.55f, 0.85f) : new Color(1f, 0.7f, 0.3f, 0.85f);
                p.FillRect(r * w + 3, LaneH - 4 - h, w - 6, h, col);
            }
        }
    }
}
