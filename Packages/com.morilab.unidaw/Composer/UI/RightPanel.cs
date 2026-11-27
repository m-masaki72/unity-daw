using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// 右ゾーン：上にカーソル位置の要約（タブに関係なく常に表示）、その下を Inspector / Assist のタブで切り替える。
    /// Inspector はセル・楽器・曲の設定、Assist は入力支援（スケール補正・伴奏・メロディ・Claude）。
    /// </summary>
    public sealed class RightPanel : VisualElement
    {
        static readonly string[] TabNames = { "Inspector", "Assist" };

        readonly ComposerContext ctx;
        readonly Label summary;
        readonly SegmentedControl tabs;
        readonly ScrollView inspectorPage, assistPage;
        readonly CellInspector inspector;
        readonly InstrumentPanel instrumentPanel;
        readonly SongPanel songPanel;
        readonly AssistPanel assistPanel;

        public RightPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-right");

            summary = new Label { tooltip = "カーソル位置：チャンネル・行・音・楽器" };
            summary.AddToClassList("cd-right-summary");
            Add(summary);

            tabs = new SegmentedControl(TabNames, i => ctx.Session.ShowRightTab((RightTab)i));
            tabs.AddToClassList("cd-right-tabs");
            tabs.tooltip = "Inspector：セル・楽器・曲の設定 / Assist：入力支援（スケール補正・伴奏・メロディ・Claude）";
            Add(tabs);

            inspectorPage = Page();
            inspectorPage.Add(inspector = new CellInspector(ctx));
            inspectorPage.Add(instrumentPanel = new InstrumentPanel(ctx));
            inspectorPage.Add(songPanel = new SongPanel(ctx));

            assistPage = Page();
            assistPage.Add(assistPanel = new AssistPanel(ctx));
        }

        ScrollView Page()
        {
            var sv = new ScrollView();
            sv.AddToClassList("cd-right-page");
            Add(sv);
            return sv;
        }

        public void Refresh()
        {
            var st = ctx.State;
            var tab = st.rightTab;
            tabs.SetIndex((int)tab);
            inspectorPage.style.display = tab == RightTab.Inspector ? DisplayStyle.Flex : DisplayStyle.None;
            assistPage.style.display = tab == RightTab.Assist ? DisplayStyle.Flex : DisplayStyle.None;
            if (st.song == null || st.CurrentPattern == null)
            {
                summary.text = "—";
                return;
            }
            int ch = st.cursorCh;
            var c = st.CursorCell;
            string inst = c.instrument >= 0 ? $"Inst {c.instrument:X2}" : "Inst --";
            summary.text = $"{Song.ChannelNames[ch]}   Row {st.cursorRow:X2}   {NoteText.Format(c.note, Song.Kinds[ch])}   {inst}";
            if (tab == RightTab.Inspector)
            {
                inspector.Refresh();
                instrumentPanel.Refresh();
                songPanel.Refresh();
            }
            else assistPanel.Refresh();
        }
    }
}
