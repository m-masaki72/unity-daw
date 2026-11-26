using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// Cubase の Lower Zone にあたる下段：Mixer / Instrument をタブで切り替える（Arrange は上部の帯、Assist は右ゾーン）。
    /// 開閉（分割ビューの折りたたみ）はウィンドウ側が受け持つ。
    /// </summary>
    public class LowerZone : VisualElement
    {
        readonly ComposerContext ctx;
        readonly SegmentedControl seg;
        readonly MixerView mixerView;
        readonly InstrumentEditor instrumentEditor;
        readonly MasterView masterView;
        static readonly LowerTab[] Tabs = { LowerTab.Mixer, LowerTab.Instrument, LowerTab.Master };

        ComposerState State => ctx.State;

        public LowerZone(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-lower");
            var bar = new VisualElement();
            bar.AddToClassList("cd-lower-bar");
            seg = new SegmentedControl(new[] { "Mixer (F3)", "Instrument", "Master" }, i => ctx.Session.ShowLowerTab(Tabs[i]));
            seg.tooltip = "Mixer：チャンネルの音量・パン・EQ・センド / Instrument：楽器エディタ / Master：マスターチェーンと曲の解析";
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
            Add(masterView = new MasterView(ctx));
        }

        public void Refresh()
        {
            var tab = State.lowerTab;
            seg.SetIndex(System.Math.Max(0, System.Array.IndexOf(Tabs, tab)));
            Show(mixerView, tab == LowerTab.Mixer);
            Show(instrumentEditor, tab == LowerTab.Instrument);
            Show(masterView, tab == LowerTab.Master);
            switch (tab)
            {
                case LowerTab.Mixer: mixerView.Refresh(); break;
                case LowerTab.Instrument: instrumentEditor.Refresh(); break;
                case LowerTab.Master: masterView.Refresh(); break;
            }
        }

        /// <summary>再生中の毎フレーム更新（見えているタブだけ）。</summary>
        public void UpdatePlayback()
        {
            if (!State.lowerOpen) return;
            if (State.lowerTab == LowerTab.Mixer) mixerView.UpdateMeters();
            else if (State.lowerTab == LowerTab.Master) masterView.UpdateMeters();
        }

        static void Show(VisualElement v, bool on) => v.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
