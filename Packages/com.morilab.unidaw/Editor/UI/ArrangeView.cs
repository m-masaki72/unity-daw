using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// アレンジ表示（下ゾーン）：順序表を時間軸のブロックとして並べ、各ブロックに 4 チャンネルの音符を縮小表示する。
    /// クリックで選択、ダブルクリックでそこから再生、ドラッグで並べ替え、右クリックで複製・削除・移調など。
    /// </summary>
    public sealed class ArrangeView : VisualElement
    {
        const float RulerHeight = 18f, LaneTop = 20f, BlockHeight = 96f, DragThreshold = 4f;
        static readonly Color NoteColor = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color LaneLine = new Color(0f, 0f, 0f, 0.25f);
        static readonly Color[] ChannelColors =
        {
            new Color(0.55f, 0.85f, 1f, 0.85f), new Color(0.75f, 0.65f, 1f, 0.85f),
            new Color(1f, 0.8f, 0.45f, 0.85f), new Color(0.85f, 0.85f, 0.85f, 0.7f),
        };

        readonly ComposerContext ctx;
        readonly ScrollView scroll;
        readonly VisualElement canvas, ruler, playhead, dropMarker;
        readonly Label info;
        readonly List<VisualElement> blockViews = new List<VisualElement>();
        List<ArrangeLayout.Block> blocks = new List<ArrangeLayout.Block>();
        float zoom;

        // ドラッグ
        int dragIndex = -1;
        float dragStartX;
        bool dragging;

        ComposerSession S => ctx.Session;

        public ArrangeView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-arrange");

            var bar = new VisualElement();
            bar.AddToClassList("cd-arrange-bar");
            bar.Add(ToolButton("複製", "選択中のエントリを後ろに複製（同じパターン）", () => S.DuplicateOrderEntry(ctx.State.orderIndex)));
            bar.Add(ToolButton("削除", "選択中のエントリを順序表から外す（パターンは残る）", () => S.RemoveOrderEntryAt(ctx.State.orderIndex)));
            bar.Add(ToolButton("固有化", "このエントリのパターンを複製して、ここだけ別に編集できるようにする", () => S.MakeOrderUnique(ctx.State.orderIndex)));
            bar.Add(ToolButton("◀", "前へ移動", () => S.MoveOrderTo(ctx.State.orderIndex, ctx.State.orderIndex - 1)));
            bar.Add(ToolButton("▶", "後ろへ移動", () => S.MoveOrderTo(ctx.State.orderIndex, ctx.State.orderIndex + 1)));
            bar.Add(ToolButton("−", "縮小（Ctrl+ホイール）", () => S.SetArrangeZoom(ctx.State.arrangeZoom / 1.5f)));
            bar.Add(ToolButton("＋", "拡大（Ctrl+ホイール）", () => S.SetArrangeZoom(ctx.State.arrangeZoom * 1.5f)));
            info = new Label();
            info.AddToClassList("cd-arrange-info");
            bar.Add(info);
            Add(bar);

            scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.AddToClassList("cd-arrange-scroll");
            canvas = new VisualElement();
            canvas.AddToClassList("cd-arrange-canvas");
            ruler = new VisualElement();
            ruler.AddToClassList("cd-arrange-ruler");
            ruler.generateVisualContent += DrawRuler;
            canvas.Add(ruler);
            dropMarker = new VisualElement { pickingMode = PickingMode.Ignore };
            dropMarker.AddToClassList("cd-arrange-drop");
            playhead = new VisualElement { pickingMode = PickingMode.Ignore };
            playhead.AddToClassList("cd-arrange-playhead");
            scroll.Add(canvas);
            Add(scroll);
            scroll.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        static Button ToolButton(string text, string tip, System.Action click)
        {
            var b = new Button(click) { text = text, tooltip = tip };
            b.AddToClassList("cd-arrange-btn");
            return b;
        }

        void OnWheel(WheelEvent e)
        {
            if (!e.actionKey) return;
            S.SetArrangeZoom(ctx.State.arrangeZoom * (e.delta.y < 0 ? 1.25f : 0.8f));
            e.StopPropagation();
        }

        // =====================================================================
        // 再構築
        // =====================================================================

        public void Refresh()
        {
            var song = ctx.State.song;
            blocks = ArrangeLayout.Blocks(song);
            zoom = ctx.State.arrangeZoom;
            int total = ArrangeLayout.TotalRows(blocks);
            canvas.style.width = total * zoom + 40f;
            ruler.MarkDirtyRepaint();

            foreach (var v in blockViews) v.RemoveFromHierarchy();
            blockViews.Clear();
            if (song == null) return;
            for (int i = 0; i < blocks.Count; i++)
            {
                var v = BuildBlock(song, blocks[i], i);
                canvas.Add(v);
                blockViews.Add(v);
            }
            canvas.Add(dropMarker);
            canvas.Add(playhead);
            dropMarker.style.display = DisplayStyle.None;

            double sec = ArrangeLayout.Seconds(total, song);
            int bars = Mathf.CeilToInt(total / (float)(Mathf.Max(1, song.rowsPerBeat) * 4));
            info.text = $"{blocks.Count} エントリ · {bars} 小節 · {(int)sec / 60}:{(int)sec % 60:00}（テンポ一定として）";
            UpdatePlayhead();
        }

        VisualElement BuildBlock(Song song, ArrangeLayout.Block b, int index)
        {
            var v = new VisualElement();
            v.AddToClassList("cd-arrange-block");
            v.EnableInClassList("sel", index == ctx.State.orderIndex);
            v.style.left = b.start * zoom;
            v.style.width = Mathf.Max(2f, b.length * zoom - 1f);
            v.style.top = LaneTop;
            v.style.height = BlockHeight;
            v.style.backgroundColor = Color.HSVToRGB(b.pattern * 0.137f % 1f, 0.35f, 0.32f);
            var p = song.GetPattern(b.pattern);
            string tr = b.transpose != 0 ? $"  {b.transpose:+0;-0}" : "";
            var title = new Label($"{b.pattern:X2} {p?.name}{tr}") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("cd-arrange-title");
            v.Add(title);
            v.tooltip = $"順序 {index:X2}: パターン {b.pattern:X2} {p?.name}（{b.length} 行）{(b.transpose != 0 ? $"、移調 {b.transpose:+0;-0}" : "")}\n" +
                        "クリック：選択   ダブルクリック：ここから再生   ドラッグ：並べ替え   右クリック：メニュー";
            if (p != null) v.generateVisualContent += mgc => DrawNotes(mgc, v, p, b.transpose);

            v.RegisterCallback<PointerDownEvent>(e => OnBlockDown(e, v, index));
            v.RegisterCallback<PointerMoveEvent>(e => OnBlockMove(e, v));
            v.RegisterCallback<PointerUpEvent>(e => OnBlockUp(e, v, index));
            return v;
        }

        // =====================================================================
        // 描画
        // =====================================================================

        void DrawRuler(MeshGenerationContext mgc)
        {
            var song = ctx.State.song;
            if (song == null || zoom <= 0) return;
            var p = mgc.painter2D;
            int rpb = Mathf.Max(1, song.rowsPerBeat), bar = rpb * 4;
            int total = ArrangeLayout.TotalRows(blocks);
            // 小節の線が詰まりすぎないよう間引く
            int every = 1;
            while (bar * every * zoom < 28f) every *= 2;
            p.lineWidth = 1f;
            for (int r = 0, n = 0; r <= total; r += bar, n++)
            {
                if (n % every != 0) continue;
                float x = r * zoom + 0.5f;
                p.strokeColor = new Color(1f, 1f, 1f, 0.35f);
                p.BeginPath();
                p.MoveTo(new Vector2(x, 6f));
                p.LineTo(new Vector2(x, RulerHeight));
                p.Stroke();
                mgc.DrawText((n + 1).ToString(), new Vector2(x + 2f, 1f), 9f, new Color(0.75f, 0.75f, 0.82f));
            }
        }

        static void DrawNotes(MeshGenerationContext mgc, VisualElement v, Pattern p, int transpose)
        {
            var rect = v.contentRect;
            float top = 14f, h = rect.height - top - 2f, laneH = h / Song.ChannelCount;
            if (laneH <= 1f || p.length <= 0) return;
            float sx = rect.width / p.length;
            var pt = mgc.painter2D;
            pt.lineWidth = 1f;
            pt.strokeColor = LaneLine;
            for (int c = 1; c < Song.ChannelCount; c++)
            {
                pt.BeginPath();
                pt.MoveTo(new Vector2(0, top + c * laneH));
                pt.LineTo(new Vector2(rect.width, top + c * laneH));
                pt.Stroke();
            }
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var notes = MelodyModel.Extract(p, c);
                if (notes.Count == 0) continue;
                bool noise = Song.Kinds[c] == ChannelKind.Noise;
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (var n in notes)
                {
                    lo = Mathf.Min(lo, n.pitch);
                    hi = Mathf.Max(hi, n.pitch);
                }
                float span = Mathf.Max(1, hi - lo);
                float laneTop = top + c * laneH + 1f, usable = laneH - 3f;
                pt.fillColor = ChannelColors[c];
                foreach (var n in notes)
                {
                    float y = noise ? laneTop + usable * 0.5f : laneTop + usable * (1f - (n.pitch - lo) / span);
                    float x = n.start * sx, w = noise ? Mathf.Max(1f, sx * 0.6f) : Mathf.Max(1f, n.length * sx - 0.5f);
                    pt.BeginPath();
                    pt.MoveTo(new Vector2(x, y - 1f));
                    pt.LineTo(new Vector2(x + w, y - 1f));
                    pt.LineTo(new Vector2(x + w, y + 1f));
                    pt.LineTo(new Vector2(x, y + 1f));
                    pt.ClosePath();
                    pt.Fill();
                }
            }
        }

        /// <summary>毎フレーム：再生位置の線。</summary>
        public void UpdatePlayhead()
        {
            var e = ctx.Transport.Engine;
            float x = -1f;
            if (e != null && e.IsPlaying && e.PlayingPattern >= 0)
            {
                int order = e.IsPatternMode ? ctx.State.orderIndex : e.PlayingOrder;
                if (order >= 0 && order < blocks.Count && blocks[order].pattern == e.PlayingPattern)
                    x = (blocks[order].start + e.PlayingRow) * zoom;
            }
            playhead.style.display = x >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (x < 0) return;
            playhead.style.left = x;
            // 追従：見えている範囲を外れたら送る
            if (!ctx.State.follow) return;
            float view = scroll.contentViewport.layout.width, off = scroll.scrollOffset.x;
            if (view > 0 && (x < off || x > off + view - 20f)) scroll.scrollOffset = new Vector2(Mathf.Max(0, x - 20f), 0);
        }

        // =====================================================================
        // 操作
        // =====================================================================

        void OnBlockDown(PointerDownEvent e, VisualElement v, int index)
        {
            if (e.button == 1)
            {
                S.SelectOrder(index);
                ShowMenu(index);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                S.SelectOrder(index);
                ctx.Transport.PlayFromOrder(index);
                e.StopPropagation();
                return;
            }
            dragIndex = index;
            dragStartX = e.position.x;
            dragging = false;
            v.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnBlockMove(PointerMoveEvent e, VisualElement v)
        {
            if (dragIndex < 0 || !v.HasPointerCapture(e.pointerId)) return;
            float dx = e.position.x - dragStartX;
            if (!dragging && Mathf.Abs(dx) < DragThreshold) return;
            dragging = true;
            v.style.translate = new Translate(dx, 0);
            v.AddToClassList("drag");
            int to = DropSlot(e.position);
            float mx = to >= blocks.Count ? ArrangeLayout.TotalRows(blocks) * zoom : blocks[to].start * zoom;
            dropMarker.style.left = mx - 1f;
            dropMarker.style.display = DisplayStyle.Flex;
        }

        void OnBlockUp(PointerUpEvent e, VisualElement v, int index)
        {
            if (dragIndex < 0) return;
            if (v.HasPointerCapture(e.pointerId)) v.ReleasePointer(e.pointerId);
            bool moved = dragging;
            int from = dragIndex;
            dragIndex = -1;
            dragging = false;
            v.style.translate = new Translate(0, 0);
            v.RemoveFromClassList("drag");
            dropMarker.style.display = DisplayStyle.None;
            if (moved)
            {
                float row = canvas.WorldToLocal(e.position).x / Mathf.Max(0.01f, zoom);
                S.MoveOrderTo(from, ArrangeLayout.DropIndex(blocks, from, row));
            }
            else S.SelectOrder(index);
            e.StopPropagation();
        }

        /// <summary>ドロップ先の挿入位置（0..Count、取り除く前の番号）。</summary>
        int DropSlot(Vector2 worldPos)
        {
            float row = canvas.WorldToLocal(worldPos).x / Mathf.Max(0.01f, zoom);
            for (int i = 0; i < blocks.Count; i++)
                if (row < blocks[i].start + blocks[i].length * 0.5f) return i;
            return blocks.Count;
        }

        void ShowMenu(int index)
        {
            var song = ctx.State.song;
            var m = new GenericMenu();
            m.AddItem(new GUIContent("ここから再生"), false, () => ctx.Transport.PlayFromOrder(index));
            m.AddSeparator("");
            m.AddItem(new GUIContent("複製"), false, () => S.DuplicateOrderEntry(index));
            if (song.order.Count > 1) m.AddItem(new GUIContent("削除"), false, () => S.RemoveOrderEntryAt(index));
            else m.AddDisabledItem(new GUIContent("削除"));
            m.AddItem(new GUIContent("固有化（パターンを複製）"), false, () => S.MakeOrderUnique(index));
            m.AddSeparator("");
            for (int i = 0; i < song.patterns.Count; i++)
            {
                int pi = i;
                m.AddItem(new GUIContent($"パターン/{i:X2} {song.patterns[i].name}"), song.order[index] == i, () => S.SetOrderPattern(index, pi));
            }
            m.AddItem(new GUIContent("移調/+1"), false, () => S.TransposeOrderAt(index, 1));
            m.AddItem(new GUIContent("移調/-1"), false, () => S.TransposeOrderAt(index, -1));
            m.AddItem(new GUIContent("移調/+12"), false, () => S.TransposeOrderAt(index, 12));
            m.AddItem(new GUIContent("移調/-12"), false, () => S.TransposeOrderAt(index, -12));
            m.AddItem(new GUIContent("移調/リセット"), false, () => S.TransposeOrderAt(index, null));
            m.ShowAsContext();
        }
    }
}
