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
    public class PianoRollView : VisualElement
    {
        public const float CellW = 18f, RowH = 12f;

        readonly UniDAWWindow w;
        readonly Button[] chButtons = new Button[Song.ChannelCount];
        readonly Label info;
        readonly VisualElement keys;
        readonly VisualElement canvas;
        int lo = 36, hi = 84, lastPlayRow = -2;

        static readonly Color[] ChColors =
        {
            new Color(0.35f, 0.75f, 1f), new Color(0.45f, 0.95f, 0.55f),
            new Color(1f, 0.7f, 0.3f), new Color(0.9f, 0.5f, 0.9f),
        };

        public PianoRollView(UniDAWWindow window)
        {
            w = window;
            AddToClassList("cd-rollview");
            style.flexGrow = 1;

            var head = new VisualElement();
            head.AddToClassList("cd-chhead");
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var b = new Button(() => { w.SetCursor(w.CursorRow, ch, 0); w.FocusMain(); }) { text = Song.ChannelNames[c] };
                b.AddToClassList("cd-roll-ch");
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
            content.Add(canvas);
            scroll.Add(content);
            Add(scroll);
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
            if (IsNoise)
            {
                lo = 0;
                hi = 15;
            }
            else
            {
                lo = 36;
                hi = 84;
                var cells = p.tracks[ch].cells;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (!cells[i].HasNote) continue;
                    lo = Math.Min(lo, cells[i].note - 2);
                    hi = Math.Max(hi, cells[i].note + 2);
                }
                lo = Math.Max(NoteText.MinNote, lo);
                hi = Math.Min(NoteText.MaxNote, hi);
            }

            int tl = p.TrackLength(ch);
            info.text = (tl < p.length ? $"poly {tl}/{p.length}  " : "") + "左クリック: 置く／消す   右クリック: ノートオフ";

            keys.Clear();
            for (int n = hi; n >= lo; n--)
            {
                var l = new Label(IsNoise ? n.ToString("X") : NoteText.Format(n, ChannelKind.Pulse));
                l.style.height = RowH;
                l.style.fontSize = 9;
                l.style.paddingTop = l.style.paddingBottom = 0;
                l.style.unityTextAlign = TextAnchor.MiddleRight;
                l.style.color = !IsNoise && IsBlack(n) ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.85f, 0.85f, 0.85f);
                keys.Add(l);
            }
            canvas.style.width = p.length * CellW;
            canvas.style.height = (hi - lo + 1) * RowH;
            lastPlayRow = -2;
            canvas.MarkDirtyRepaint();
        }

        public void UpdatePlayhead()
        {
            int r = w.PlayheadRow;
            if (r == lastPlayRow) return;
            lastPlayRow = r;
            canvas.MarkDirtyRepaint();
        }

        static bool IsBlack(int n)
        {
            int k = ((n % 12) + 12) % 12;
            return k == 1 || k == 3 || k == 6 || k == 8 || k == 10;
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
            int len = pat.length, tl = pat.TrackLength(ch);
            int rpb = Math.Max(1, song.rowsPerBeat);
            float width = len * CellW, height = (hi - lo + 1) * RowH;

            // 背景（黒鍵の段を暗く、C の段に線）
            for (int n = lo; n <= hi; n++)
            {
                var bg = !IsNoise && IsBlack(n) ? new Color(0.13f, 0.13f, 0.15f) : new Color(0.18f, 0.18f, 0.2f);
                Rect(p, 0, YOf(n), width, RowH, bg);
                if (!IsNoise && n % 12 == 0) Rect(p, 0, YOf(n) + RowH - 1, width, 1, new Color(0.3f, 0.3f, 0.34f));
            }
            // 縦線（拍は濃く）とポリメーターの折り返し部分
            for (int r = 0; r <= len; r++)
                Rect(p, r * CellW, 0, 1, height, r % rpb == 0 ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.24f, 0.24f, 0.27f));
            if (tl < len) Rect(p, tl * CellW, 0, (len - tl) * CellW, height, new Color(0f, 0f, 0f, 0.3f));
            // カーソル列
            int cr = w.CursorRow;
            if (cr >= 0 && cr < len) Rect(p, cr * CellW, 0, CellW, height, new Color(1f, 1f, 1f, 0.07f));

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

            // 再生位置
            int pr = w.PlayheadRow;
            if (pr >= 0 && pr < len) Rect(p, pr * CellW, 0, 2, height, new Color(1f, 0.85f, 0.2f, 0.9f));
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
