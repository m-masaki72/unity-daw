using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>カーソル位置のセルの全項目（パラメータロック・条件・トラック長）を編集する。</summary>
    public sealed class CellInspector : VisualElement
    {
        const string FxCheatSheet =
            "0xy Arp / 1xx↑ 2xx↓ Slide / 3xx Porta\n4xy Vibrato / Axy VolSlide / Vxx Duty\nFxx Speed(<20)・BPM / Sxx NoteCut";

        readonly ComposerContext ctx;
        readonly Label title, fxHelp;
        readonly TextField noteField, fx1Field, fx2Field;
        readonly IntegerField instField, volField, condAField, condBField, trackLenField;
        readonly SliderInt probField;

        public CellInspector(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;

            title = ComposerContext.Title("");
            Add(title);

            noteField = new TextField("Note") { isDelayed = true, tooltip = "C-4 / C#5 / === (オフ) / --- (空)。ノイズは 0-#〜F-#" };
            noteField.RegisterValueChangedCallback(e =>
            {
                if (NoteText.TryParse(e.newValue, Song.Kinds[ctx.State.cursorCh], out int n)) s.ModifyCell("Edit Note", (ref Cell c) => c.note = n);
                else Refresh();
            });
            Add(noteField);
            instField = new IntegerField("Instrument") { isDelayed = true, tooltip = "-1 = 変更なし" };
            instField.RegisterValueChangedCallback(e => s.ModifyCell("Edit Instrument", (ref Cell c) => c.instrument = Mathf.Clamp(e.newValue, -1, 255)));
            Add(instField);
            volField = new IntegerField("Volume") { isDelayed = true, tooltip = "0-15、-1 = 既定（パラメータロック解除）" };
            volField.RegisterValueChangedCallback(e => s.ModifyCell("Edit Volume", (ref Cell c) => c.volume = Mathf.Clamp(e.newValue, -1, 15)));
            Add(volField);
            fx1Field = FxField("Fx 1", (ref Cell c, Fx fx) => c.fx1 = fx);
            fx2Field = FxField("Fx 2", (ref Cell c, Fx fx) => c.fx2 = fx);
            probField = new SliderInt("Probability", 0, 100) { showInputField = true, tooltip = "発音確率（%）" };
            probField.RegisterValueChangedCallback(e => s.ModifyCell("Edit Probability", (ref Cell c) => c.probability = e.newValue));
            Add(probField);

            var condRow = new VisualElement();
            condRow.AddToClassList("cd-condrow");
            condAField = new IntegerField("Condition A:B") { isDelayed = true, tooltip = "B 回に 1 回、A 回目だけ鳴らす（B ≤ 1 で無効）" };
            condBField = new IntegerField { isDelayed = true };
            condAField.RegisterValueChangedCallback(e => s.ModifyCell("Edit Condition", (ref Cell c) => c.condA = Mathf.Clamp(e.newValue, 0, 16)));
            condBField.RegisterValueChangedCallback(e => s.ModifyCell("Edit Condition", (ref Cell c) => c.condB = Mathf.Clamp(e.newValue, 0, 16)));
            condAField.AddToClassList("cd-cond-a");
            condRow.Add(condAField);
            condRow.Add(condBField);
            Add(condRow);

            trackLenField = new IntegerField("Track Length") { isDelayed = true, tooltip = "このチャンネルのループ長（0 = パターン長）" };
            trackLenField.RegisterValueChangedCallback(e => s.SetTrackLength(ctx.State.cursorCh, e.newValue));
            Add(trackLenField);

            Add(ComposerContext.ButtonRow(
                new Button(ctx.Cells.ClearCell) { text = "Clear Cell", tooltip = "カーソル位置のセルを空にする" },
                new Button(ctx.Cells.ClearLocks) { text = "Clear Locks", tooltip = "ノートは残し、音量・エフェクト・確率・条件を消す" }));

            fxHelp = new Label();
            fxHelp.AddToClassList("cd-help");
            Add(fxHelp);
        }

        delegate void FxAssign(ref Cell c, Fx fx);

        TextField FxField(string label, FxAssign assign)
        {
            var f = new TextField(label) { isDelayed = true };
            f.RegisterValueChangedCallback(e =>
            {
                if (FxText.TryParse(e.newValue, out var fx)) ctx.Session.ModifyCell("Edit Fx", (ref Cell c) => assign(ref c, fx));
                else Refresh();
            });
            Add(f);
            return f;
        }

        static string Describe(Fx fx) => fx.IsEmpty ? "" : $"{FxText.Format(fx)} = {FxText.Describe(fx.type)}\n";

        public void Refresh()
        {
            var st = ctx.State;
            var p = st.CurrentPattern;
            if (p == null) return;
            int ch = st.cursorCh;
            var c = st.CursorCell;
            title.text = $"Cell  {Song.ChannelNames[ch]}  Row {st.cursorRow:X2}";
            noteField.SetValueWithoutNotify(NoteText.Format(c.note, Song.Kinds[ch]));
            instField.SetValueWithoutNotify(c.instrument);
            volField.SetValueWithoutNotify(c.volume);
            fx1Field.SetValueWithoutNotify(FxText.Format(c.fx1));
            fx2Field.SetValueWithoutNotify(FxText.Format(c.fx2));
            probField.SetValueWithoutNotify(c.probability);
            condAField.SetValueWithoutNotify(c.condA);
            condBField.SetValueWithoutNotify(c.condB);
            trackLenField.SetValueWithoutNotify(p.tracks[ch].length);
            fxHelp.text = Describe(c.fx1) + Describe(c.fx2) + FxCheatSheet;
        }
    }
}
