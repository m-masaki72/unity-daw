using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniDAW.Midi;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>UniDAW コンポーザー：ステップ表示とトラッカー表示で同じ曲データを編集する。</summary>
    public class UniDAWWindow : EditorWindow
    {
        [SerializeField] Song song;
        [SerializeField] int patternIndex;
        [SerializeField] int orderIndex;
        [SerializeField] int cursorRow;
        [SerializeField] int cursorCh;
        [SerializeField] int cursorCol;
        [SerializeField] int octave = 4;
        [SerializeField] int editStep = 1;
        [SerializeField] bool stepMode;
        [SerializeField] bool follow = true;
        [SerializeField] bool recording;
        [SerializeField] bool[] muted = new bool[Song.ChannelCount];
        [SerializeField] bool[] soloed = new bool[Song.ChannelCount];

        // ---- UI
        ObjectField songField;
        FloatField bpmField;
        IntegerField speedField, rpbField, octaveField, stepField, lenField;
        TextField patNameField;
        Label patLabel, statusLabel, readOnlyLabel;
        VisualElement readOnlyBanner, mainArea, orderList;
        ToolbarButton viewButton;
        ToolbarToggle followToggle, recToggle;
        ToolbarMenu midiMenu;
        TrackerView tracker;
        StepView steps;

        // インスペクタ
        TextField noteField, fx1Field, fx2Field;
        IntegerField instField, volField, condAField, condBField, trackLenField;
        SliderInt probField;
        Label cellTitle, fxHelp;
        DropdownField instDropdown;
        TextField instName, instVol, instArp, instDuty;

        // ---- 状態
        int lastPlayRow = -2, lastPlayPattern = -2;
        readonly double[] previewOff = { -1, -1, -1, -1 };
        static Cell[] clipboard;
        static int clipboardLength;

        public Song Song => song;
        public Pattern CurrentPattern => song != null ? song.GetPattern(patternIndex) : null;
        public int CursorRow => cursorRow;
        public int CursorChannel => cursorCh;
        public int CursorCol => cursorCol;
        public bool Follow => follow;
        public ChipEngine Engine => UniDAWPreview.Engine;
        public bool IsPlaying => Engine != null && Engine.IsPlaying;

        public int PlayheadRow
        {
            get
            {
                var e = Engine;
                if (e == null || !e.IsPlaying || e.PlayingPattern != patternIndex) return -1;
                return e.PlayingRow;
            }
        }

        public int PlayheadLoop
        {
            get
            {
                var e = Engine;
                return e != null && e.IsPlaying ? e.PatternLoopCount : 0;
            }
        }

        public const string PackageRoot = "Packages/com.morilab.unidaw";
        const string StyleSheetPath = PackageRoot + "/Editor/UniDAW.uss";

        [MenuItem("Window/UniDAW/Composer")]
        public static UniDAWWindow Open()
        {
            var w = GetWindow<UniDAWWindow>();
            w.titleContent = new GUIContent("UniDAW");
            w.minSize = new Vector2(820, 420);
            return w;
        }

        public static UniDAWWindow OpenWith(Song s)
        {
            var w = Open();
            w.SetSong(s);
            return w;
        }

        [UnityEditor.Callbacks.OnOpenAsset]
        static bool OnOpenAsset(int instanceId, int line)
        {
            // ダブルクリックされたアセットは選択中になっている（ID API の世代差を避ける）
            if (Selection.activeObject is Song s)
            {
                OpenWith(s);
                return true;
            }
            return false;
        }

        void OnEnable()
        {
            if (muted == null || muted.Length != Song.ChannelCount) muted = new bool[Song.ChannelCount];
            if (soloed == null || soloed.Length != Song.ChannelCount) soloed = new bool[Song.ChannelCount];
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            var e = UniDAWPreview.Engine;
            e?.Stop();
        }

        void OnUndoRedo()
        {
            if (song == null) return;
            song.Validate();
            ClampState();
            RefreshAll();
        }

        // =====================================================================
        // UI 構築
        // =====================================================================

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            var ss0 = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            var guids = ss0 != null ? new[] { AssetDatabase.AssetPathToGUID(StyleSheetPath) } : AssetDatabase.FindAssets("UniDAW t:StyleSheet");
            foreach (var g in guids)
            {
                var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(g));
                if (ss != null) { root.styleSheets.Add(ss); break; }
            }
            root.AddToClassList("cd-root");

            root.Add(BuildToolbar());

            readOnlyBanner = new VisualElement();
            readOnlyBanner.AddToClassList("cd-banner");
            readOnlyLabel = new Label("MIDI からインポートされた Song は読み取り専用ですわ。編集するには複製してくださいませ。");
            readOnlyBanner.Add(readOnlyLabel);
            readOnlyBanner.Add(new Button(MakeEditableCopy) { text = "編集用に複製" });
            root.Add(readOnlyBanner);

            var body = new VisualElement();
            body.AddToClassList("cd-body");
            body.Add(BuildOrderPanel());

            var center = new VisualElement();
            center.AddToClassList("cd-center");
            center.Add(BuildPatternBar());
            mainArea = new VisualElement { focusable = true };
            mainArea.AddToClassList("cd-main");
            mainArea.RegisterCallback<PointerDownEvent>(_ => FocusMain());
            tracker = new TrackerView(this);
            steps = new StepView(this);
            mainArea.Add(tracker);
            mainArea.Add(steps);
            center.Add(mainArea);
            body.Add(center);

            body.Add(BuildInspector());
            root.Add(body);

            statusLabel = new Label();
            statusLabel.AddToClassList("cd-status");
            root.Add(statusLabel);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);

            if (song == null)
            {
                var demo = AssetDatabase.LoadAssetAtPath<Song>(UniDAWAssets.DemoSongPath);
                if (demo != null) song = demo;
            }
            SetSong(song);
        }

        VisualElement BuildToolbar()
        {
            var tb = new Toolbar();
            tb.AddToClassList("cd-toolbar");

            var file = new ToolbarMenu { text = "File" };
            file.menu.AppendAction("New Song...", _ => NewSong());
            file.menu.AppendAction("Create Demo Assets", _ => CreateDemo());
            file.menu.AppendSeparator();
            file.menu.AppendAction("Import MIDI.../Auto", _ => ImportMidi(VoiceAssignMode.Auto));
            file.menu.AppendAction("Import MIDI.../By Track", _ => ImportMidi(VoiceAssignMode.ByTrack));
            file.menu.AppendAction("Import MIDI.../Split By Pitch", _ => ImportMidi(VoiceAssignMode.SplitByPitch));
            file.menu.AppendSeparator();
            file.menu.AppendAction("Export WAV...", _ => ExportWav(), _ => song != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            file.menu.AppendAction("Export MIDI...", _ => ExportMidi(), _ => song != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            tb.Add(file);

            songField = new ObjectField { objectType = typeof(Song), allowSceneObjects = false };
            songField.AddToClassList("cd-songfield");
            songField.RegisterValueChangedCallback(e => SetSong(e.newValue as Song));
            tb.Add(songField);

            tb.Add(new ToolbarSpacer());
            tb.Add(new ToolbarButton(PlayPattern) { text = "▶ Pattern", tooltip = "パターンをループ再生 (Space)" });
            tb.Add(new ToolbarButton(PlaySong) { text = "▶ Song", tooltip = "順序表の位置から曲を再生 (Enter)" });
            tb.Add(new ToolbarButton(Stop) { text = "■", tooltip = "停止 (Esc)" });

            bpmField = Small(new FloatField("BPM") { isDelayed = true }, 34);
            bpmField.RegisterValueChangedCallback(e => ModifySong("BPM", () => song.bpm = Mathf.Clamp(e.newValue, 20, 400)));
            tb.Add(bpmField);
            rpbField = Small(new IntegerField("Rows/Beat") { isDelayed = true }, 26);
            rpbField.RegisterValueChangedCallback(e => ModifySong("Rows/Beat", () => song.rowsPerBeat = Mathf.Clamp(e.newValue, 1, 16)));
            tb.Add(rpbField);
            speedField = Small(new IntegerField("Speed") { isDelayed = true, tooltip = "1行あたりの tick 数（エフェクトの細かさ）" }, 26);
            speedField.RegisterValueChangedCallback(e => ModifySong("Speed", () => song.speed = Mathf.Clamp(e.newValue, 1, 31)));
            tb.Add(speedField);

            tb.Add(new ToolbarSpacer());
            viewButton = new ToolbarButton(ToggleView);
            tb.Add(viewButton);
            followToggle = new ToolbarToggle { text = "Follow", value = follow };
            followToggle.RegisterValueChangedCallback(e => follow = e.newValue);
            tb.Add(followToggle);

            octaveField = Small(new IntegerField("Oct"), 22);
            octaveField.RegisterValueChangedCallback(e => { octave = Mathf.Clamp(e.newValue, 0, 8); octaveField.SetValueWithoutNotify(octave); });
            tb.Add(octaveField);
            stepField = Small(new IntegerField("Step") { tooltip = "ノート入力後に進む行数" }, 22);
            stepField.RegisterValueChangedCallback(e => { editStep = Mathf.Clamp(e.newValue, 0, 16); stepField.SetValueWithoutNotify(editStep); });
            tb.Add(stepField);

            tb.Add(new ToolbarSpacer { flex = true });
            midiMenu = new ToolbarMenu();
            RebuildMidiMenu();
            tb.Add(midiMenu);
            recToggle = new ToolbarToggle { text = "● Rec", value = recording, tooltip = "MIDI 入力を記録（停止中はステップ入力、再生中はリアルタイム録音）" };
            recToggle.AddToClassList("cd-rec");
            recToggle.RegisterValueChangedCallback(e => recording = e.newValue);
            tb.Add(recToggle);
            return tb;
        }

        static T Small<T>(T field, int inputWidth) where T : VisualElement
        {
            field.AddToClassList("cd-small");
            var input = field.Q(className: "unity-base-field__input");
            if (input != null) input.style.width = inputWidth;
            return field;
        }

        VisualElement BuildOrderPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList("cd-left");
            var title = new Label("Order");
            title.AddToClassList("cd-panel-title");
            panel.Add(title);
            var sv = new ScrollView();
            sv.style.flexGrow = 1;
            orderList = new VisualElement();
            sv.Add(orderList);
            panel.Add(sv);

            var row1 = new VisualElement();
            row1.AddToClassList("cd-btnrow");
            row1.Add(new Button(() => OrderEdit("Add Order", () => { song.order.Insert(orderIndex + 1, patternIndex); orderIndex++; })) { text = "+", tooltip = "現在のパターンを順序表に挿入" });
            row1.Add(new Button(() => OrderEdit("Remove Order", () => { if (song.order.Count > 1) song.order.RemoveAt(orderIndex); })) { text = "−", tooltip = "削除" });
            row1.Add(new Button(() => OrderEdit("Move Order", () => Swap(orderIndex, orderIndex - 1))) { text = "▲" });
            row1.Add(new Button(() => OrderEdit("Move Order", () => Swap(orderIndex, orderIndex + 1))) { text = "▼" });
            panel.Add(row1);
            var row2 = new VisualElement();
            row2.AddToClassList("cd-btnrow");
            row2.Add(new Button(() => OrderEdit("Order Pattern", () => song.order[orderIndex] = Math.Max(0, song.order[orderIndex] - 1))) { text = "Pat −" });
            row2.Add(new Button(() => OrderEdit("Order Pattern", () => song.order[orderIndex] = Math.Min(song.patterns.Count - 1, song.order[orderIndex] + 1))) { text = "Pat +" });
            panel.Add(row2);
            return panel;
        }

        void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= song.order.Count || b >= song.order.Count) return;
            (song.order[a], song.order[b]) = (song.order[b], song.order[a]);
            orderIndex = b;
        }

        void OrderEdit(string name, Action a)
        {
            if (!CanEdit()) return;
            ModifySong(name, a);
            orderIndex = Mathf.Clamp(orderIndex, 0, song.order.Count - 1);
            patternIndex = song.order[orderIndex];
            RefreshAll();
        }

        VisualElement BuildPatternBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("cd-patbar");
            bar.Add(new Button(() => SelectPattern(patternIndex - 1)) { text = "◀" });
            patLabel = new Label();
            patLabel.AddToClassList("cd-patlabel");
            bar.Add(patLabel);
            bar.Add(new Button(() => SelectPattern(patternIndex + 1)) { text = "▶" });
            patNameField = new TextField { isDelayed = true };
            patNameField.AddToClassList("cd-patname");
            patNameField.RegisterValueChangedCallback(e => ModifyPattern("Rename Pattern", p => p.name = e.newValue));
            bar.Add(patNameField);
            lenField = new IntegerField("Len") { isDelayed = true };
            lenField.AddToClassList("cd-small");
            lenField.RegisterValueChangedCallback(e => ModifyPattern("Pattern Length", p => p.Resize(e.newValue)));
            bar.Add(lenField);
            bar.Add(new Button(NewPattern) { text = "New", tooltip = "空のパターンを追加" });
            bar.Add(new Button(ClonePattern) { text = "Clone", tooltip = "現在のパターンを複製" });
            return bar;
        }

        VisualElement BuildInspector()
        {
            var panel = new ScrollView();
            panel.AddToClassList("cd-right");

            cellTitle = new Label();
            cellTitle.AddToClassList("cd-panel-title");
            panel.Add(cellTitle);

            noteField = new TextField("Note") { isDelayed = true, tooltip = "C-4 / C#5 / === (オフ) / --- (空)。ノイズは 0-#〜F-#" };
            noteField.RegisterValueChangedCallback(e =>
            {
                if (NoteText.TryParse(e.newValue, Song.Kinds[cursorCh], out int n)) ModifyCell("Edit Note", (ref Cell c) => c.note = n);
                else RefreshInspector();
            });
            panel.Add(noteField);
            instField = new IntegerField("Instrument") { isDelayed = true, tooltip = "-1 = 変更なし" };
            instField.RegisterValueChangedCallback(e => ModifyCell("Edit Instrument", (ref Cell c) => c.instrument = Mathf.Clamp(e.newValue, -1, 255)));
            panel.Add(instField);
            volField = new IntegerField("Volume") { isDelayed = true, tooltip = "0-15、-1 = 既定（パラメータロック解除）" };
            volField.RegisterValueChangedCallback(e => ModifyCell("Edit Volume", (ref Cell c) => c.volume = Mathf.Clamp(e.newValue, -1, 15)));
            panel.Add(volField);
            fx1Field = new TextField("Fx 1") { isDelayed = true };
            fx1Field.RegisterValueChangedCallback(e =>
            {
                if (FxText.TryParse(e.newValue, out var fx)) ModifyCell("Edit Fx", (ref Cell c) => c.fx1 = fx);
                else RefreshInspector();
            });
            panel.Add(fx1Field);
            fx2Field = new TextField("Fx 2") { isDelayed = true };
            fx2Field.RegisterValueChangedCallback(e =>
            {
                if (FxText.TryParse(e.newValue, out var fx)) ModifyCell("Edit Fx", (ref Cell c) => c.fx2 = fx);
                else RefreshInspector();
            });
            panel.Add(fx2Field);
            probField = new SliderInt("Probability", 0, 100) { showInputField = true, tooltip = "発音確率（%）" };
            probField.RegisterValueChangedCallback(e => ModifyCell("Edit Probability", (ref Cell c) => c.probability = e.newValue));
            panel.Add(probField);
            var condRow = new VisualElement();
            condRow.AddToClassList("cd-condrow");
            condAField = new IntegerField("Condition A:B") { isDelayed = true, tooltip = "B 回に 1 回、A 回目だけ鳴らす（B ≤ 1 で無効）" };
            condBField = new IntegerField { isDelayed = true };
            condAField.RegisterValueChangedCallback(e => ModifyCell("Edit Condition", (ref Cell c) => c.condA = Mathf.Clamp(e.newValue, 0, 16)));
            condBField.RegisterValueChangedCallback(e => ModifyCell("Edit Condition", (ref Cell c) => c.condB = Mathf.Clamp(e.newValue, 0, 16)));
            condAField.AddToClassList("cd-cond-a");
            condRow.Add(condAField);
            condRow.Add(condBField);
            panel.Add(condRow);
            trackLenField = new IntegerField("Track Length") { isDelayed = true, tooltip = "このチャンネルのループ長（0 = パターン長）" };
            trackLenField.RegisterValueChangedCallback(e => SetTrackLength(cursorCh, e.newValue));
            panel.Add(trackLenField);
            var cellBtns = new VisualElement();
            cellBtns.AddToClassList("cd-btnrow");
            cellBtns.Add(new Button(() => ModifyCell("Clear Cell", (ref Cell c) => c = Cell.Blank)) { text = "Clear Cell" });
            cellBtns.Add(new Button(() => ModifyCell("Clear Locks", (ref Cell c) =>
            {
                c.volume = -1;
                c.fx1 = Fx.None;
                c.fx2 = Fx.None;
                c.probability = 100;
                c.condA = c.condB = 0;
            })) { text = "Clear Locks" });
            panel.Add(cellBtns);
            fxHelp = new Label();
            fxHelp.AddToClassList("cd-help");
            panel.Add(fxHelp);

            var it = new Label("Instrument");
            it.AddToClassList("cd-panel-title");
            panel.Add(it);
            instDropdown = new DropdownField("Channel Inst");
            instDropdown.tooltip = "カーソルのあるチャンネルで入力に使うインストゥルメント";
            instDropdown.RegisterValueChangedCallback(_ =>
            {
                if (instDropdown.index >= 0) UniDAWMidiBridge.ChannelInstrument[cursorCh] = instDropdown.index;
                RefreshAll();
            });
            panel.Add(instDropdown);
            instName = new TextField("Name") { isDelayed = true };
            instName.RegisterValueChangedCallback(e => ModifyInstrument("Rename Instrument", i => i.name = e.newValue));
            panel.Add(instName);
            instVol = new TextField("Volume Seq") { isDelayed = true, tooltip = "例: 15 14 | 12 / 8 0（| = ループ、/ = リリース）" };
            instVol.RegisterValueChangedCallback(e => ModifyInstrument("Edit Volume Seq", i => i.volume = Sequence.Parse(e.newValue)));
            panel.Add(instVol);
            instArp = new TextField("Arp Seq") { isDelayed = true, tooltip = "半音オフセット。例: | 0 4 7" };
            instArp.RegisterValueChangedCallback(e => ModifyInstrument("Edit Arp Seq", i => i.arpeggio = Sequence.Parse(e.newValue)));
            panel.Add(instArp);
            instDuty = new TextField("Duty Seq") { isDelayed = true, tooltip = "0-3（12.5/25/50/75%）。ノイズは 1 で短周期" };
            instDuty.RegisterValueChangedCallback(e => ModifyInstrument("Edit Duty Seq", i => i.duty = Sequence.Parse(e.newValue)));
            panel.Add(instDuty);
            var ib = new VisualElement();
            ib.AddToClassList("cd-btnrow");
            ib.Add(new Button(() => AddInstrument(false)) { text = "New" });
            ib.Add(new Button(() => AddInstrument(true)) { text = "Duplicate" });
            ib.Add(new Button(Audition) { text = "▶ Audition" });
            panel.Add(ib);

            var help = new Label(
                "キー操作\n" +
                "Z〜M / Q〜P : ノート入力（2段鍵盤）\n" +
                "1 : ノートオフ   Del : 消去\n" +
                "0-9 A-F : 数値列（16進）\n" +
                "Fx列: 0 1 2 3 4 A V F S\n" +
                "Space : パターン再生/停止\n" +
                "Enter : 曲再生   Esc : 停止\n" +
                "[ ] : オクターブ   Tab : 次ch\n" +
                "Ctrl+↑↓ : 半音移調 (+Shift 1oct)\n" +
                "Ctrl+C / Ctrl+V : トラックコピー\n" +
                ".mid をドロップでインポート");
            help.AddToClassList("cd-help");
            panel.Add(help);
            return panel;
        }

        // =====================================================================
        // 状態・リフレッシュ
        // =====================================================================

        public void SetSong(Song s)
        {
            song = s;
            if (song != null) song.Validate();
            ClampState();
            var e = UniDAWPreview.Engine;
            if (e != null)
            {
                e.Stop();
                e.Song = song;
            }
            RefreshAll();
        }

        void ClampState()
        {
            if (song == null) return;
            orderIndex = Mathf.Clamp(orderIndex, 0, song.order.Count - 1);
            patternIndex = Mathf.Clamp(patternIndex, 0, song.patterns.Count - 1);
            var p = CurrentPattern;
            cursorRow = Mathf.Clamp(cursorRow, 0, p.length - 1);
            cursorCh = Mathf.Clamp(cursorCh, 0, Song.ChannelCount - 1);
            cursorCol = Mathf.Clamp(cursorCol, 0, TrackerView.Cols - 1);
        }

        public void RefreshAll()
        {
            if (tracker == null) return;
            songField.SetValueWithoutNotify(song);
            bool has = song != null;
            mainArea.SetEnabled(has);
            readOnlyBanner.style.display = UniDAWAssets.IsReadOnly(song) ? DisplayStyle.Flex : DisplayStyle.None;
            viewButton.text = stepMode ? "View: Step" : "View: Tracker";
            tracker.style.display = stepMode ? DisplayStyle.None : DisplayStyle.Flex;
            steps.style.display = stepMode ? DisplayStyle.Flex : DisplayStyle.None;
            octaveField.SetValueWithoutNotify(octave);
            stepField.SetValueWithoutNotify(editStep);
            recToggle.SetValueWithoutNotify(recording);
            followToggle.SetValueWithoutNotify(follow);
            RebuildMidiMenu();

            if (!has)
            {
                patLabel.text = "(Song なし)";
                statusLabel.text = "Song アセットを選ぶか、File > New Song / Create Demo Assets から始めてくださいませ。";
                return;
            }
            ApplyMuteSolo();
            bpmField.SetValueWithoutNotify(song.bpm);
            rpbField.SetValueWithoutNotify(song.rowsPerBeat);
            speedField.SetValueWithoutNotify(song.speed);
            var p = CurrentPattern;
            patLabel.text = $"Pattern {patternIndex:X2}/{song.patterns.Count - 1:X2}";
            patNameField.SetValueWithoutNotify(p.name);
            lenField.SetValueWithoutNotify(p.length);
            RefreshOrder();
            if (stepMode) steps.Refresh();
            else tracker.Refresh();
            RefreshInspector();
            RefreshStatus();
        }

        void RefreshOrder()
        {
            orderList.Clear();
            for (int i = 0; i < song.order.Count; i++)
            {
                int idx = i;
                var pat = song.GetPattern(song.order[i]);
                var b = new Button(() =>
                {
                    orderIndex = idx;
                    patternIndex = song.order[idx];
                    ClampState();
                    RefreshAll();
                    if (IsPlaying && !Engine.IsPlaying) return;
                }) { text = $"{i:X2}  {song.order[i]:X2}  {pat?.name}" };
                b.AddToClassList("cd-order-item");
                b.EnableInClassList("selected", i == orderIndex);
                b.EnableInClassList("playing", IsPlaying && Engine.PlayingPattern == song.order[i] && Engine.PlayingOrder == i);
                orderList.Add(b);
            }
        }

        void RefreshInspector()
        {
            if (song == null || cellTitle == null) return;
            var p = CurrentPattern;
            var c = p.tracks[cursorCh].cells[cursorRow];
            cellTitle.text = $"Cell  {Song.ChannelNames[cursorCh]}  Row {cursorRow:X2}";
            noteField.SetValueWithoutNotify(NoteText.Format(c.note, Song.Kinds[cursorCh]));
            instField.SetValueWithoutNotify(c.instrument);
            volField.SetValueWithoutNotify(c.volume);
            fx1Field.SetValueWithoutNotify(FxText.Format(c.fx1));
            fx2Field.SetValueWithoutNotify(FxText.Format(c.fx2));
            probField.SetValueWithoutNotify(c.probability);
            condAField.SetValueWithoutNotify(c.condA);
            condBField.SetValueWithoutNotify(c.condB);
            trackLenField.SetValueWithoutNotify(p.tracks[cursorCh].length);
            string Desc(Fx fx) => fx.IsEmpty ? "" : $"{FxText.Format(fx)} = {FxText.Describe(fx.type)}\n";
            fxHelp.text = Desc(c.fx1) + Desc(c.fx2) +
                          "0xy Arp / 1xx↑ 2xx↓ Slide / 3xx Porta\n4xy Vibrato / Axy VolSlide / Vxx Duty\nFxx Speed(<20)・BPM / Sxx NoteCut";

            var names = song.instruments.Select((ins, i) => $"{i:X2} {ins.name}").ToList();
            instDropdown.choices = names;
            int ci = Mathf.Clamp(UniDAWMidiBridge.ChannelInstrument[cursorCh], 0, Math.Max(0, names.Count - 1));
            if (names.Count > 0) instDropdown.SetValueWithoutNotify(names[ci]);
            var inst = song.GetInstrument(ci);
            instName.SetValueWithoutNotify(inst?.name ?? "");
            instVol.SetValueWithoutNotify(inst?.volume.ToString() ?? "");
            instArp.SetValueWithoutNotify(inst?.arpeggio.ToString() ?? "");
            instDuty.SetValueWithoutNotify(inst?.duty.ToString() ?? "");
        }

        void RefreshStatus()
        {
            if (statusLabel == null || song == null) return;
            var dev = MidiInput.IsOpen ? MidiInput.OpenDeviceName : "なし";
            var e = Engine;
            string play = e != null && e.IsPlaying ? $"▶ Order {e.PlayingOrder:X2} Pat {e.PlayingPattern:X2} Row {e.PlayingRow:X2}" : "■ Stop";
            statusLabel.text = $"{play}   |   Cursor {Song.ChannelNames[cursorCh]} Row {cursorRow:X2}   Oct {octave}  Step {editStep}   |   MIDI: {dev}{(recording ? "  ● REC" : "")}";
        }

        public void FocusMain() => mainArea?.Focus();

        // =====================================================================
        // 編集
        // =====================================================================

        bool CanEdit()
        {
            if (song == null) return false;
            if (UniDAWAssets.IsReadOnly(song))
            {
                ShowNotification(new GUIContent("読み取り専用の Song ですわ（編集用に複製してくださいませ）"));
                return false;
            }
            return true;
        }

        void ModifySong(string name, Action a)
        {
            if (!CanEdit()) return;
            Undo.RecordObject(song, name);
            a();
            EditorUtility.SetDirty(song);
            RefreshAll();
        }

        void ModifyPattern(string name, Action<Pattern> a)
        {
            if (!CanEdit()) return;
            Undo.RecordObject(song, name);
            a(CurrentPattern);
            EditorUtility.SetDirty(song);
            ClampState();
            RefreshAll();
        }

        delegate void CellEdit(ref Cell c);

        void ModifyCell(string name, CellEdit a) => ModifyCellAt(patternIndex, cursorCh, cursorRow, name, a);

        void ModifyCellAt(int pat, int ch, int row, string name, CellEdit a)
        {
            if (!CanEdit()) return;
            var p = song.GetPattern(pat);
            if (p == null || row < 0 || row >= p.length) return;
            Undo.RecordObject(song, name);
            a(ref p.tracks[ch].cells[row]);
            EditorUtility.SetDirty(song);
            RefreshAll();
        }

        void ModifyInstrument(string name, Action<Instrument> a)
        {
            if (!CanEdit()) return;
            var inst = song.GetInstrument(UniDAWMidiBridge.ChannelInstrument[cursorCh]);
            if (inst == null) return;
            Undo.RecordObject(song, name);
            a(inst);
            EditorUtility.SetDirty(song);
            RefreshAll();
        }

        void AddInstrument(bool duplicate)
        {
            if (!CanEdit()) return;
            Undo.RecordObject(song, "Add Instrument");
            var src = song.GetInstrument(UniDAWMidiBridge.ChannelInstrument[cursorCh]);
            var inst = duplicate && src != null ? src.Clone() : new Instrument("New", "15 | 12", "", "2");
            if (duplicate) inst.name += " copy";
            song.instruments.Add(inst);
            UniDAWMidiBridge.ChannelInstrument[cursorCh] = song.instruments.Count - 1;
            EditorUtility.SetDirty(song);
            RefreshAll();
        }

        void Audition()
        {
            int note = Song.Kinds[cursorCh] == ChannelKind.Noise ? 9 : (octave + 1) * 12;
            PreviewNote(cursorCh, note, UniDAWMidiBridge.ChannelInstrument[cursorCh], 0.4);
        }

        public void SetTrackLength(int ch, int len)
        {
            ModifyPattern("Track Length", p =>
            {
                int l = Mathf.Clamp(len, 0, p.length);
                p.tracks[ch].length = l >= p.length ? 0 : l;
            });
        }

        public void SetCursor(int row, int ch, int col)
        {
            if (song == null) return;
            cursorRow = row;
            cursorCh = ch;
            cursorCol = col;
            ClampState();
            UniDAWMidiBridge.TargetChannel = cursorCh;
            RefreshAll();
        }

        public void StepClick(int ch, int row, bool toggle)
        {
            if (song == null) return;
            SetCursor(row, ch, 0);
            var p = CurrentPattern;
            if (!toggle || row >= p.TrackLength(ch)) return;
            var cell = p.tracks[ch].cells[row];
            if (cell.HasNote || cell.IsOff)
            {
                ModifyCell("Toggle Step", (ref Cell c) => c = Cell.Blank);
            }
            else
            {
                int note = LastNoteInTrack(p, ch, row);
                int inst = UniDAWMidiBridge.ChannelInstrument[ch];
                ModifyCell("Toggle Step", (ref Cell c) =>
                {
                    c.note = note;
                    c.instrument = inst;
                });
                PreviewNote(ch, note, inst, 0.15);
            }
        }

        static readonly int[] DefaultNotes = { 72, 67, 48, 9 };

        static int LastNoteInTrack(Pattern p, int ch, int row)
        {
            var cells = p.tracks[ch].cells;
            for (int i = row - 1; i >= 0; i--)
                if (cells[i].HasNote) return cells[i].note;
            for (int i = cells.Length - 1; i > row; i--)
                if (cells[i].HasNote) return cells[i].note;
            return DefaultNotes[ch];
        }

        void EnterNote(int note)
        {
            if (!CanEdit()) return;
            int ch = cursorCh;
            int inst = UniDAWMidiBridge.ChannelInstrument[ch];
            ModifyCell("Enter Note", (ref Cell c) =>
            {
                c.note = note;
                if (note >= 0) c.instrument = inst;
            });
            if (note >= 0) PreviewNote(ch, note, inst, 0.25);
            Advance();
        }

        void Advance()
        {
            var p = CurrentPattern;
            if (p == null || editStep <= 0) return;
            cursorRow = (cursorRow + editStep) % p.length;
            RefreshAll();
        }

        void PreviewNote(int ch, int note, int inst, double seconds)
        {
            var e = Engine;
            if (e == null) return;
            if (e.IsPlaying) return; // 再生中はシーケンスを邪魔しない
            UniDAWPreview.Host.EnsureSourceRunning();
            e.Song = song;
            e.NoteOn(ch, note, 15, inst);
            previewOff[ch] = EditorApplication.timeSinceStartup + seconds;
        }

        void NewPattern()
        {
            if (!CanEdit()) return;
            ModifySong("New Pattern", () =>
            {
                song.patterns.Add(new Pattern(CurrentPattern.length, $"P{song.patterns.Count:X2}"));
                patternIndex = song.patterns.Count - 1;
            });
            ClampState();
            RefreshAll();
        }

        void ClonePattern()
        {
            if (!CanEdit()) return;
            ModifySong("Clone Pattern", () =>
            {
                var c = CurrentPattern.Clone();
                c.name += "'";
                song.patterns.Add(c);
                patternIndex = song.patterns.Count - 1;
            });
            ClampState();
            RefreshAll();
        }

        void SelectPattern(int idx)
        {
            if (song == null) return;
            patternIndex = Mathf.Clamp(idx, 0, song.patterns.Count - 1);
            ClampState();
            RefreshAll();
        }

        // =====================================================================
        // 再生
        // =====================================================================

        void PlayPattern()
        {
            if (song == null) return;
            var e = Engine;
            UniDAWPreview.Host.EnsureSourceRunning();
            e.Song = song;
            e.PlayPattern(patternIndex, 0);
            FocusMain();
        }

        void PlaySong()
        {
            if (song == null) return;
            var e = Engine;
            UniDAWPreview.Host.EnsureSourceRunning();
            e.Song = song;
            e.Play(orderIndex, 0);
            FocusMain();
        }

        void Stop()
        {
            Engine?.Stop();
            lastPlayRow = -2;
            RefreshAll();
        }

        void ToggleView()
        {
            stepMode = !stepMode;
            RefreshAll();
        }

        public bool IsMuted(int ch) => muted[ch];
        public bool IsSoloed(int ch) => soloed[ch];

        public void ToggleMute(int ch)
        {
            muted[ch] = !muted[ch];
            ApplyMuteSolo();
            RefreshAll();
        }

        public void ToggleSolo(int ch)
        {
            soloed[ch] = !soloed[ch];
            ApplyMuteSolo();
            RefreshAll();
        }

        void ApplyMuteSolo()
        {
            var e = Engine;
            if (e == null) return;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                e.Mute[c] = muted[c];
                e.Solo[c] = soloed[c];
            }
        }

        void OnEditorUpdate()
        {
            var e = UniDAWPreview.Engine;
            UniDAWMidiBridge.Engine = e;
            UniDAWMidiBridge.TargetChannel = cursorCh;
            if (e == null) return;
            if (song != null && e.Song != song) e.Song = song;
            ApplyMuteSolo();
            UniDAWPreview.Host.DispatchEvents();

            double now = EditorApplication.timeSinceStartup;
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                if (previewOff[c] > 0 && now >= previewOff[c])
                {
                    previewOff[c] = -1;
                    if (!e.IsPlaying) e.NoteOff(c);
                }
            }

            DrainMidi(e);

            int pr = e.IsPlaying ? e.PlayingRow : -1;
            int pp = e.IsPlaying ? e.PlayingPattern : -1;
            if (pr == lastPlayRow && pp == lastPlayPattern) return;
            bool patternChanged = pp != lastPlayPattern;
            lastPlayRow = pr;
            lastPlayPattern = pp;
            if (tracker == null || song == null) return;

            if (follow && pp >= 0 && pp != patternIndex)
            {
                patternIndex = pp;
                orderIndex = Mathf.Clamp(e.PlayingOrder, 0, song.order.Count - 1);
                ClampState();
                RefreshAll();
            }
            else if (patternChanged) RefreshAll();

            if (stepMode) steps.UpdatePlayhead();
            else tracker.UpdatePlayhead(PlayheadRow);
            RefreshStatus();
            if (patternChanged) RefreshOrder();
        }

        // =====================================================================
        // MIDI
        // =====================================================================

        void RebuildMidiMenu()
        {
            if (midiMenu == null) return;
            midiMenu.text = MidiInput.IsOpen ? "MIDI: " + MidiInput.OpenDeviceName : "MIDI In";
            var items = midiMenu.menu.MenuItems();
            items.Clear();
            var names = MidiInput.GetDeviceNames();
            if (names.Length == 0) midiMenu.menu.AppendAction("(デバイスなし)", null, DropdownMenuAction.Status.Disabled);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                string n = names[i];
                midiMenu.menu.AppendAction($"{i}: {n}", _ =>
                {
                    if (!MidiInput.Open(idx)) ShowNotification(new GUIContent($"MIDI デバイス {n} を開けませんでしたわ"));
                    RefreshAll();
                }, _ => MidiInput.OpenDeviceName == n ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            midiMenu.menu.AppendSeparator();
            midiMenu.menu.AppendAction("Close", _ => { MidiInput.Close(); RefreshAll(); }, _ => MidiInput.IsOpen ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            midiMenu.menu.AppendAction("Rescan", _ => RefreshAll());
        }

        void DrainMidi(ChipEngine e)
        {
            if (UniDAWMidiBridge.Pending.IsEmpty) return;
            var latest = new Dictionary<int, LiveNote>();
            while (UniDAWMidiBridge.Pending.TryDequeue(out var n))
            {
                if (!n.on) continue;
                if (!latest.TryGetValue(n.channel, out var prev) || n.note > prev.note || n.channel == 3) latest[n.channel] = n;
            }
            if (!recording || song == null || latest.Count == 0 || UniDAWAssets.IsReadOnly(song)) return;

            bool live = e.IsPlaying && e.PlayingPattern >= 0;
            int pat = live ? e.PlayingPattern : patternIndex;
            int row = live ? e.PlayingRow : cursorRow;
            foreach (var kv in latest)
            {
                var n = kv.Value;
                int vol = n.volume >= 13 ? -1 : n.volume;
                ModifyCellAt(pat, n.channel, row, live ? "Record MIDI" : "Step Input", (ref Cell c) =>
                {
                    c.note = n.note;
                    c.instrument = n.instrument;
                    c.volume = vol;
                });
            }
            if (!live) Advance();
        }

        // =====================================================================
        // キーボード
        // =====================================================================

        static readonly KeyCode[] LowerKeys =
        {
            KeyCode.Z, KeyCode.S, KeyCode.X, KeyCode.D, KeyCode.C, KeyCode.V, KeyCode.G, KeyCode.B, KeyCode.H, KeyCode.N, KeyCode.J, KeyCode.M,
            KeyCode.Comma, KeyCode.L, KeyCode.Period, KeyCode.Semicolon, KeyCode.Slash,
        };

        static readonly KeyCode[] UpperKeys =
        {
            KeyCode.Q, KeyCode.Alpha2, KeyCode.W, KeyCode.Alpha3, KeyCode.E, KeyCode.R, KeyCode.Alpha5, KeyCode.T, KeyCode.Alpha6, KeyCode.Y, KeyCode.Alpha7, KeyCode.U,
            KeyCode.I, KeyCode.Alpha9, KeyCode.O, KeyCode.Alpha0, KeyCode.P,
        };

        static int PianoOffset(KeyCode k)
        {
            int i = Array.IndexOf(LowerKeys, k);
            if (i >= 0) return i;
            i = Array.IndexOf(UpperKeys, k);
            return i >= 0 ? 12 + i : -1;
        }

        static int HexDigit(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return k - KeyCode.Alpha0;
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad0;
            if (k >= KeyCode.A && k <= KeyCode.F) return 10 + (k - KeyCode.A);
            return -1;
        }

        static char FxChar(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha4) return (char)('0' + (k - KeyCode.Alpha0));
            switch (k)
            {
                case KeyCode.A: return 'A';
                case KeyCode.V: return 'V';
                case KeyCode.F: return 'F';
                case KeyCode.S: return 'S';
                default: return '\0';
            }
        }

        bool IsTypingInField()
        {
            var f = rootVisualElement.focusController?.focusedElement as VisualElement;
            for (var v = f; v != null; v = v.parent)
            {
                if (v == mainArea) return false;
                if (v is TextField || v is IntegerField || v is FloatField || v is SliderInt || v is ObjectField || v is DropdownField) return true;
            }
            return false;
        }

        readonly HashSet<KeyCode> held = new HashSet<KeyCode>();

        void OnKeyUp(KeyUpEvent ev)
        {
            if (!held.Remove(ev.keyCode)) return;
            if (PianoOffset(ev.keyCode) >= 0 && song != null)
            {
                var e = Engine;
                if (e != null && !e.IsPlaying) previewOff[cursorCh] = EditorApplication.timeSinceStartup + 0.02;
            }
        }

        void OnKeyDown(KeyDownEvent ev)
        {
            if (ev.keyCode == KeyCode.None || song == null || IsTypingInField()) return;
            if (HandleKey(ev.keyCode, ev.actionKey, ev.shiftKey))
            {
                held.Add(ev.keyCode);
                ev.StopImmediatePropagation();
            }
        }

        /// <summary>キー処理（テストからも呼べるよう public）。処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift)
        {
            if (song == null) return false;
            var p = CurrentPattern;

            // ---- 共通
            switch (k)
            {
                case KeyCode.Space:
                    if (IsPlaying) Stop();
                    else PlayPattern();
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    PlaySong();
                    return true;
                case KeyCode.Escape:
                    Stop();
                    return true;
                case KeyCode.LeftBracket:
                    octave = Math.Max(0, octave - 1);
                    RefreshAll();
                    return true;
                case KeyCode.RightBracket:
                    octave = Math.Min(8, octave + 1);
                    RefreshAll();
                    return true;
                case KeyCode.Tab:
                    SetCursor(cursorRow, (cursorCh + (shift ? Song.ChannelCount - 1 : 1)) % Song.ChannelCount, stepMode ? 0 : cursorCol);
                    return true;
                case KeyCode.PageUp:
                    SetCursor(Math.Max(0, cursorRow - 16), cursorCh, cursorCol);
                    return true;
                case KeyCode.PageDown:
                    SetCursor(Math.Min(p.length - 1, cursorRow + 16), cursorCh, cursorCol);
                    return true;
                case KeyCode.Home:
                    SetCursor(0, cursorCh, cursorCol);
                    return true;
                case KeyCode.End:
                    SetCursor(p.length - 1, cursorCh, cursorCol);
                    return true;
            }

            if (ctrl)
            {
                switch (k)
                {
                    case KeyCode.UpArrow:
                        Transpose(shift ? 12 : 1);
                        return true;
                    case KeyCode.DownArrow:
                        Transpose(shift ? -12 : -1);
                        return true;
                    case KeyCode.C:
                        clipboard = (Cell[])p.tracks[cursorCh].cells.Clone();
                        clipboardLength = p.tracks[cursorCh].length;
                        ShowNotification(new GUIContent($"{Song.ChannelNames[cursorCh]} をコピーしましたわ"));
                        return true;
                    case KeyCode.V:
                        if (clipboard == null) return true;
                        ModifyPattern("Paste Track", pat =>
                        {
                            var dst = pat.tracks[cursorCh].cells;
                            for (int i = 0; i < dst.Length && i < clipboard.Length; i++) dst[i] = clipboard[i];
                            pat.tracks[cursorCh].length = clipboardLength < pat.length ? clipboardLength : 0;
                        });
                        return true;
                }
                return false;
            }

            // ---- カーソル移動
            if (stepMode)
            {
                switch (k)
                {
                    case KeyCode.LeftArrow: SetCursor((cursorRow + p.length - 1) % p.length, cursorCh, 0); return true;
                    case KeyCode.RightArrow: SetCursor((cursorRow + 1) % p.length, cursorCh, 0); return true;
                    case KeyCode.UpArrow: SetCursor(cursorRow, Math.Max(0, cursorCh - 1), 0); return true;
                    case KeyCode.DownArrow: SetCursor(cursorRow, Math.Min(Song.ChannelCount - 1, cursorCh + 1), 0); return true;
                }
            }
            else
            {
                switch (k)
                {
                    case KeyCode.UpArrow: SetCursor((cursorRow + p.length - 1) % p.length, cursorCh, cursorCol); return true;
                    case KeyCode.DownArrow: SetCursor((cursorRow + 1) % p.length, cursorCh, cursorCol); return true;
                    case KeyCode.LeftArrow:
                        if (cursorCol > 0) SetCursor(cursorRow, cursorCh, cursorCol - 1);
                        else if (cursorCh > 0) SetCursor(cursorRow, cursorCh - 1, TrackerView.Cols - 1);
                        return true;
                    case KeyCode.RightArrow:
                        if (cursorCol < TrackerView.Cols - 1) SetCursor(cursorRow, cursorCh, cursorCol + 1);
                        else if (cursorCh < Song.ChannelCount - 1) SetCursor(cursorRow, cursorCh + 1, 0);
                        return true;
                }
            }

            // ---- 削除
            if (k == KeyCode.Delete || k == KeyCode.Backspace)
            {
                int col = stepMode ? 0 : cursorCol;
                ModifyCell("Delete", (ref Cell c) =>
                {
                    switch (col)
                    {
                        case 0: c.note = Cell.Empty; c.instrument = -1; c.volume = -1; break;
                        case 1: c.instrument = -1; break;
                        case 2: c.volume = -1; break;
                        case 3: case 4: c.fx1 = Fx.None; break;
                        default: c.fx2 = Fx.None; break;
                    }
                });
                if (col == 0) Advance();
                return true;
            }

            // ---- 入力
            int column = stepMode ? 0 : cursorCol;
            if (column == 0)
            {
                if (k == KeyCode.Alpha1)
                {
                    EnterNote(Cell.Off);
                    return true;
                }
                int off = PianoOffset(k);
                if (off < 0) return false;
                int note = Song.Kinds[cursorCh] == ChannelKind.Noise
                    ? off % 16
                    : Mathf.Clamp((octave + 1) * 12 + off, NoteText.MinNote, NoteText.MaxNote);
                EnterNote(note);
                return true;
            }

            if (column == 3 || column == 5)
            {
                char ch = FxChar(k);
                if (ch == '\0') return false;
                var t = FxText.TypeFromChar(ch);
                bool first = column == 3;
                ModifyCell("Edit Fx", (ref Cell c) =>
                {
                    if (first) c.fx1 = new Fx(t, c.fx1.param);
                    else c.fx2 = new Fx(t, c.fx2.param);
                });
                SetCursor(cursorRow, cursorCh, column + 1);
                return true;
            }

            int d = HexDigit(k);
            if (d < 0) return false;
            switch (column)
            {
                case 1:
                    ModifyCell("Edit Instrument", (ref Cell c) => c.instrument = ((Math.Max(0, c.instrument) << 4) | d) & 0xFF);
                    break;
                case 2:
                    ModifyCell("Edit Volume", (ref Cell c) => c.volume = d);
                    Advance();
                    break;
                case 4:
                    ModifyCell("Edit Fx", (ref Cell c) =>
                    {
                        if (c.fx1.IsEmpty) return;
                        c.fx1 = new Fx(c.fx1.type, ((c.fx1.param << 4) | d) & 0xFF);
                    });
                    break;
                case 6:
                    ModifyCell("Edit Fx", (ref Cell c) =>
                    {
                        if (c.fx2.IsEmpty) return;
                        c.fx2 = new Fx(c.fx2.type, ((c.fx2.param << 4) | d) & 0xFF);
                    });
                    break;
            }
            return true;
        }

        void Transpose(int semis)
        {
            ModifyCell("Transpose", (ref Cell c) =>
            {
                if (!c.HasNote) return;
                if (Song.Kinds[cursorCh] == ChannelKind.Noise) c.note = Mathf.Clamp(c.note + Math.Sign(semis), 0, 15);
                else c.note = Mathf.Clamp(c.note + semis, NoteText.MinNote, NoteText.MaxNote);
            });
            var cell = CurrentPattern.tracks[cursorCh].cells[cursorRow];
            if (cell.HasNote) PreviewNote(cursorCh, cell.note, UniDAWMidiBridge.ChannelInstrument[cursorCh], 0.2);
        }

        // =====================================================================
        // ファイル
        // =====================================================================

        void NewSong()
        {
            var path = EditorUtility.SaveFilePanelInProject("New Song", "NewSong", "asset", "Song の保存先");
            if (string.IsNullOrEmpty(path)) return;
            var s = Song.CreateDefault(64);
            s.title = Path.GetFileNameWithoutExtension(path);
            SetSong(UniDAWAssets.SaveSong(s, path));
        }

        void CreateDemo()
        {
            var demo = UniDAWAssets.CreateDemo(createScene: EditorUtility.DisplayDialog("UniDAW", "デモシーン（映像連携のサンプル）も作成しますか？\n現在のシーンは閉じられます。", "作成する", "曲だけ"));
            SetSong(demo);
        }

        void ImportMidi(VoiceAssignMode mode)
        {
            var path = EditorUtility.OpenFilePanel("Import MIDI", "", "mid,midi");
            if (!string.IsNullOrEmpty(path)) ImportMidiFrom(path, mode);
        }

        void ImportMidiFrom(string path, VoiceAssignMode mode)
        {
            try
            {
                var s = UniDAWAssets.ImportMidiFile(path, new MidiImportOptions { mode = mode }, out var report);
                var dst = EditorUtility.SaveFilePanelInProject("Save Imported Song", Path.GetFileNameWithoutExtension(path), "asset", "変換した Song の保存先");
                if (string.IsNullOrEmpty(dst))
                {
                    DestroyImmediate(s);
                    return;
                }
                SetSong(UniDAWAssets.SaveSong(s, dst));
                Debug.Log($"[UniDAW] MIDI import {Path.GetFileName(path)}: {report}");
                ShowNotification(new GUIContent($"インポート完了: {report.patterns} patterns, dropped {report.droppedNotes}"));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("UniDAW", "MIDI の読み込みに失敗しましたわ…\n" + ex.Message, "OK");
            }
        }

        void ExportWav()
        {
            var path = EditorUtility.SaveFilePanel("Export WAV", "", (song.title ?? song.name) + ".wav", "wav");
            if (string.IsNullOrEmpty(path)) return;
            UniDAWAssets.ExportWav(song, path);
            ShowNotification(new GUIContent("WAV を書き出しましたわ"));
        }

        void ExportMidi()
        {
            var path = EditorUtility.SaveFilePanel("Export MIDI", "", (song.title ?? song.name) + ".mid", "mid");
            if (string.IsNullOrEmpty(path)) return;
            UniDAWAssets.ExportMidi(song, path);
            ShowNotification(new GUIContent("MIDI を書き出しましたわ"));
        }

        void MakeEditableCopy()
        {
            if (song == null) return;
            var path = EditorUtility.SaveFilePanelInProject("Editable Copy", song.name, "asset", "編集用 Song の保存先");
            if (string.IsNullOrEmpty(path)) return;
            SetSong(UniDAWAssets.MakeEditableCopy(song, path));
        }

        static string DroppedMidi()
        {
            return DragAndDrop.paths?.FirstOrDefault(p => p.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));
        }

        void OnDragUpdated(DragUpdatedEvent e)
        {
            if (DroppedMidi() != null || DragAndDrop.objectReferences.OfType<Song>().Any())
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        void OnDragPerform(DragPerformEvent e)
        {
            var s = DragAndDrop.objectReferences.OfType<Song>().FirstOrDefault();
            DragAndDrop.AcceptDrag();
            if (s != null && !AssetDatabase.GetAssetPath(s).EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
            {
                SetSong(s);
                return;
            }
            var midi = DroppedMidi();
            if (midi != null) ImportMidiFrom(Path.GetFullPath(midi), VoiceAssignMode.Auto);
        }
    }
}
