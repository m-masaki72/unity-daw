using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>左パネル：順序表（パターンの並び・エントリ単位の移調）。</summary>
    public sealed class OrderPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly VisualElement list;

        public OrderPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Order;
            AddToClassList("cd-left");
            Add(ComposerContext.Title("Order"));
            var sv = new ScrollView();
            sv.style.flexGrow = 1;
            list = new VisualElement();
            sv.Add(list);
            Add(sv);

            Add(ComposerContext.ButtonRow(
                new Button(s.Insert) { text = "+", tooltip = "現在のパターンを順序表に挿入" },
                new Button(s.RemoveSelected) { text = "−", tooltip = "削除" },
                new Button(() => s.MoveSelected(-1)) { text = "▲" },
                new Button(() => s.MoveSelected(1)) { text = "▼" }));
            Add(ComposerContext.ButtonRow(
                new Button(() => s.ShiftSelectedPattern(-1)) { text = "Pat −" },
                new Button(() => s.ShiftSelectedPattern(1)) { text = "Pat +" }));
            Add(ComposerContext.ButtonRow(
                new Button(() => s.TransposeSelected(-1)) { text = "Tr −", tooltip = "このエントリだけ半音下げて再生（パターンは共有のまま。ノイズは対象外）" },
                new Button(() => s.TransposeSelected(1)) { text = "Tr +", tooltip = "このエントリだけ半音上げて再生" },
                new Button(() => s.TransposeSelected(12)) { text = "+12", tooltip = "1 オクターブ上げ" },
                new Button(() => s.TransposeSelected(null)) { text = "Tr 0", tooltip = "移調なしに戻す" }));
        }

        static string TransposeLabel(int t) => t == 0 ? "" : t > 0 ? $"  +{t}" : $"  {t}";

        public void Refresh()
        {
            list.Clear();
            var song = ctx.State.song;
            if (song == null) return;
            var e = ctx.Transport.Engine;
            bool playing = e != null && e.IsPlaying;
            for (int i = 0; i < song.order.Count; i++)
            {
                int idx = i;
                var pat = song.GetPattern(song.order[i]);
                var b = new Button(() => ctx.Session.SelectOrder(idx))
                {
                    text = $"{i:X2}  {song.order[i]:X2}  {pat?.name}" + TransposeLabel(song.TransposeAtOrder(i)),
                };
                b.AddToClassList("cd-order-item");
                b.EnableInClassList("selected", i == ctx.State.orderIndex);
                b.EnableInClassList("playing", playing && e.PlayingOrder == i && e.PlayingPattern == song.order[i]);
                list.Add(b);
            }
        }
    }
}
