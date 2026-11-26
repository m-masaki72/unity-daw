using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// アレンジ帯（編集エリアの上）：順序表を時間軸のブロックとして並べ、各ブロックに 4 チャンネルの音符を縮小表示する。
    /// クリックで選択、ダブルクリックでそこから再生、ドラッグで並べ替え、右クリックで挿入・複製・削除・パターン変更・移調など。
    /// 並びが変わったときだけブロックを作り直し、選択の変化は枠の色、音符の変化はそのパターンのブロックの再描画だけで済ませる。
    /// </summary>
    public sealed class ArrangeView : VisualElement
    {
        const float LaneTop = 20f, DragThreshold = 4f;
        static readonly Color LaneLine = new Color(0f, 0f, 0f, 0.25f);
        static readonly Color[] ChannelColors =
        {
            new Color(0.55f, 0.85f, 1f, 0.85f), new Color(0.75f, 0.65f, 1f, 0.85f),
            new Color(1f, 0.8f, 0.45f, 0.85f), new Color(0.85f, 0.85f, 0.85f, 0.7f),
        };

        /// <summary>パターンごとの縮小表示用の音符（内容のハッシュが変わったときだけ取り直す）。</summary>
        sealed class MiniNotes
        {
            public int hash;
            public List<MelodyModel.Note>[] notes = new List<MelodyModel.Note>[Song.ChannelCount];
            public int[] lo = new int[Song.ChannelCount], hi = new int[Song.ChannelCount];
        }

        readonly ComposerContext ctx;
        readonly ScrollView scroll;
        readonly VisualElement canvas, ruler, playhead, dropMarker;
        readonly Label info;
        readonly List<VisualElement> blockViews = new List<VisualElement>();
        readonly Dictionary<int, MiniNotes> notesCache = new Dictionary<int, MiniNotes>();
        List<ArrangeLayout.Block> blocks = new List<ArrangeLayout.Block>();
        float zoom;
        Song builtSong;
        string builtKey;
        int selected = -1;

        // ドラッグ
        int dragIndex = -1;
        float dragStartX;
        bool dragging;

        ComposerSession S => ctx.Session;
        OrderCommands O => ctx.Order;

        public ArrangeView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-arrange");

            var bar = new VisualElement();
            bar.AddToClassList("cd-arrange-bar");
            var title = new Label("ARRANGE");
            title.AddToClassList("cd-arrange-name");
            bar.Add(title);
            bar.Add(ToolButton("Ins", "現在のパターンを選択中のエントリの後ろに挿入（Alt+Insert）", O.Insert));
            bar.Add(ToolButton("Dup", "選択中のエントリを後ろに複製（同じパターン）", () => O.Duplicate(ctx.State.orderIndex)));
            bar.Add(ToolButton("Del", "選択中のエントリを順序表から外す（パターンは残る、Alt+Delete）", O.RemoveSelected));
            bar.Add(ToolButton("Unique", "このエントリのパターンを複製して、ここだけ別に編集できるようにする", () => O.MakeUnique(ctx.State.orderIndex)));
            bar.Add(ToolButton("◀", "前へ移動（Alt+PageUp）", () => O.MoveSelected(-1)));
            bar.Add(ToolButton("▶", "後ろへ移動（Alt+PageDown）", () => O.MoveSelected(1)));
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
            // 帯の高さに合わせてブロックを伸び縮みさせる
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => FitHeight());
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

        void FitHeight()
        {
            float h = scroll.contentViewport.layout.height;
            if (h > 0 && !float.IsNaN(h)) canvas.style.height = h;
        }

        // =====================================================================
        // 更新
        // =====================================================================

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null)
            {
                Clear(null);
                return;
            }
            var next = ArrangeLayout.Blocks(song);
            string key = StructureKey(song, next, ctx.State.arrangeZoom);
            if (song != builtSong || key != builtKey) Rebuild(song, next, key);

            // 音符：内容が変わったパターンのブロックだけ描き直す
            for (int pi = 0; pi < song.patterns.Count; pi++)
            {
                if (!UpdateNotes(pi, song.patterns[pi])) continue;
                for (int i = 0; i < blocks.Count; i++)
                    if (blocks[i].pattern == pi) blockViews[i].MarkDirtyRepaint();
            }

            // 選択：枠の色だけ
            int sel = ctx.State.orderIndex;
            if (sel != selected)
            {
                if (selected >= 0 && selected < blockViews.Count) blockViews[selected].RemoveFromClassList("sel");
                if (sel >= 0 && sel < blockViews.Count)
                {
                    blockViews[sel].AddToClassList("sel");
                    if (!dragging && scroll.panel != null) scroll.ScrollTo(blockViews[sel]);
                }
                selected = sel;
            }
            UpdatePlayhead();
        }

        /// <summary>ブロックの作り直しが要る変化（並び・長さ・移調・名前・倍率）を 1 本の文字列にまとめる。</summary>
        static string StructureKey(Song song, List<ArrangeLayout.Block> blocks, float zoom)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(zoom).Append('|').Append(song.rowsPerBeat).Append('|').Append(song.bpm);
            foreach (var b in blocks)
                sb.Append('|').Append(b.pattern).Append(',').Append(b.length).Append(',').Append(b.transpose).Append(',').Append(song.GetPattern(b.pattern)?.name);
            return sb.ToString();
        }

        void Clear(Song song)
        {
            foreach (var v in blockViews) v.RemoveFromHierarchy();
            blockViews.Clear();
            blocks = new List<ArrangeLayout.Block>();
            notesCache.Clear();
            builtSong = song;
            builtKey = null;
            selected = -1;
            info.text = "";
            playhead.style.display = DisplayStyle.None;
        }

        void Rebuild(Song song, List<ArrangeLayout.Block> next, string key)
        {
            if (song != builtSong) notesCache.Clear();
            foreach (var v in blockViews) v.RemoveFromHierarchy();
            blockViews.Clear();
            blocks = next;
            builtSong = song;
            builtKey = key;
            selected = -1;
            zoom = ctx.State.arrangeZoom;
            int total = ArrangeLayout.TotalRows(blocks);
            canvas.style.width = total * zoom + 40f;
            ruler.MarkDirtyRepaint();
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
            info.text = $"{blocks.Count} entries · {bars} bars · {(int)sec / 60}:{(int)sec % 60:00}";
            info.tooltip = "エントリ数・小節数・長さ（テンポ一定として）";
        }

        /// <summary>パターンの音符を取り直す。変わっていれば true。</summary>
        bool UpdateNotes(int index, Pattern p)
        {
            int hash = NotesHash(p);
            if (notesCache.TryGetValue(index, out var m) && m.hash == hash) return false;
            if (m == null) notesCache[index] = m = new MiniNotes();
            m.hash = hash;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var notes = m.notes[c] = MelodyModel.Extract(p, c);
                int lo = int.MaxValue, hi = int.MinValue;
                foreach (var n in notes)
                {
                    lo = Mathf.Min(lo, n.pitch);
                    hi = Mathf.Max(hi, n.pitch);
                }
                m.lo[c] = lo;
                m.hi[c] = hi;
            }
            return true;
        }

        /// <summary>縮小表示に関わる内容（長さ・ノート）のハッシュ。</summary>
        static int NotesHash(Pattern p)
        {
            unchecked
            {
                int h = p.length;
                foreach (var t in p.tracks)
                {
                    h = h * 31 + t.length;
                    var cells = t.cells;
                    for (int i = 0; i < cells.Length; i++) h = h * 31 + cells[i].note;
                }
                return h;
            }
        }

        VisualElement BuildBlock(Song song, ArrangeLayout.Block b, int index)
        {
            var v = new VisualElement();
            v.AddToClassList("cd-arrange-block");
            v.style.left = b.start * zoom;
            v.style.width = Mathf.Max(2f, b.length * zoom - 1f);
            v.style.top = LaneTop;
            v.style.backgroundColor = Color.HSVToRGB(b.pattern * 0.137f % 1f, 0.35f, 0.32f);
            var p = song.GetPattern(b.pattern);
            string tr = b.transpose != 0 ? $"  {b.transpose:+0;-0}" : "";
            var title = new Label($"{b.pattern:X2} {p?.name}{tr}") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("cd-arrange-title");
            v.Add(title);
            v.tooltip = $"順序 {index:X2}: パターン {b.pattern:X2} {p?.name}（{b.length} 行）{(b.transpose != 0 ? $"、移調 {b.transpose:+0;-0}" : "")}\n" +
                        "クリック：選択   ダブルクリック：ここから再生   ドラッグ：並べ替え   右クリック：メニュー";
            int pi = b.pattern;
            v.generateVisualContent += mgc => DrawNotes(mgc, v, pi);

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
                p.Line(new Vector2(x, 6f), new Vector2(x, LaneTop - 2f), new Color(1f, 1f, 1f, 0.35f));
                mgc.DrawText((n + 1).ToString(), new Vector2(x + 2f, 1f), 9f, new Color(0.75f, 0.75f, 0.82f));
            }
        }

        void DrawNotes(MeshGenerationContext mgc, VisualElement v, int patternIndex)
        {
            if (!notesCache.TryGetValue(patternIndex, out var m)) return;
            var p = builtSong?.GetPattern(patternIndex);
            var rect = v.contentRect;
            float top = 14f, h = rect.height - top - 2f, laneH = h / Song.ChannelCount;
            if (p == null || laneH <= 1f || p.length <= 0) return;
            float sx = rect.width / p.length;
            var pt = mgc.painter2D;
            pt.lineWidth = 1f;
            for (int c = 1; c < Song.ChannelCount; c++)
                pt.Line(new Vector2(0, top + c * laneH), new Vector2(rect.width, top + c * laneH), LaneLine);
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var notes = m.notes[c];
                if (notes == null || notes.Count == 0) continue;
                bool noise = Song.Kinds[c] == ChannelKind.Noise;
                float span = Mathf.Max(1, m.hi[c] - m.lo[c]);
                float laneTop = top + c * laneH + 1f, usable = Mathf.Max(0f, laneH - 3f);
                float th = Mathf.Clamp(laneH * 0.15f, 1f, 3f);
                foreach (var n in notes)
                {
                    float y = noise ? laneTop + usable * 0.5f : laneTop + usable * (1f - (n.pitch - m.lo[c]) / span);
                    float x = n.start * sx, w = noise ? Mathf.Max(1f, sx * 0.6f) : Mathf.Max(1f, n.length * sx - 0.5f);
                    pt.FillRect(x, y - th * 0.5f, w, th, ChannelColors[c]);
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
            var d = x >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (playhead.style.display != d) playhead.style.display = d;
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
            dropMarker.style.left = SlotX(ArrangeLayout.SlotAt(blocks, RowAt(e.position))) - 1f;
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
            if (moved) O.Move(from, ArrangeLayout.DropIndex(blocks, from, RowAt(e.position)));
            else S.SelectOrder(index);
            e.StopPropagation();
        }

        /// <summary>パターン一覧からドラッグ中：帯の上なら挿入位置に印を出す（null で消す）。</summary>
        public void ShowPatternDrop(Vector2? worldPos)
        {
            if (dragIndex >= 0) return;
            int slot = worldPos.HasValue ? DropSlot(worldPos.Value) : -1;
            dropMarker.style.display = slot >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (slot >= 0) dropMarker.style.left = SlotX(slot) - 1f;
        }

        /// <summary>パターン一覧からのドロップ：帯の上なら、その位置にパターンを並べる。</summary>
        public bool DropPattern(Vector2 worldPos, int pattern)
        {
            int slot = DropSlot(worldPos);
            if (slot < 0) return false;
            O.InsertPattern(slot, pattern);
            return true;
        }

        int DropSlot(Vector2 worldPos) =>
            ctx.State.song != null && scroll.worldBound.Contains(worldPos) ? ArrangeLayout.SlotAt(blocks, RowAt(worldPos)) : -1;

        float SlotX(int slot) => slot >= blocks.Count ? ArrangeLayout.TotalRows(blocks) * zoom : blocks[slot].start * zoom;

        /// <summary>画面上の位置 → 時間軸の行。</summary>
        float RowAt(Vector2 worldPos) => canvas.WorldToLocal(worldPos).x / Mathf.Max(0.01f, zoom);

        void ShowMenu(int index)
        {
            var song = ctx.State.song;
            int cur = song.order[index];
            var m = new GenericMenu();
            m.AddItem(new GUIContent("ここから再生"), false, () => ctx.Transport.PlayFromOrder(index));
            m.AddSeparator("");
            m.AddItem(new GUIContent("後ろに挿入（編集中のパターン）"), false, O.Insert);
            m.AddItem(new GUIContent("複製"), false, () => O.Duplicate(index));
            if (song.order.Count > 1) m.AddItem(new GUIContent("削除"), false, () => O.Remove(index));
            else m.AddDisabledItem(new GUIContent("削除"));
            m.AddItem(new GUIContent("固有化（パターンを複製）"), false, () => O.MakeUnique(index));
            m.AddSeparator("");
            if (cur > 0) m.AddItem(new GUIContent("前のパターン（Alt+[）"), false, () => O.SetPattern(index, cur - 1));
            else m.AddDisabledItem(new GUIContent("前のパターン（Alt+[）"));
            if (cur < song.patterns.Count - 1) m.AddItem(new GUIContent("次のパターン（Alt+]）"), false, () => O.SetPattern(index, cur + 1));
            else m.AddDisabledItem(new GUIContent("次のパターン（Alt+]）"));
            for (int i = 0; i < song.patterns.Count; i++)
            {
                int pi = i;
                m.AddItem(new GUIContent($"パターン/{i:X2} {song.patterns[i].name}"), cur == i, () => O.SetPattern(index, pi));
            }
            m.AddItem(new GUIContent("移調/+1"), false, () => O.Transpose(index, 1));
            m.AddItem(new GUIContent("移調/-1"), false, () => O.Transpose(index, -1));
            m.AddItem(new GUIContent("移調/+12"), false, () => O.Transpose(index, 12));
            m.AddItem(new GUIContent("移調/-12"), false, () => O.Transpose(index, -12));
            m.AddItem(new GUIContent("移調/リセット"), false, () => O.Transpose(index, null));
            m.ShowAsContext();
        }
    }
}
