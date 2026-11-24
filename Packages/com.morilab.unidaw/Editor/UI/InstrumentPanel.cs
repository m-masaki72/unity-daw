using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>カーソルのチャンネルで使うインストゥルメントの選択と編集。</summary>
    public sealed class InstrumentPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly DropdownField dropdown;
        readonly TextField nameField, volField, arpField, dutyField;

        public InstrumentPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            Add(ComposerContext.Title("Instrument"));

            dropdown = new DropdownField("Channel Inst") { tooltip = "カーソルのあるチャンネルで入力に使うインストゥルメント" };
            dropdown.RegisterValueChangedCallback(_ => s.SetChannelInstrument(ctx.State.cursorCh, dropdown.index));
            Add(dropdown);
            nameField = SeqField("Name", null, "Rename Instrument", (i, v) => i.name = v);
            volField = SeqField("Volume Seq", "例: 15 14 | 12 / 8 0（| = ループ、/ = リリース）", "Edit Volume Seq", (i, v) => i.volume = Sequence.Parse(v));
            arpField = SeqField("Arp Seq", "半音オフセット。例: | 0 4 7", "Edit Arp Seq", (i, v) => i.arpeggio = Sequence.Parse(v));
            dutyField = SeqField("Duty Seq", "0-3（12.5/25/50/75%）。ノイズは 1 で短周期", "Edit Duty Seq", (i, v) => i.duty = Sequence.Parse(v));

            Add(ComposerContext.ButtonRow(
                new Button(() => s.AddInstrument(false)) { text = "New" },
                new Button(() => s.AddInstrument(true)) { text = "Duplicate" },
                new Button(s.Audition) { text = "▶ Audition" }));
        }

        TextField SeqField(string label, string tooltip, string undoName, Action<Instrument, string> apply)
        {
            var f = new TextField(label) { isDelayed = true, tooltip = tooltip };
            f.RegisterValueChangedCallback(e => ctx.Session.ModifyInstrument(undoName, i => apply(i, e.newValue)));
            Add(f);
            return f;
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null) return;
            var names = song.instruments.Select((ins, i) => $"{i:X2} {ins.name}").ToList();
            dropdown.choices = names;
            int ci = Mathf.Clamp(ctx.Session.ChannelInstrument(ctx.State.cursorCh), 0, Math.Max(0, names.Count - 1));
            if (names.Count > 0) dropdown.SetValueWithoutNotify(names[ci]);
            var inst = song.GetInstrument(ci);
            nameField.SetValueWithoutNotify(inst?.name ?? "");
            volField.SetValueWithoutNotify(inst?.volume.ToString() ?? "");
            arpField.SetValueWithoutNotify(inst?.arpeggio.ToString() ?? "");
            dutyField.SetValueWithoutNotify(inst?.duty.ToString() ?? "");
        }
    }
}
