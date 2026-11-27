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
        OrderCommands order;
        AssistCommands assist;
        MasteringCommands mastering;
        ComposerContext ctx;

        // ---- UI
        ComposerToolbar toolbar;
        VisualElement readOnlyBanner, mainArea;
        PatternListPanel patternList;
        EditorHeader editorHeader;
        ArrangeView arrangeBand;
        Splitter bandSplit;
        RightPanel rightPanel;
        KeyHelpOverlay keyHelp;
        StartScreen startScreen;
        VisualElement body, center;
        Splitter leftSplit, rightSplit, lowerSplit;
        LowerZone lowerZone;
        StatusBar statusBar;
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
        public OrderCommands Order => order;
        public AssistCommands Assist => assist;
        public ComposerSession Session => session;
        public ComposerState State => state;
        public string FxHelpAtCursor() => session.FxHelpAtCursor();

        /// <summary>キー処理（テストからも呼べるよう public）。処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift, bool alt = false) => keymap.HandleKey(k, ctrl, shift, alt);

        /// <summary>Shortcut Manager から：ウィンドウ全体のキー（<see cref="ComposerKeymap.WindowCommands"/>）を実行する。</summary>
        internal void RunWindowCommand(KeyCode k, bool ctrl, bool alt) => keymap.HandleKey(k, ctrl, false, alt);

        UniDAWShortcuts.Context shortcutContext;
        TooltipOverlay tooltips;

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
            recorder = new MidiRecorder(session);
            files = new SongFileCommands(session);
            mixer = new MixerCommands(session);
            order = new OrderCommands(session);
            assist = new AssistCommands(session, roll);
            mastering = new MasteringCommands(session);
            keymap = new ComposerKeymap(session, cells, roll, order, assist.Claude);
            ctx = new ComposerContext(session, cells, roll, files, mixer, order, assist, mastering, FocusMain);

            session.Changed += RefreshAll;
            session.Notified += Notify;
            transport.Changed += RefreshAll;
            EditorApplication.update += OnEditorUpdate;
            DawPlatform.Current.UndoRedoPerformed += session.OnUndoRedo;
            shortcutContext = new UniDAWShortcuts.Context(this);
            UnityEditor.ShortcutManagement.ShortcutManager.RegisterContext(shortcutContext);
        }

        void OnDisable()
        {
            session.Changed -= RefreshAll;
            session.Notified -= Notify;
            transport.Changed -= RefreshAll;
            EditorApplication.update -= OnEditorUpdate;
            DawPlatform.Current.UndoRedoPerformed -= session.OnUndoRedo;
            UnityEditor.ShortcutManagement.ShortcutManager.UnregisterContext(shortcutContext);
            transport.Engine?.Stop();
        }

        void Notify(string message) => statusBar?.ShowMessage(message);

        /// <summary>ステータスバーに出ている結果メッセージ（テスト用）。</summary>
        public string StatusMessage => statusBar?.Message;

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
            readOnlyBanner.Add(new Button(files.MakeEditableCopy) { text = "編集用に複製", tooltip = "この Song を編集できるアセットとして複製して開く" });
            root.Add(readOnlyBanner);

            body = new VisualElement();
            body.AddToClassList("cd-body");
            BuildCenter();
            patternList = new PatternListPanel(ctx, arrangeBand);
            body.Add(patternList);
            BuildRightPanel();
            leftSplit = new Splitter(patternList, PanelEdge.Left, () => ComposerState.MinSideWidth, () => MaxSide(rightPanel),
                w => session.SetPanelSize(PanelEdge.Left, w), session.ToggleLeftZone, "ドラッグで幅を変更・ダブルクリックで閉じる（Ctrl+Alt+L）");
            rightSplit = new Splitter(rightPanel, PanelEdge.Right, () => ComposerState.MinSideWidth, () => MaxSide(patternList),
                w => session.SetPanelSize(PanelEdge.Right, w), session.ToggleRightZone, "ドラッグで幅を変更・ダブルクリックで閉じる（Ctrl+Alt+R）");
            body.Add(leftSplit);
            body.Add(center);
            body.Add(rightSplit);
            body.Add(rightPanel);
            body.RegisterCallback<GeometryChangedEvent>(_ => ApplyLayout());
            root.Add(body);

            statusBar = new StatusBar(ctx);
            root.Add(statusBar);
            keyHelp = new KeyHelpOverlay(ctx);
            root.Add(keyHelp);

            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            // Space / Enter は再生のショートカット。フォーカスが残った Button（▶ Audition 等）まで Submit で押されると
            // 再生が切り替わらず「無反応」に見えるので、文字入力中以外は Submit を止める（ボタンはクリックで押す）
            root.RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            // フォーカスを持てない所（アレンジ帯のブロック等）をクリックしてもフォーカスがウィンドウの外へ抜けないよう、
            // 大枠にフォーカスを持たせる（クリックした所に一番近いフォーカス可能な親が受け取る）。Tab 移動の対象にはしない
            foreach (var frame in new VisualElement[] { toolbar, readOnlyBanner, body, statusBar, keyHelp })
            {
                frame.focusable = true;
                frame.tabIndex = -1;
            }
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);
            tooltips = new TooltipOverlay(root);

            SetSong(state.song);
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

        void BuildCenter()
        {
            center = new VisualElement();
            center.AddToClassList("cd-center");
            // Song が無いときだけ出す開始画面
            startScreen = new StartScreen(ctx);
            startScreen.style.display = DisplayStyle.None;
            center.Add(startScreen);
            // 上：アレンジ帯（曲全体の並び）→ 見出し → パターン編集 → 下ゾーン
            arrangeBand = new ArrangeView(ctx);
            center.Add(arrangeBand);
            bandSplit = new Splitter(arrangeBand, PanelEdge.Band, () => ComposerState.MinBandHeight,
                () => Mathf.Min(ComposerState.MaxBandHeight, mainArea.resolvedStyle.height + arrangeBand.resolvedStyle.height - PanelLayout.EditorMinHeight),
                h => session.SetPanelSize(PanelEdge.Band, h), session.ToggleBandZone, "ドラッグで高さを変更・ダブルクリックで閉じる（Ctrl+Alt+A）");
            center.Add(bandSplit);
            editorHeader = new EditorHeader(ctx);
            center.Add(editorHeader);
            mainArea = new VisualElement { focusable = true };
            mainArea.AddToClassList("cd-main");
            mainArea.RegisterCallback<PointerDownEvent>(_ => FocusMain());
            // PatternViewKind の値の順に並べる
            views = new IPatternView[] { new TrackerView(ctx), new StepView(ctx), new PianoRollView(ctx) };
            foreach (var v in views) mainArea.Add((VisualElement)v);

            // Cubase の Lower Zone：上 = パターン編集、下 = ミキサー / 楽器エディタ（境界はドラッグで調整）
            center.Add(mainArea);
            lowerZone = new LowerZone(ctx);
            lowerSplit = new Splitter(lowerZone, PanelEdge.Lower, () => ComposerState.MinLowerHeight,
                () => Mathf.Min(ComposerState.MaxLowerHeight, mainArea.resolvedStyle.height + lowerZone.resolvedStyle.height - PanelLayout.EditorMinHeight),
                h => session.SetPanelSize(PanelEdge.Lower, h), session.ToggleLowerZone, "ドラッグで高さを変更・ダブルクリックで閉じる（Ctrl+Alt+E）");
            center.Add(lowerSplit);
            center.Add(lowerZone);
        }

        /// <summary>左右パネルの最大幅：編集エリアの最小幅を残す。</summary>
        float MaxSide(VisualElement other)
        {
            float w = body.resolvedStyle.width;
            if (float.IsNaN(w)) return ComposerState.MaxSideWidth;
            float rest = w - (other.style.display == DisplayStyle.None ? 0 : other.resolvedStyle.width) - PanelLayout.MinEditorWidth(state.view);
            return Mathf.Min(ComposerState.MaxSideWidth, rest);
        }

        /// <summary>覚えている開閉・大きさを当てはめ、狭いときは <see cref="PanelLayout.Fit"/> に従って畳む。</summary>
        PanelVisibility visible = new PanelVisibility(true, true, false, true);
        const float SplitterSize = 4f;

        void ApplyLayout()
        {
            if (body == null) return;
            bool has = state.song != null;
            float h = center.resolvedStyle.height - editorHeader.resolvedStyle.height;
            visible = PanelLayout.Fit(body.resolvedStyle.width, h, new PanelVisibility(has && state.leftOpen, has && state.rightOpen, has && state.bandOpen, has && state.lowerOpen),
                state.leftWidth + SplitterSize, state.rightWidth + SplitterSize, state.bandHeight + SplitterSize, state.lowerHeight + SplitterSize, state.view);
            patternList.style.width = state.leftWidth;
            rightPanel.style.width = state.rightWidth;
            lowerZone.style.height = state.lowerHeight;
            arrangeBand.style.height = state.bandHeight;
            Show(patternList, leftSplit, visible.left);
            Show(rightPanel, rightSplit, visible.right);
            bool lowerWas = lowerZone.style.display != DisplayStyle.None;
            Show(lowerZone, lowerSplit, visible.lower);
            if (visible.lower && !lowerWas && state.song != null) lowerZone.Refresh();
            bool bandWas = arrangeBand.style.display != DisplayStyle.None;
            Show(arrangeBand, bandSplit, visible.band);
            if (visible.band && !bandWas && state.song != null) arrangeBand.Refresh();
        }

        /// <summary>表示を切り替え、変わったら true。</summary>
        static bool SetDisplay(VisualElement v, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (v.style.display == d) return false;
            v.style.display = d;
            return true;
        }

        static void Show(VisualElement panel, VisualElement splitter, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (panel.style.display != d) panel.style.display = d;
            if (splitter.style.display != d) splitter.style.display = d;
        }

        void BuildRightPanel()
        {
            rightPanel = new RightPanel(ctx);
        }

        // =====================================================================
        // 再表示
        // =====================================================================

        public void RefreshAll()
        {
            if (toolbar == null) return;
            bool has = state.song != null;
            toolbar.Refresh();
            mainArea.SetEnabled(has);
            SetDisplay(editorHeader, has);
            SetDisplay(mainArea, has);
            if (SetDisplay(startScreen, !has) && !has) startScreen.Refresh();
            SetDisplay(keyHelp, state.keyHelpOpen);
            if (state.keyHelpOpen) keyHelp.Refresh(DawKeys.Preset, state.view);
            readOnlyBanner.style.display = session.IsReadOnly ? DisplayStyle.Flex : DisplayStyle.None;
            var cur = CurrentView;
            foreach (var v in views) ((VisualElement)v).style.display = v == cur ? DisplayStyle.Flex : DisplayStyle.None;
            editorHeader.Refresh(cur);
            statusBar.Refresh();
            ApplyLayout();
            if (!has) return;
            if (visible.lower) lowerZone.Refresh();
            if (visible.band) arrangeBand.Refresh();
            if (visible.left) patternList.Refresh();
            cur.Refresh();
            if (visible.right) rightPanel.Refresh();
        }

        void OnEditorUpdate()
        {
            tooltips?.Tick();
            statusBar?.Tick();
            mastering?.Tick();
            bool moved = transport.Poll(out bool patternChanged);
            var e = transport.Engine;
            if (e == null) return;
            recorder.Drain(e);
            toolbar?.UpdatePlayhead();
            lowerZone?.UpdatePlayback();
            if (visible.band) arrangeBand?.UpdatePlayhead();
            if (!moved || toolbar == null || state.song == null) return;

            int pp = e.IsPlaying ? e.PlayingPattern : -1;
            if (state.follow && pp >= 0 && pp != state.patternIndex) session.FollowPlayback(pp, e.PlayingOrder);
            else if (patternChanged) RefreshAll();

            CurrentView.UpdatePlayhead();
            statusBar.Refresh();
            if (patternChanged) patternList.Refresh();
        }

        // =====================================================================
        // 入力
        // =====================================================================

        /// <summary>
        /// フォーカス中の部品にキーを譲るか。文字入力欄はすべて、スライダー等の操作部品は Space（再生/停止）以外を譲る。
        /// </summary>
        bool FocusTakesKey(KeyCode key)
        {
            var f = rootVisualElement.focusController?.focusedElement as VisualElement;
            for (var v = f; v != null; v = v.parent)
            {
                if (v == mainArea) return false;
                if (v is TextField || v is IntegerField || v is FloatField) return true;
                if (v is SliderInt || v is ObjectField || v is DropdownField) return key != KeyCode.Space;
            }
            return false;
        }

        /// <summary>文字入力欄にフォーカスがある（キーはすべて入力欄へ渡す）。</summary>
        internal bool IsTyping => FocusTakesKey(KeyCode.Space);

        void OnSubmit(NavigationSubmitEvent ev)
        {
            if (!IsTyping) ev.StopImmediatePropagation();
        }

        void OnKeyUp(KeyUpEvent ev)
        {
            if (!held.Remove(ev.keyCode)) return;
            if (ComposerKeymap.PianoOffset(ev.keyCode) >= 0 && state.song != null) transport.ReleasePreview(state.cursorCh);
        }

        void OnKeyDown(KeyDownEvent ev)
        {
            if (ev.keyCode == KeyCode.None || FocusTakesKey(ev.keyCode)) return;
            // ウィンドウ全体のキーは Shortcut Manager（UniDAWShortcuts）が受け持つ。ここでも処理すると二重に効く
            if (ComposerKeymap.IsWindowCommand(ev.keyCode, ev.actionKey, ev.shiftKey, ev.altKey)) return;
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
