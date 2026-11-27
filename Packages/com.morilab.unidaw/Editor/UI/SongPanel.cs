using System;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>右パネルの「Song」欄：テンポ（BPM / Rows/Beat / Speed）とキー・スケール。曲全体の設定はここに集める。</summary>
    public sealed class SongPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly FloatField bpmField;
        readonly IntegerField rpbField, speedField;
        readonly MenuChip rootChip, modeChip;

        public SongPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            AddToClassList("cd-songpanel");
            Add(ComposerContext.Title("Song"));

            bpmField = new FloatField("BPM") { isDelayed = true, tooltip = "テンポ（20〜400）" };
            bpmField.RegisterValueChangedCallback(e => s.SetBpm(e.newValue));
            Add(bpmField);
            rpbField = new IntegerField("Rows/Beat") { isDelayed = true, tooltip = "1 拍あたりの行数（1〜16）" };
            rpbField.RegisterValueChangedCallback(e => s.SetRowsPerBeat(e.newValue));
            Add(rpbField);
            speedField = new IntegerField("Speed") { isDelayed = true, tooltip = "1 行あたりの tick 数（エフェクトの細かさ、1〜31）" };
            speedField.RegisterValueChangedCallback(e => s.SetSpeed(e.newValue));
            Add(speedField);

            var keyRow = new VisualElement();
            keyRow.style.flexDirection = FlexDirection.Row;
            keyRow.style.alignItems = Align.Center;
            var cap = new Label("Key");
            cap.AddToClassList("cd-songpanel-cap");
            keyRow.Add(cap);
            var A = ctx.Assist;
            rootChip = new MenuChip(a => ShowMenu(a, Scale.RootNames, A.Key.root, i => A.SetKey(i, A.Key.IsChromatic ? ScaleMode.Major : A.Key.mode)), "主音");
            modeChip = new MenuChip(a => ShowMenu(a, Scale.ModeNames, (int)A.Key.mode, i => A.SetKey(A.Key.root, (ScaleMode)i)), "スケール（Chromatic = 指定なし）");
            keyRow.Add(rootChip);
            keyRow.Add(modeChip);
            Add(keyRow);
        }

        static void ShowMenu(VisualElement anchor, string[] names, int current, Action<int> pick)
        {
            var m = new DawMenu();
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                m.AddItem(names[i], i == current, () => pick(idx));
            }
            m.DropDown(anchor.worldBound);
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null) return;
            bpmField.SetValueWithoutNotify(song.bpm);
            rpbField.SetValueWithoutNotify(song.rowsPerBeat);
            speedField.SetValueWithoutNotify(song.speed);
            var key = song.Key;
            rootChip.Text = key.IsChromatic ? "—" : Scale.RootNames[key.root];
            modeChip.Text = Scale.ModeNames[(int)key.mode];
        }
    }
}
