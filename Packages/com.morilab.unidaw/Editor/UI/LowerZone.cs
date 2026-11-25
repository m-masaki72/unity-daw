using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// Cubase の Lower Zone にあたる下段：Mixer / Instrument / Assist / Arrange をタブで切り替える。
    /// 開閉（分割ビューの折りたたみ）はウィンドウ側が受け持つ。
    /// </summary>
    public class LowerZone : VisualElement
    {
        readonly ComposerContext ctx;
        readonly SegmentedControl seg;
        readonly MixerView mixerView;
        readonly InstrumentEditor instrumentEditor;
        readonly AssistPanel assistPanel;
        readonly ArrangeView arrangeView;

        ComposerState State => ctx.State;

        public LowerZone(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-lower");
            var bar = new VisualElement();
            bar.AddToClassList("cd-lower-bar");
            seg = new SegmentedControl(new[] { "Mixer (F3)", "Instrument", "Assist", "Arrange" }, i => ctx.Session.ShowLowerTab((LowerTab)i));
            bar.Add(seg);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);
            var close = new Button(ctx.Session.ToggleLowerZone) { text = "×", tooltip = "下ゾーンを閉じる（Ctrl+Alt+E）" };
            close.AddToClassList("cd-lower-close");
            bar.Add(close);
            Add(bar);
            Add(mixerView = new MixerView(ctx));
            Add(instrumentEditor = new InstrumentEditor(ctx));
            Add(assistPanel = new AssistPanel(ctx));
            Add(arrangeView = new ArrangeView(ctx));
        }

        public void Refresh()
        {
            var tab = State.lowerTab;
            seg.SetIndex((int)tab);
            Show(mixerView, tab == LowerTab.Mixer);
            Show(instrumentEditor, tab == LowerTab.Instrument);
            Show(assistPanel, tab == LowerTab.Assist);
            Show(arrangeView, tab == LowerTab.Arrange);
            switch (tab)
            {
                case LowerTab.Mixer: mixerView.Refresh(); break;
                case LowerTab.Instrument: instrumentEditor.Refresh(); break;
                case LowerTab.Assist: assistPanel.Refresh(); break;
                case LowerTab.Arrange: arrangeView.Refresh(); break;
            }
        }

        /// <summary>再生中の毎フレーム更新（見えているタブだけ）。</summary>
        public void UpdatePlayback()
        {
            if (!State.lowerOpen) return;
            if (State.lowerTab == LowerTab.Mixer) mixerView.UpdateMeters();
            else if (State.lowerTab == LowerTab.Arrange) arrangeView.UpdatePlayhead();
        }

        static void Show(VisualElement v, bool on) => v.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
