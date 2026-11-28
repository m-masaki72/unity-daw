using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// ステップシーケンサー表示。チャンネル毎に 16 ステップ/行で並べる。
    /// 左クリック：トグル＋選択、右クリック：選択のみ（右パネルでパラメータロック編集）。
    /// </summary>
    public class StepView : VisualElement, IPatternView
    {
        class StepUI
        {
            public VisualElement root;
            public Label note;
            public VisualElement vol;
            public VisualElement lockDot;
        }

        class ChannelUI
        {
            public Button mute, solo;
            public IntegerField len;
            public Label info;
            public VisualElement grid;
            public readonly List<StepUI> steps = new List<StepUI>();
            public int playing = -1;
        }

        readonly ComposerContext ctx;
        readonly ChannelUI[] chs = new ChannelUI[Song.ChannelCount];

        public StepView(ComposerContext context)
        {
            ctx = context;
            AddToClassList("cd-stepview");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            Add(scroll);
            style.flexGrow = 1;

            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var ui = new ChannelUI();
                var block = new VisualElement();
                block.AddToClassList("cd-chblock");
                block.AddToClassList("ch" + c);

                var head = new VisualElement();
                head.AddToClassList("cd-chhead");
                var name = new Label(Song.ChannelNames[c]);
                name.AddToClassList("cd-chname");
                head.Add(name);
                ui.mute = new Button(() => ctx.Mixer.ToggleMute(ch)) { text = "M", tooltip = "ミュート" };
                ui.solo = new Button(() => ctx.Mixer.ToggleSolo(ch)) { text = "S", tooltip = "ソロ" };
                ui.mute.AddToClassList("cd-ms");
                ui.solo.AddToClassList("cd-ms");
                head.Add(ui.mute);
                head.Add(ui.solo);
                ui.len = new IntegerField("Len") { isDelayed = true, tooltip = "トラック長（0 = パターン長）。パターン長より短くするとポリメーター" };
                ui.len.AddToClassList("cd-len");
                ui.len.RegisterValueChangedCallback(e => ctx.Session.SetTrackLength(ch, e.newValue));
                head.Add(ui.len);
                ui.info = new Label();
                ui.info.AddToClassList("cd-chinfo");
                head.Add(ui.info);
                block.Add(head);

                ui.grid = new VisualElement();
                ui.grid.AddToClassList("cd-steps");
                block.Add(ui.grid);
                scroll.Add(block);
                chs[c] = ui;
            }
        }

        void EnsureSteps(int ch, int count)
        {
            var ui = chs[ch];
            while (ui.steps.Count < count)
            {
                int i = ui.steps.Count;
                var s = new StepUI { root = new VisualElement() };
                s.root.AddToClassList("cd-step");
                s.vol = new VisualElement();
                s.vol.AddToClassList("cd-step-vol");
                s.vol.pickingMode = PickingMode.Ignore;
                s.root.Add(s.vol);
                s.note = new Label();
                s.note.AddToClassList("cd-step-note");
                s.note.pickingMode = PickingMode.Ignore;
                s.root.Add(s.note);
                s.lockDot = new VisualElement();
                s.lockDot.AddToClassList("cd-step-lock");
                s.lockDot.pickingMode = PickingMode.Ignore;
                s.root.Add(s.lockDot);
                s.root.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0) ctx.Cells.StepClick(ch, i, true);
                    else ctx.Cells.StepClick(ch, i, false);
                    ctx.FocusMain();
                    e.StopPropagation();
                });
                ui.steps.Add(s);
                ui.grid.Add(s.root);
            }
            for (int i = 0; i < ui.steps.Count; i++)
                ui.steps[i].root.style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            var p = ctx.State.CurrentPattern;
            if (p == null) return;
            int rpb = song != null ? System.Math.Max(1, song.rowsPerBeat) : 4;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var ui = chs[c];
                EnsureSteps(c, p.length);
                int tl = p.TrackLength(c);
                ui.len.SetValueWithoutNotify(p.tracks[c].length);
                ui.mute.EnableInClassList("on", ctx.Mixer.IsMuted(c));
                ui.solo.EnableInClassList("on", ctx.Mixer.IsSoloed(c));
                var inst = song != null ? song.GetInstrument(ctx.Session.ChannelInstrument(c)) : null;
                ui.info.text = (tl < p.length ? $"poly {tl}/{p.length}  " : "") + (inst != null ? $"Inst {ctx.Session.ChannelInstrument(c):X2} {inst.name}" : "");
                for (int i = 0; i < p.length; i++)
                {
                    var s = ui.steps[i];
                    bool ghost = i >= tl;
                    var cell = p.tracks[c].cells[ghost ? i % tl : i];
                    s.root.EnableInClassList("ghost", ghost);
                    s.root.EnableInClassList("beat", i % rpb == 0);
                    s.root.EnableInClassList("on", cell.HasNote);
                    s.root.EnableInClassList("off", cell.IsOff);
                    s.root.EnableInClassList("fxonly", !cell.HasNote && !cell.IsOff && !cell.IsBlank);
                    s.root.EnableInClassList("cursor", c == ctx.State.cursorCh && i == ctx.State.cursorRow);
                    s.root.EnableInClassList("prob", cell.probability < 100 || cell.HasCondition);
                    s.note.text = cell.HasNote || cell.IsOff ? NoteText.Format(cell.note, Song.Kinds[c]) : "";
                    float v = cell.HasNote ? (cell.volume >= 0 ? cell.volume : 15) / 15f : 0f;
                    s.vol.style.height = Length.Percent(v * 100f);
                    bool locks = cell.instrument >= 0 && cell.HasNote && cell.instrument != ctx.Session.ChannelInstrument(c);
                    locks |= cell.volume >= 0 || !cell.fx1.IsEmpty || !cell.fx2.IsEmpty || cell.probability < 100 || cell.HasCondition;
                    s.lockDot.style.display = locks ? DisplayStyle.Flex : DisplayStyle.None;
                    s.root.tooltip = $"{i:X2}  " + NoteText.Format(cell.note, Song.Kinds[c]) +
                                     (cell.volume >= 0 ? $"  vol {cell.volume:X}" : "") +
                                     (cell.fx1.IsEmpty ? "" : "  " + FxText.Format(cell.fx1)) +
                                     (cell.fx2.IsEmpty ? "" : "  " + FxText.Format(cell.fx2)) +
                                     (cell.probability < 100 ? $"  {cell.probability}%" : "") +
                                     (cell.HasCondition ? $"  {cell.condA}:{cell.condB}" : "");
                }
                if (ui.playing >= 0 && ui.playing < ui.steps.Count) ui.steps[ui.playing].root.RemoveFromClassList("playing");
                ui.playing = -1;
            }
            UpdatePlayhead();
        }

        public PatternViewKind Kind => PatternViewKind.Step;
        public VisualElement HeaderTools => null;

        public void UpdatePlayhead()
        {
            var p = ctx.State.CurrentPattern;
            int row = ctx.Transport.PlayheadRow;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var ui = chs[c];
                int idx = -1;
                if (p != null && row >= 0 && row < p.length) idx = p.CellIndex(c, row, ctx.Transport.PlayheadLoop);
                if (idx == ui.playing) continue;
                if (ui.playing >= 0 && ui.playing < ui.steps.Count) ui.steps[ui.playing].root.RemoveFromClassList("playing");
                ui.playing = idx;
                if (idx >= 0 && idx < ui.steps.Count) ui.steps[idx].root.AddToClassList("playing");
            }
        }
    }
}
