using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>ピアノロールの表示：音域の決定・鍵盤・グリッドとノートの描画。</summary>
    public partial class PianoRollView
    {
        public void Refresh()
        {
            var p = ctx.State.CurrentPattern;
            if (p == null) return;
            int ch = ctx.State.cursorCh;
            for (int c = 0; c < Song.ChannelCount; c++) chButtons[c].Active = c == ch;
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
            lane.Refresh();
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
                p.FillRect(0, YOf(n), width, RowH, bg);
                if (!noise && n % 12 == 0) p.FillRect(0, YOf(n) + RowH - 1, width, 1, new Color(0.3f, 0.3f, 0.34f));
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
                            p.FillRect(s * CellW, YOf(n), (e - s) * CellW, RowH,
                                Scale.Pc(n - chords[i].root) == 0 ? new Color(0.35f, 0.6f, 1f, 0.16f) : new Color(0.35f, 0.6f, 1f, 0.08f));
                    if (i > 0) p.FillRect(s * CellW - 1, 0, 2, height, new Color(0.45f, 0.65f, 1f, 0.5f));
                }
            }
            // 縦線（拍は濃く）とポリメーターの折り返し部分
            for (int r = 0; r <= len; r++)
                p.FillRect(r * CellW, 0, 1, height, r % rpb == 0 ? new Color(0.38f, 0.38f, 0.42f) : new Color(0.24f, 0.24f, 0.27f));
            if (tl < len) p.FillRect(tl * CellW, 0, (len - tl) * CellW, height, new Color(0f, 0f, 0f, 0.3f));
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
            if (selected) p.FillRect(x - 1, y - 1, wd + 2, ht + 2, new Color(1f, 1f, 1f, ghost ? 0.4f : 0.95f));
            var c = selected ? Color.Lerp(col, Color.white, 0.35f) : col;
            c.a = (ghost ? 0.4f : 1f) * (0.45f + 0.55f * vol);
            p.FillRect(x, y, wd, ht, c);
            p.FillRect(x, y, 3, ht, new Color(1f, 1f, 1f, 0.6f)); // 発音位置
            if (!n.cell.fx1.IsEmpty || !n.cell.fx2.IsEmpty) p.FillRect(x + wd - 4, y + 1, 3, 3, new Color(1f, 1f, 1f, 0.9f)); // エフェクト付き
        }
    }
}
