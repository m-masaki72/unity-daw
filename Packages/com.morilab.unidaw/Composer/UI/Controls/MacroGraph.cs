using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// マクロ（<see cref="Sequence"/>）のバーグラフ編集（FamiTracker / 0CC のシーケンスエディタ風）。
    /// ドラッグで描画（間は補間）、右側の空き列に描くと長さが伸びる。下のレーンのクリックでループ / リリース位置。
    /// 編集は手元のコピーに行い、1 ストロークごとに <see cref="Committed"/> で確定する（Undo 1 回）。
    /// </summary>
    public sealed class MacroGraph : VisualElement
    {
        public const int MaxLength = 128;
        const int MinColumns = 16, GhostColumns = 8;
        const float LaneH = 13f, Gap = 2f;

        static readonly Color Bg = new Color(0.07f, 0.07f, 0.09f);
        static readonly Color GhostBg = new Color(0.1f, 0.1f, 0.13f);
        static readonly Color Grid = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color Axis = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color LoopShade = new Color(0.4f, 0.8f, 0.5f, 0.08f);
        static readonly Color LoopColor = new Color(0.4f, 0.85f, 0.55f);
        static readonly Color ReleaseShade = new Color(0.95f, 0.6f, 0.3f, 0.08f);
        static readonly Color ReleaseColor = new Color(0.95f, 0.62f, 0.32f);
        static readonly Color LaneBg = new Color(0.12f, 0.12f, 0.15f);

        Sequence seq = new Sequence();
        int min, max = 15;
        Color barColor = new Color(0.4f, 0.6f, 1f);
        bool drawing;
        int columns = MinColumns, lastCol = -1, lastVal;
        readonly Label hover;

        /// <summary>1 ストローク / 1 操作ぶんの確定（新しいシーケンス）。</summary>
        public event Action<Sequence> Committed;

        public MacroGraph()
        {
            AddToClassList("cd-macro-graph");
            focusable = false;
            hover = new Label { pickingMode = PickingMode.Ignore };
            hover.AddToClassList("cd-macro-hover");
            Add(hover);
            tooltip = "ドラッグで描画（右の空き列に描くと伸びる）・右クリックでメニュー\n下段: Loop / Release レーンをクリックで位置を設定";
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ => hover.text = "");
        }

        public Sequence Sequence => seq;
        public bool IsDrawing => drawing;

        /// <summary>表示データの差し替え（描画中は無視）。</summary>
        public void SetData(Sequence source, int min, int max, Color color)
        {
            if (drawing) return;
            seq = source?.Clone() ?? new Sequence();
            if (seq.values == null) seq.values = Array.Empty<int>();
            this.min = min;
            this.max = Math.Max(min + 1, max);
            barColor = color;
            MarkDirtyRepaint();
        }

        int Length => seq.values.Length;
        int VisibleColumns => Math.Min(MaxLength, Math.Max(MinColumns, Length + GhostColumns));
        float GraphH => Math.Max(1f, contentRect.height - 2 * LaneH - Gap);
        float ColW => contentRect.width / Math.Max(1, columns);

        int ColAt(float x) => Mathf.Clamp(Mathf.FloorToInt(x / ColW), 0, columns - 1);

        int ValueAt(float y)
        {
            float t = 1f - Mathf.Clamp01(y / GraphH);
            return Mathf.Clamp(Mathf.RoundToInt(min + t * (max - min)), min, max);
        }

        float YOf(float v) => (1f - (v - min) / (max - min)) * GraphH;

        enum Zone { Graph, Loop, Release }

        Zone ZoneAt(float y)
        {
            if (y < GraphH + Gap * 0.5f) return Zone.Graph;
            return y < GraphH + Gap + LaneH ? Zone.Loop : Zone.Release;
        }

        // ---- 入力 --------------------------------------------------------------------

        void OnPointerDown(PointerDownEvent e)
        {
            columns = VisibleColumns;
            var pos = e.localPosition;
            int col = ColAt(pos.x);
            var zone = ZoneAt(pos.y);
            e.StopPropagation();

            if (e.button == 1)
            {
                ShowMenu(col, pos);
                return;
            }
            if (e.button != 0) return;

            if (zone != Zone.Graph)
            {
                if (col >= Length) return;
                if (zone == Zone.Loop) seq.loop = seq.loop == col ? -1 : col;
                else seq.release = seq.release == col ? -1 : col;
                Commit();
                return;
            }

            drawing = true;
            lastCol = col;
            lastVal = ValueAt(pos.y);
            Put(col, lastVal);
            this.CapturePointer(e.pointerId);
            MarkDirtyRepaint();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            var pos = e.localPosition;
            if (!drawing)
            {
                columns = VisibleColumns;
                int c = ColAt(pos.x);
                hover.text = ZoneAt(pos.y) == Zone.Graph
                    ? (c < Length ? $"{c}: {seq.values[c]}" : $"{c}: —") + $"   ({ValueAt(pos.y)})"
                    : (ZoneAt(pos.y) == Zone.Loop ? "Loop" : "Release") + $" @ {c}";
                return;
            }
            if (!this.HasPointerCapture(e.pointerId)) return;
            int col = ColAt(pos.x), val = ValueAt(pos.y);
            // 前の点から直線で補間して、速いドラッグでも隙間を作らない
            int steps = Math.Abs(col - lastCol);
            for (int i = 1; i <= steps; i++)
            {
                int cc = lastCol + Math.Sign(col - lastCol) * i;
                Put(cc, Mathf.RoundToInt(Mathf.Lerp(lastVal, val, i / (float)steps)));
            }
            if (steps == 0) Put(col, val);
            lastCol = col;
            lastVal = val;
            hover.text = $"{col}: {val}";
            MarkDirtyRepaint();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!drawing) return;
            drawing = false;
            this.ReleasePointer(e.pointerId);
            Commit();
        }

        /// <summary>列に値を書く。長さより右なら（直前の値で埋めて）伸ばす。</summary>
        void Put(int col, int v)
        {
            if (col >= MaxLength) return;
            if (col >= Length)
            {
                var a = new int[col + 1];
                int fill = Length > 0 ? seq.values[Length - 1] : v;
                for (int i = 0; i < a.Length; i++) a[i] = i < Length ? seq.values[i] : fill;
                seq.values = a;
            }
            seq.values[col] = Mathf.Clamp(v, min, max);
        }

        void SetLength(int len)
        {
            len = Mathf.Clamp(len, 0, MaxLength);
            var a = new int[len];
            for (int i = 0; i < len; i++) a[i] = i < Length ? seq.values[i] : (Length > 0 ? seq.values[Length - 1] : 0);
            seq.values = a;
            if (seq.loop >= len) seq.loop = -1;
            if (seq.release >= len) seq.release = -1;
        }

        void Commit()
        {
            MarkDirtyRepaint();
            Committed?.Invoke(seq.Clone());
        }

        void ShowMenu(int col, Vector2 pos)
        {
            var m = new DawMenu(ComposerRoot.PlatformOf(this));
            bool inside = col < Length;
            Item(m, $"ここで終わる（長さ {col + 1}）", true, () => { SetLength(col + 1); Commit(); });
            Item(m, $"ループ開始をここに（{col}）", inside, () => { seq.loop = col; Commit(); });
            Item(m, $"リリース開始をここに（{col}）", inside, () => { seq.release = col; Commit(); });
            m.AddSeparator("");
            Item(m, "ループを解除", seq.loop >= 0, () => { seq.loop = -1; Commit(); });
            Item(m, "リリースを解除", seq.release >= 0, () => { seq.release = -1; Commit(); });
            Item(m, "すべて消去", Length > 0, () => { seq = new Sequence(); Commit(); });
            m.DropDown(new Rect(this.LocalToWorld(pos), Vector2.zero));
        }

        static void Item(DawMenu m, string label, bool enabled, System.Action action)
        {
            if (enabled) m.AddItem(label, false, action);
            else m.AddDisabledItem(label);
        }

        // ---- 描画 --------------------------------------------------------------------

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 4 || r.height < 4) return;
            if (!drawing) columns = VisibleColumns;
            var p = mgc.painter2D;
            float cw = ColW, gh = GraphH;
            int len = Length;

            Fader.Rect(p, 0, 0, r.width, gh, Bg);
            Fader.Rect(p, len * cw, 0, r.width - len * cw, gh, GhostBg);
            if (seq.loop >= 0) Fader.Rect(p, seq.loop * cw, 0, ((seq.release > seq.loop ? seq.release : len) - seq.loop) * cw, gh, LoopShade);
            if (seq.release >= 0) Fader.Rect(p, seq.release * cw, 0, (len - seq.release) * cw, gh, ReleaseShade);

            // 目盛り：4 列ごと・値の半分ごと
            for (int c = 4; c < columns; c += 4) Fader.Rect(p, c * cw, 0, 1, gh, Grid);
            float zero = YOf(Mathf.Clamp(0, min, max));
            Fader.Rect(p, 0, zero - 0.5f, r.width, 1, Axis);

            for (int c = 0; c < len; c++)
            {
                float y = YOf(seq.values[c]);
                float top = Mathf.Min(y, zero), h = Mathf.Abs(zero - y);
                var col = barColor;
                if (h < 2f)
                {
                    // 0 の列も見えるように細い線
                    top = zero - 1f;
                    h = 2f;
                    col.a = 0.5f;
                }
                Fader.Rect(p, c * cw + 1, top, Mathf.Max(1f, cw - 2), h, col);
            }

            // ループ / リリースのレーン
            float ly = gh + Gap, ry = ly + LaneH;
            Fader.Rect(p, 0, ly, r.width, LaneH - 1, LaneBg);
            Fader.Rect(p, 0, ry, r.width, LaneH - 1, LaneBg);
            if (seq.loop >= 0)
            {
                int end = seq.release > seq.loop ? seq.release : len;
                Fader.Rect(p, seq.loop * cw, ly + 3, (end - seq.loop) * cw, LaneH - 7, new Color(LoopColor.r, LoopColor.g, LoopColor.b, 0.35f));
                Fader.Rect(p, seq.loop * cw, ly, 2, LaneH - 1, LoopColor);
            }
            if (seq.release >= 0)
            {
                Fader.Rect(p, seq.release * cw, ry + 3, (len - seq.release) * cw, LaneH - 7, new Color(ReleaseColor.r, ReleaseColor.g, ReleaseColor.b, 0.35f));
                Fader.Rect(p, seq.release * cw, ry, 2, LaneH - 1, ReleaseColor);
            }
        }
    }
}
