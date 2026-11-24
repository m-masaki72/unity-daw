using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// ピアノロール表示（トラッカー／ステップと同じパターンデータの第3の見え方）。
    /// カーソルのあるチャンネルを 1 本ずつ表示する。
    /// 左クリック：その高さにノートを置く（同じノートなら消す）、右クリック：ノートオフ。
    /// </summary>
    public class PianoRollView : VisualElement, IPatternView
    {
        public const float CellW = 18f, RowH = 12f;
        const int DefaultLo = 36, DefaultHi = 84, NoiseHi = 15;

        readonly UniDAWWindow w;
        readonly Button[] chButtons = new Button[Song.ChannelCount];
        readonly Label info;
        readonly VisualElement keys;
        readonly VisualElement canvas;
        // 再生位置・カーソル列は重ね置きの要素にして、移動のたびにキャンバス全体を描き直さない
        readonly VisualElement playhead, cursorCol;
        int lo = DefaultLo, hi = DefaultHi;
        (int lo, int hi, bool noise) keysBuilt = (-1, -1, false);

        static readonly Color[] ChColors =
        {
            new Color(0.35f, 0.75f, 1f), new Color(0.45f, 0.95f, 0.55f),
            new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.5f, 0.9f),
        };

        public PianoRollView(UniDAWWindow window)
        {
            w = window;
            style.flexGrow = 1;

            var head = new VisualElement();
            head.AddToClassList("cd-chhead");
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var b = new Button(() => { w.SetCursor(w.CursorRow, ch, 0); w.FocusMain(); }) { text = Song.ChannelNames[c] };
                b.AddToClassList("cd-ms");
                chButtons[c] = b;
                head.Add(b);
            }
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
            canvas.generateVisualContent += Draw;
            canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            cursorCol = Overlay(CellW, new Color(1f, 1f, 1f, 0.07f));
            playhead = Overlay(2, new Color(1f, 0.85f, 0.2f, 0.9f));
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

        bool IsNoise => Song.Kinds[w.CursorChannel] == ChannelKind.Noise;

        /// <summary>座標 → (行, ピッチ)。テスト・クリック共用。</summary>
        public bool HitTest(Vector2 local, out int row, out int pitch)
        {
            row = Mathf.FloorToInt(local.x / CellW);
            pitch = hi - Mathf.FloorToInt(local.y / RowH);
            var p = w.CurrentPattern;
            return p != null && row >= 0 && row < p.length && pitch >= lo && pitch <= hi;
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (HitTest(e.localPosition, out int row, out int pitch))
                w.RollClick(w.CursorChannel, row, pitch, e.button);
            w.FocusMain();
            e.StopPropagation();
        }

        public void Refresh()
        {
            var p = w.CurrentPattern;
            if (p == null) return;
            int ch = w.CursorChannel;
            for (int c = 0; c < Song.ChannelCount; c++) chButtons[c].EnableInClassList("on", c == ch);

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
            info.text = (tl < p.length ? $"poly {tl}/{p.length}  " : "") + "左クリック: 置く／消す   右クリック: ノートオフ";

            if (keysBuilt != (lo, hi, noise)) RebuildKeys(noise);
            canvas.style.width = p.length * CellW;
            canvas.style.height = (hi - lo + 1) * RowH;
            PlaceOverlay(cursorCol, w.CursorRow, p.length);
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

        public void UpdatePlayhead() => PlaceOverlay(playhead, w.PlayheadRow, w.CurrentPattern?.length ?? 0);

        static void PlaceOverlay(VisualElement e, int row, int len)
        {
            bool show = row >= 0 && row < len;
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) e.style.left = row * CellW;
        }

        static void Rect(Painter2D p, float x, float y, float wd, float ht, Color c)
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
            var song = w.Song;
            var pat = w.CurrentPattern;
            if (song == null || pat == null) return;
            var p = mgc.painter2D;
            int ch = w.CursorChannel;
            bool noise = IsNoise;
            int len = pat.length, tl = pat.TrackLength(ch);
            int rpb = Math.Max(1, song.rowsPerBeat);
            float width = len * CellW, height = (hi - lo + 1) * RowH;

            // 背景（黒鍵の段を暗く、C の段に線）
            for (int n = lo; n <= hi; n++)
            {
                bool black = !noise && NoteText.IsBlackKey(n);
                Rect(p, 0, YOf(n), width, RowH, black ? new Color(0.13f, 0.13f, 0.15f) : new Color(0.18f, 0.18f, 0.2f));
                if (!noise && n % 12 == 0) Rect(p, 0, YOf(n) + RowH - 1, width, 1, new Color(0.3f, 0.3f, 0.34f));
            }
            // 縦線（拍は濃く）とポリメーターの折り返し部分
            for (int r = 0; r <= len; r++)
                Rect(p, r * CellW, 0, 1, height, r % rpb == 0 ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.24f, 0.24f, 0.27f));
            if (tl < len) Rect(p, tl * CellW, 0, (len - tl) * CellW, height, new Color(0f, 0f, 0f, 0.3f));

            // ノート：次のノート／オフ／パターン終端まで伸ばす
            var col = ChColors[ch % ChColors.Length];
            var cells = pat.tracks[ch].cells;
            int start = -1, note = 0;
            float vol = 1f;
            bool fx = false;
            for (int r = 0; r <= len; r++)
            {
                Cell c = r < len ? cells[pat.CellIndex(ch, r)] : Cell.Blank;
                bool end = r == len || c.HasNote || c.IsOff;
                if (end && start >= 0)
                {
                    DrawNote(p, start, r, note, vol, fx, col, start >= tl);
                    start = -1;
                }
                if (r == len) break;
                if (c.HasNote)
                {
                    start = r;
                    note = c.note;
                    vol = (c.volume >= 0 ? c.volume : 15) / 15f;
                    fx = !c.fx1.IsEmpty || !c.fx2.IsEmpty;
                }
                else if (c.IsOff)
                {
                    Rect(p, r * CellW + CellW * 0.5f - 1, 0, 2, height, new Color(1f, 0.35f, 0.35f, 0.5f));
                }
            }
        }

        void DrawNote(Painter2D p, int from, int to, int note, float vol, bool fx, Color col, bool ghost)
        {
            if (note < lo || note > hi) return;
            float y = YOf(note) + 1;
            float x = from * CellW + 1;
            float wd = (to - from) * CellW - 2;
            var c = new Color(col.r, col.g, col.b, (ghost ? 0.4f : 1f) * (0.45f + 0.55f * vol));
            Rect(p, x, y, wd, RowH - 2, c);
            Rect(p, x, y, 3, RowH - 2, new Color(1f, 1f, 1f, 0.6f)); // 発音位置
            if (fx) Rect(p, x + wd - 4, y + 1, 3, 3, new Color(1f, 1f, 1f, 0.9f)); // エフェクト付き
        }
    }
}
