using UniDAW.Midi;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>上部ツールバー：ファイル・曲選択・再生・テンポ・表示切替・入力設定・MIDI 入力。</summary>
    public sealed class ComposerToolbar : Toolbar
    {
        static readonly string[] ViewNames = { "Tracker", "Step", "Piano Roll" };

        readonly ComposerContext ctx;
        readonly ObjectField songField;
        readonly FloatField bpmField;
        readonly IntegerField rpbField, speedField, octaveField, stepField;
        readonly ToolbarButton viewButton;
        readonly ToolbarToggle followToggle, recToggle;
        readonly ToolbarMenu midiMenu;

        public ComposerToolbar(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            var t = ctx.Transport;
            AddToClassList("cd-toolbar");

            Add(BuildFileMenu());

            songField = new ObjectField { objectType = typeof(Song), allowSceneObjects = false };
            songField.AddToClassList("cd-songfield");
            songField.RegisterValueChangedCallback(e => s.SetSong(e.newValue as Song));
            Add(songField);

            Add(new ToolbarSpacer());
            Add(new ToolbarButton(() => { t.PlayPattern(); ctx.FocusMain(); }) { text = "▶ Pattern", tooltip = "パターンをループ再生 (Space)" });
            Add(new ToolbarButton(() => { t.PlaySong(); ctx.FocusMain(); }) { text = "▶ Song", tooltip = "順序表の位置から曲を再生 (Enter)" });
            Add(new ToolbarButton(t.Stop) { text = "■", tooltip = "停止 (Esc)" });

            bpmField = ComposerContext.Small(new FloatField("BPM") { isDelayed = true }, 34);
            bpmField.RegisterValueChangedCallback(e => s.SetBpm(e.newValue));
            Add(bpmField);
            rpbField = ComposerContext.Small(new IntegerField("Rows/Beat") { isDelayed = true }, 26);
            rpbField.RegisterValueChangedCallback(e => s.SetRowsPerBeat(e.newValue));
            Add(rpbField);
            speedField = ComposerContext.Small(new IntegerField("Speed") { isDelayed = true, tooltip = "1行あたりの tick 数（エフェクトの細かさ）" }, 26);
            speedField.RegisterValueChangedCallback(e => s.SetSpeed(e.newValue));
            Add(speedField);

            Add(new ToolbarSpacer());
            viewButton = new ToolbarButton(s.CycleView);
            Add(viewButton);
            followToggle = new ToolbarToggle { text = "Follow" };
            followToggle.RegisterValueChangedCallback(e => s.SetFollow(e.newValue));
            Add(followToggle);

            octaveField = ComposerContext.Small(new IntegerField("Oct"), 22);
            octaveField.RegisterValueChangedCallback(e => s.SetOctave(e.newValue));
            Add(octaveField);
            stepField = ComposerContext.Small(new IntegerField("Step") { tooltip = "ノート入力後に進む行数" }, 22);
            stepField.RegisterValueChangedCallback(e => s.SetEditStep(e.newValue));
            Add(stepField);

            Add(new ToolbarSpacer { flex = true });
            midiMenu = new ToolbarMenu();
            Add(midiMenu);
            recToggle = new ToolbarToggle { text = "● Rec", tooltip = "MIDI 入力を記録（停止中はステップ入力、再生中はリアルタイム録音）" };
            recToggle.AddToClassList("cd-rec");
            recToggle.RegisterValueChangedCallback(e => s.SetRecording(e.newValue));
            Add(recToggle);
        }

        ToolbarMenu BuildFileMenu()
        {
            var f = ctx.Files;
            var file = new ToolbarMenu { text = "File" };
            var m = file.menu;
            m.AppendAction("New Song...", _ => f.NewSong());
            m.AppendAction("Create Demo Assets", _ => f.CreateDemo());
            m.AppendSeparator();
            m.AppendAction("Import MIDI.../Auto", _ => f.ImportMidi(VoiceAssignMode.Auto));
            m.AppendAction("Import MIDI.../By Track", _ => f.ImportMidi(VoiceAssignMode.ByTrack));
            m.AppendAction("Import MIDI.../Split By Pitch", _ => f.ImportMidi(VoiceAssignMode.SplitByPitch));
            m.AppendSeparator();
            m.AppendAction("Export WAV...", _ => f.ExportWav(), HasSong);
            m.AppendAction("Export MIDI...", _ => f.ExportMidi(), HasSong);
            return file;
        }

        DropdownMenuAction.Status HasSong(DropdownMenuAction _) =>
            ctx.Session.Song != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

        public void Refresh()
        {
            var st = ctx.State;
            songField.SetValueWithoutNotify(st.song);
            viewButton.text = "View: " + ViewNames[(int)st.view];
            octaveField.SetValueWithoutNotify(st.octave);
            stepField.SetValueWithoutNotify(st.editStep);
            recToggle.SetValueWithoutNotify(st.recording);
            followToggle.SetValueWithoutNotify(st.follow);
            RebuildMidiMenu();
            if (st.song == null) return;
            bpmField.SetValueWithoutNotify(st.song.bpm);
            rpbField.SetValueWithoutNotify(st.song.rowsPerBeat);
            speedField.SetValueWithoutNotify(st.song.speed);
        }

        void RebuildMidiMenu()
        {
            midiMenu.text = MidiInput.IsOpen ? "MIDI: " + MidiInput.OpenDeviceName : "MIDI In";
            var m = midiMenu.menu;
            m.MenuItems().Clear();
            var names = MidiInput.GetDeviceNames();
            if (names.Length == 0) m.AppendAction("(デバイスなし)", null, DropdownMenuAction.Status.Disabled);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                string n = names[i];
                m.AppendAction($"{i}: {n}", _ =>
                {
                    if (!MidiInput.Open(idx)) ctx.Session.Notify($"MIDI デバイス {n} を開けませんでしたわ");
                    ctx.Session.NotifyChanged();
                }, _ => MidiInput.OpenDeviceName == n ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            m.AppendSeparator();
            m.AppendAction("Close", _ => { MidiInput.Close(); ctx.Session.NotifyChanged(); },
                _ => MidiInput.IsOpen ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            m.AppendAction("Rescan", _ => ctx.Session.NotifyChanged());
        }
    }
}
