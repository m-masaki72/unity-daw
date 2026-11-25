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
    /// <summary>
    /// UniDAW コンポーザーのウィンドウ。レイアウトとイベント配線だけを受け持ち、
    /// 編集・再生・入力の処理は Composer 層（<see cref="ComposerSession"/> ほか）に任せる。
    /// </summary>
    public class UniDAWWindow : EditorWindow
    {
        public const string PackageRoot = "Packages/com.morilab.unidaw";
        const string StyleSheetPath = PackageRoot + "/Editor/UI/UniDAW.uss";

        [SerializeField] ComposerState state = new ComposerState();

        // ---- Composer 層
        ComposerSession session;
        Transport transport;
        CellCommands cells;
        RollEditor roll;
        ComposerKeymap keymap;
        MidiRecorder recorder;
        SongFileCommands files;
        MixerCommands mixer;
        AssistCommands assist;
        ComposerContext ctx;

        // ---- UI
        ComposerToolbar toolbar;
        VisualElement readOnlyBanner, mainArea;
        OrderPanel orderPanel;
        PatternBar patternBar;
        CellInspector inspector;
        InstrumentPanel instrumentPanel;
        TwoPaneSplitView split;
        VisualElement lowerZone;
        SegmentedControl lowerSeg;
        MixerView mixerView;
        InstrumentEditor instrumentEditor;
        AssistPanel assistPanel;
        ArrangeView arrangeView;
        bool lowerCollapsed;
        StatusBar statusBar;
        Label help;
        IPatternView[] views;
        IPatternView CurrentView => views[Mathf.Clamp((int)state.view, 0, views.Length - 1)];
        readonly HashSet<KeyCode> held = new HashSet<KeyCode>();

        // ---- テスト・外部向けの窓口
        public Song Song => state.song;
        public Pattern CurrentPattern => state.CurrentPattern;
        public int CursorRow => state.cursorRow;
        public int CursorChannel => state.cursorCh;
        public int CursorCol => state.cursorCol;

        public void SetSong(Song s) => session.SetSong(s);
        public void SetCursor(int row, int ch, int col) => session.SetCursor(row, ch, col);
        public void SetTrackLength(int ch, int len) => session.SetTrackLength(ch, len);
        public void StepClick(int ch, int row, bool toggle) => cells.StepClick(ch, row, toggle);
        public RollEditor Roll => roll;
        public Transport Transport => transport;
        public MixerCommands Mixer => mixer;
        public AssistCommands Assist => assist;
        public ComposerSession Session => session;
        public ComposerState State => state;
        public string FxHelpAtCursor() => session.FxHelpAtCursor();

        /// <summary>キー処理（テストからも呼べるよう public）。処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift, bool alt = false) => keymap.HandleKey(k, ctrl, shift, alt);

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
            state ??= new ComposerState();
            transport = new Transport(state);
            session = new ComposerSession(state, transport);
            cells = new CellCommands(session);
            roll = new RollEditor(session);
            keymap = new ComposerKeymap(session, cells, roll);
            recorder = new MidiRecorder(session);
            files = new SongFileCommands(session);
            mixer = new MixerCommands(session);
            assist = new AssistCommands(session, roll);
            ctx = new ComposerContext(session, cells, roll, files, mixer, assist, FocusMain);

            session.Changed += RefreshAll;
            session.Notified += Notify;
            transport.Changed += RefreshAll;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += session.OnUndoRedo;
        }

        void OnDisable()
        {
            session.Changed -= RefreshAll;
            session.Notified -= Notify;
            transport.Changed -= RefreshAll;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= session.OnUndoRedo;
            transport.Engine?.Stop();
        }

        void Notify(string message) => ShowNotification(new GUIContent(message));

        void FocusMain() => mainArea?.Focus();

        // =====================================================================
        // レイアウト
        // =====================================================================

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            AddStyleSheet(root);
            root.AddToClassList("cd-root");

            toolbar = new ComposerToolbar(ctx);
            root.Add(toolbar);

            readOnlyBanner = new VisualElement();
            readOnlyBanner.AddToClassList("cd-banner");
            readOnlyBanner.Add(new Label("MIDI からインポートされた Song は読み取り専用ですわ。編集するには複製してくださいませ。"));
            readOnlyBanner.Add(new Button(files.MakeEditableCopy) { text = "編集用に複製" });
            root.Add(readOnlyBanner);

            var body = new VisualElement();
            body.AddToClassList("cd-body");
            orderPanel = new OrderPanel(ctx);
            body.Add(orderPanel);
            body.Add(BuildCenter());
            body.Add(BuildRightPanel());
            root.Add(body);

            statusBar = new StatusBar(ctx);
            root.Add(statusBar);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);

            var song = state.song;
            if (song == null) song = AssetDatabase.LoadAssetAtPath<Song>(UniDAWAssets.DemoSongPath);
            SetSong(song);
        }

        static void AddStyleSheet(VisualElement root)
        {
            var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (ss == null)
            {
                // パッケージ外へコピーされた場合の保険
                var guid = AssetDatabase.FindAssets("UniDAW t:StyleSheet").FirstOrDefault();
                if (guid != null) ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (ss != null) root.styleSheets.Add(ss);
        }

        VisualElement BuildCenter()
        {
            var center = new VisualElement();
            center.AddToClassList("cd-center");
            patternBar = new PatternBar(ctx);
            center.Add(patternBar);
            mainArea = new VisualElement { focusable = true };
            mainArea.AddToClassList("cd-main");
            mainArea.RegisterCallback<PointerDownEvent>(_ => FocusMain());
            // PatternViewKind の値の順に並べる
            views = new IPatternView[] { new TrackerView(ctx), new StepView(ctx), new PianoRollView(ctx) };
            foreach (var v in views) mainArea.Add((VisualElement)v);

            // Cubase の Lower Zone：上 = パターン編集、下 = ミキサー / 楽器エディタ（境界はドラッグで調整）
            split = new TwoPaneSplitView(1, 230, TwoPaneSplitViewOrientation.Vertical);
            split.AddToClassList("cd-split");
            split.Add(mainArea);
            split.Add(BuildLowerZone());
            center.Add(split);
            return center;
        }

        VisualElement BuildLowerZone()
        {
            lowerZone = new VisualElement();
            lowerZone.AddToClassList("cd-lower");
            var bar = new VisualElement();
            bar.AddToClassList("cd-lower-bar");
            lowerSeg = new SegmentedControl(new[] { "Mixer (F3)", "Instrument", "Assist", "Arrange" }, i => session.ShowLowerTab((LowerTab)i));
            bar.Add(lowerSeg);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);
            var close = new Button(session.ToggleLowerZone) { text = "×", tooltip = "下ゾーンを閉じる（Ctrl+Alt+E）" };
            close.AddToClassList("cd-lower-close");
            bar.Add(close);
            lowerZone.Add(bar);
            mixerView = new MixerView(ctx);
            lowerZone.Add(mixerView);
            instrumentEditor = new InstrumentEditor(ctx);
            lowerZone.Add(instrumentEditor);
            assistPanel = new AssistPanel(ctx);
            lowerZone.Add(assistPanel);
            arrangeView = new ArrangeView(ctx);
            lowerZone.Add(arrangeView);
            return lowerZone;
        }

        void RefreshLowerZone()
        {
            bool open = state.lowerOpen;
            if (open == lowerCollapsed)
            {
                lowerCollapsed = !open;
                if (open) split.UnCollapse();
                else split.CollapseChild(1);
            }
            if (!open) return;
            lowerSeg.SetIndex((int)state.lowerTab);
            var tab = state.lowerTab;
            mixerView.style.display = tab == LowerTab.Mixer ? DisplayStyle.Flex : DisplayStyle.None;
            instrumentEditor.style.display = tab == LowerTab.Instrument ? DisplayStyle.Flex : DisplayStyle.None;
            assistPanel.style.display = tab == LowerTab.Assist ? DisplayStyle.Flex : DisplayStyle.None;
            arrangeView.style.display = tab == LowerTab.Arrange ? DisplayStyle.Flex : DisplayStyle.None;
            switch (tab)
            {
                case LowerTab.Mixer: mixerView.Refresh(); break;
                case LowerTab.Instrument: instrumentEditor.Refresh(); break;
                case LowerTab.Assist: assistPanel.Refresh(); break;
                case LowerTab.Arrange: arrangeView.Refresh(); break;
            }
        }

        VisualElement BuildRightPanel()
        {
            var panel = new ScrollView();
            panel.AddToClassList("cd-right");
            inspector = new CellInspector(ctx);
            panel.Add(inspector);
            instrumentPanel = new InstrumentPanel(ctx);
            panel.Add(instrumentPanel);
            help = new Label();
            help.AddToClassList("cd-help");
            panel.Add(help);
            return panel;
        }

        // =====================================================================
        // 再表示
        // =====================================================================

        public void RefreshAll()
        {
            if (toolbar == null) return;
            bool has = state.song != null;
            toolbar.Refresh();
            help.text = ComposerKeymap.Help(DawKeys.Preset);
            mainArea.SetEnabled(has);
            readOnlyBanner.style.display = session.IsReadOnly ? DisplayStyle.Flex : DisplayStyle.None;
            var cur = CurrentView;
            foreach (var v in views) ((VisualElement)v).style.display = v == cur ? DisplayStyle.Flex : DisplayStyle.None;
            patternBar.Refresh();
            statusBar.Refresh();
            if (!has) return;
            RefreshLowerZone();
            orderPanel.Refresh();
            cur.Refresh();
            inspector.Refresh();
            instrumentPanel.Refresh();
        }

        void OnEditorUpdate()
        {
            bool moved = transport.Poll(out bool patternChanged);
            var e = transport.Engine;
            if (e == null) return;
            recorder.Drain(e);
            toolbar?.UpdatePlayhead();
            if (state.lowerOpen && state.lowerTab == LowerTab.Mixer) mixerView?.UpdateMeters();
            if (state.lowerOpen && state.lowerTab == LowerTab.Arrange) arrangeView?.UpdatePlayhead();
            if (!moved || toolbar == null || state.song == null) return;

            int pp = e.IsPlaying ? e.PlayingPattern : -1;
            if (state.follow && pp >= 0 && pp != state.patternIndex) session.FollowPlayback(pp, e.PlayingOrder);
            else if (patternChanged) RefreshAll();

            CurrentView.UpdatePlayhead();
            statusBar.Refresh();
            if (patternChanged) orderPanel.Refresh();
        }

        // =====================================================================
        // 入力
        // =====================================================================

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

        void OnKeyUp(KeyUpEvent ev)
        {
            if (!held.Remove(ev.keyCode)) return;
            if (ComposerKeymap.PianoOffset(ev.keyCode) >= 0 && state.song != null) transport.ReleasePreview(state.cursorCh);
        }

        void OnKeyDown(KeyDownEvent ev)
        {
            if (ev.keyCode == KeyCode.None || state.song == null || IsTypingInField()) return;
            if (HandleKey(ev.keyCode, ev.actionKey, ev.shiftKey, ev.altKey))
            {
                held.Add(ev.keyCode);
                ev.StopImmediatePropagation();
            }
        }

        static string DroppedMidi() => DragAndDrop.paths?.FirstOrDefault(SongFileCommands.IsMidiPath);

        void OnDragUpdated(DragUpdatedEvent e)
        {
            if (DroppedMidi() != null || DragAndDrop.objectReferences.OfType<Song>().Any())
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        void OnDragPerform(DragPerformEvent e)
        {
            var s = DragAndDrop.objectReferences.OfType<Song>().FirstOrDefault();
            DragAndDrop.AcceptDrag();
            if (s != null && !SongFileCommands.IsMidiPath(AssetDatabase.GetAssetPath(s)))
            {
                SetSong(s);
                return;
            }
            var midi = DroppedMidi();
            if (midi != null) files.ImportMidiFrom(Path.GetFullPath(midi), VoiceAssignMode.Auto);
        }
    }
}
