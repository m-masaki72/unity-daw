using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>カーソルのチャンネルで使うインストゥルメントの選択（詳しい編集は下ゾーンの楽器エディタ）。</summary>
    public sealed class InstrumentPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly DropdownField dropdown;

        public InstrumentPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            Add(ComposerContext.Title("Instrument"));

            dropdown = new DropdownField("Channel Inst") { tooltip = "カーソルのあるチャンネルで入力に使うインストゥルメント" };
            dropdown.RegisterValueChangedCallback(_ => s.SetChannelInstrument(ctx.State.cursorCh, dropdown.index));
            Add(dropdown);

            Add(ComposerContext.ButtonRow(
                new Button(() => s.ShowLowerTab(LowerTab.Instrument)) { text = "Edit…", tooltip = "楽器エディタを開く" },
                new Button(() => s.AddInstrument(false)) { text = "New", tooltip = "楽器を追加" },
                new Button(s.Audition) { text = "▶", tooltip = "選択中の楽器を試聴" }));
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null) return;
            var names = song.instruments.Select((ins, i) => $"{i:X2} {ins.name}").ToList();
            dropdown.choices = names;
            int ci = Mathf.Clamp(ctx.Session.ChannelInstrument(ctx.State.cursorCh), 0, Math.Max(0, names.Count - 1));
            if (names.Count > 0) dropdown.SetValueWithoutNotify(names[ci]);
        }
    }
}
