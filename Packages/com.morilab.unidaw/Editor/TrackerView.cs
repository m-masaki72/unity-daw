using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// トラッカー表示。行 × (Note Ins Vol FxT FxP FxT FxP Flag)。
    /// ポリメーターでトラック長を超えた行はループ内容を薄く表示（ゴースト）。
    /// </summary>
    public class TrackerView : VisualElement
    {
        public const int Cols = 7; // カーソルが乗る列数
        static readonly string[] ColClass = { "cd-note", "cd-inst", "cd-vol", "cd-fxt", "cd-fxp", "cd-fxt", "cd-fxp" };

        class RowUI
        {
            public VisualElement root;
            public Label num;
            public readonly Label[,] f = new Label[Song.ChannelCount, Cols + 1];
        }

        readonly UniDAWWindow w;
        readonly ScrollView scroll;
        readonly List<RowUI> rows = new List<RowUI>();
        readonly Button[] mute = new Button[Song.ChannelCount];
        readonly Button[] solo = new Button[Song.ChannelCount];
        int lastPlayRow = -1;
        int lastCursorRow = -1;

        public TrackerView(UniDAWWindow window)
        {
            w = window;
            AddToClassList("cd-tracker");
            style.flexGrow = 1;

            var head = new VisualElement();
            head.AddToClassList("cd-trow");
            head.AddToClassList("cd-thead");
            var rn = new Label("Row");
            rn.AddToClassList("cd-cell");
            rn.AddToClassList("cd-num");
            head.Add(rn);
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var box = new VisualElement();
                box.AddToClassList("cd-chhead-t");
                box.AddToClassList("ch" + c);
                var name = new Label(Song.ChannelNames[c]);
                name.AddToClassList("cd-chname");
                box.Add(name);
                mute[c] = new Button(() => w.ToggleMute(ch)) { text = "M" };
                solo[c] = new Button(() => w.ToggleSolo(ch)) { text = "S" };
                mute[c].AddToClassList("cd-ms");
                solo[c].AddToClassList("cd-ms");
                box.Add(mute[c]);
                box.Add(solo[c]);
                head.Add(box);
            }
            Add(head);

            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cd-tscroll");
            scroll.style.flexGrow = 1;
            Add(scroll);
        }

        void EnsureRows(int count)
        {
            while (rows.Count < count)
            {
                int r = rows.Count;
                var ui = new RowUI { root = new VisualElement() };
                ui.root.AddToClassList("cd-trow");
                ui.num = new Label(r.ToString("X2"));
                ui.num.AddToClassList("cd-cell");
                ui.num.AddToClassList("cd-num");
                ui.root.Add(ui.num);
                for (int c = 0; c < Song.ChannelCount; c++)
                {
                    var group = new VisualElement();
                    group.AddToClassList("cd-tgroup");
                    for (int k = 0; k <= Cols; k++)
                    {
                        var l = new Label();
                        l.AddToClassList("cd-cell");
                        l.AddToClassList(k < Cols ? ColClass[k] : "cd-flag");
                        int ch = c, col = k < Cols ? k : 0, row = r;
                        l.RegisterCallback<PointerDownEvent>(e =>
                        {
                            w.SetCursor(row, ch, col);
                            w.FocusMain();
                            e.StopPropagation();
                        });
                        ui.f[c, k] = l;
                        group.Add(l);
                    }
                    ui.root.Add(group);
                }
                rows.Add(ui);
                scroll.Add(ui.root);
            }
        }

        public void Refresh()
        {
            var song = w.Song;
            var p = w.CurrentPattern;
            int n = p != null ? p.length : 0;
            EnsureRows(n);
            int rpb = song != null ? System.Math.Max(1, song.rowsPerBeat) : 4;
            for (int r = 0; r < rows.Count; r++)
            {
                var ui = rows[r];
                bool vis = r < n;
                ui.root.style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
                if (!vis) continue;
                ui.root.EnableInClassList("beat", r % rpb == 0);
                ui.root.EnableInClassList("bar", r % (rpb * 4) == 0);
                ui.root.EnableInClassList("cursor-row", r == w.CursorRow);
                for (int c = 0; c < Song.ChannelCount; c++)
                {
                    int tl = p.TrackLength(c);
                    bool ghost = r >= tl;
                    var cell = p.tracks[c].cells[ghost ? r % tl : r];
                    var kind = Song.Kinds[c];
                    Set(ui.f[c, 0], cell.HasNote || cell.IsOff ? NoteText.Format(cell.note, kind) : "---", !(cell.HasNote || cell.IsOff), ghost);
                    Set(ui.f[c, 1], cell.instrument >= 0 ? cell.instrument.ToString("X2") : "..", cell.instrument < 0, ghost);
                    Set(ui.f[c, 2], cell.volume >= 0 ? cell.volume.ToString("X") : ".", cell.volume < 0, ghost);
                    SetFx(ui.f[c, 3], ui.f[c, 4], cell.fx1, ghost);
                    SetFx(ui.f[c, 5], ui.f[c, 6], cell.fx2, ghost);
                    string flag = cell.HasCondition ? "?" : (cell.probability < 100 ? "%" : "");
                    Set(ui.f[c, 7], flag, flag.Length == 0, ghost);
                    for (int k = 0; k < Cols; k++)
                        ui.f[c, k].EnableInClassList("cursor", r == w.CursorRow && c == w.CursorChannel && k == w.CursorCol);
                }
            }
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                mute[c].EnableInClassList("on", w.IsMuted(c));
                solo[c].EnableInClassList("on", w.IsSoloed(c));
            }
            lastPlayRow = -1;
            UpdatePlayhead(w.PlayheadRow);
            if (lastCursorRow != w.CursorRow)
            {
                lastCursorRow = w.CursorRow;
                ScrollToRow(w.CursorRow);
            }
        }

        static void Set(Label l, string text, bool empty, bool ghost)
        {
            if (l.text != text) l.text = text;
            l.EnableInClassList("empty", empty);
            l.EnableInClassList("ghost", ghost);
        }

        static void SetFx(Label t, Label p, Fx fx, bool ghost)
        {
            bool e = fx.IsEmpty;
            Set(t, e ? "." : FxText.TypeChar(fx.type).ToString(), e, ghost);
            Set(p, e ? ".." : fx.param.ToString("X2"), e, ghost);
        }

        public void UpdatePlayhead(int playRow)
        {
            if (playRow == lastPlayRow) return;
            if (lastPlayRow >= 0 && lastPlayRow < rows.Count) rows[lastPlayRow].root.RemoveFromClassList("playing");
            lastPlayRow = playRow;
            if (playRow >= 0 && playRow < rows.Count)
            {
                rows[playRow].root.AddToClassList("playing");
                if (w.Follow && w.IsPlaying) ScrollToRow(playRow);
            }
        }

        public void ScrollToRow(int row)
        {
            if (row < 0 || row >= rows.Count) return;
            var el = rows[row].root;
            schedule.Execute(() =>
            {
                if (float.IsNaN(el.layout.height) || el.layout.height <= 0) return;
                scroll.ScrollTo(el);
            });
        }
    }
}
