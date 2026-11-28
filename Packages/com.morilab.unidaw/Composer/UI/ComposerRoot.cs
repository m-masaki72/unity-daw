using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// コンポーザー画面一式（ツールバー・左右パネル・パターン編集・下ゾーン・ステータスバー）のルート要素。
    /// エディタのウィンドウにもゲーム内の UIDocument にも、このまま置ける。
    /// 毎フレームの更新と Undo/Redo の通知は <see cref="DawPlatform.Current"/> から受け取る。
    /// 使い終わったら <see cref="Dispose"/> を呼ぶ。
    /// </summary>
    public sealed class ComposerRoot : VisualElement, IDisposable
    {
        readonly ComposerState state;
        readonly IDawPlatform platform;

        /// <summary>この画面のホスト。</summary>
        public IDawPlatform Platform => platform;

        /// <summary>要素が載っている画面のホスト（画面の外なら <see cref="DawPlatform.Current"/>）。</summary>
        public static IDawPlatform PlatformOf(VisualElement e)
        {
            for (var v = e; v != null; v = v.parent)
                if (v is ComposerRoot r) return r.platform;
            return DawPlatform.Current;
        }

        // ---- Composer 層
        public ComposerSession Session { get; }
        public Transport Transport { get; }
        public CellCommands Cells { get; }
        public RollEditor Roll { get; }
        public ComposerKeymap Keymap { get; }
        public ISongFiles Files { get; }
        public MixerCommands Mixer { get; }
        public OrderCommands Order { get; }
        public AssistCommands Assist { get; }
        public MasteringCommands Mastering { get; }
        public ComposerContext Context { get; }
        readonly MidiRecorder recorder;

        /// <summary>
        /// ウィンドウ全体のキー（<see cref="ComposerKeymap.WindowCommands"/>）を外で受け持つか。
        /// エディタでは Shortcut Manager が処理するので true、ゲーム内では false（ここで処理する）。
        /// </summary>
        public bool WindowCommandsHandledOutside { get; set; }

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
        TooltipOverlay tooltips;
        IPatternView CurrentView => Array.Find(views, v => v.Kind == state.view) ?? views[0];
        readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        bool disposed;

        /// <param name="files">セッションを受け取り、ホストの曲ファイル操作を作る（IDisposable なら <see cref="Dispose"/> で片付ける）。</param>
        /// <param name="platform">この画面のホスト（null なら <see cref="DawPlatform.Current"/>）。</param>
        public ComposerRoot(ComposerState state, Func<ComposerSession, ISongFiles> files, IDawPlatform platform = null)
        {
            this.state = state ?? new ComposerState();
            this.platform = platform ?? DawPlatform.Current;
            Transport = new Transport(this.state, null, this.platform);
            Session = new ComposerSession(this.state, Transport, this.platform);
            Cells = new CellCommands(Session);
            Roll = new RollEditor(Session);
            recorder = new MidiRecorder(Session);
            Files = files(Session);
            Mixer = new MixerCommands(Session);
            Order = new OrderCommands(Session);
            Assist = new AssistCommands(Session, Roll);
            Mastering = new MasteringCommands(Session);
            Keymap = new ComposerKeymap(Session, Cells, Roll, Order, Assist.Claude);
            Context = new ComposerContext(this);

            Build();

            Session.Changed += RefreshAll;
            Session.Notified += Notify;
            Transport.Changed += RefreshAll;
            this.platform.Update += Tick;
            this.platform.UndoRedoPerformed += Session.OnUndoRedo;

            Session.SetSong(this.state.song);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Session.Changed -= RefreshAll;
            Session.Notified -= Notify;
            Transport.Changed -= RefreshAll;
            platform.Update -= Tick;
            platform.UndoRedoPerformed -= Session.OnUndoRedo;
            Session.ReleaseMidiInput();
            Transport.Engine?.Stop();
            (Files as IDisposable)?.Dispose();
        }

        public ComposerState State => state;

        void Notify(string message) => statusBar?.ShowMessage(message);

        /// <summary>ステータスバーに出ている結果メッセージ。</summary>
        public string StatusMessage => statusBar?.Message;

        public void FocusMain() => mainArea?.Focus();

        /// <summary>キー処理。処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift, bool alt = false) => Keymap.HandleKey(k, ctrl, shift, alt);

        // =====================================================================
        // レイアウト
        // =====================================================================

        void Build()
        {
            AddToClassList("cd-root");
            style.flexGrow = 1;

            toolbar = new ComposerToolbar(Context);
            Add(toolbar);

            readOnlyBanner = new VisualElement();
            readOnlyBanner.AddToClassList("cd-banner");
            readOnlyBanner.Add(new Label("この Song は読み取り専用ですわ（MIDI ファイルから読み込んだままの曲など）。編集するには複製してくださいませ。"));
            readOnlyBanner.Add(new Button(Files.MakeEditableCopy) { text = "編集用に複製", tooltip = "この Song を編集できる曲として複製して開く" });
            Add(readOnlyBanner);

            body = new VisualElement();
            body.AddToClassList("cd-body");
            BuildCenter();
            patternList = new PatternListPanel(Context, arrangeBand);
            body.Add(patternList);
            rightPanel = new RightPanel(Context);
            leftSplit = new Splitter(patternList, PanelEdge.Left, () => ComposerState.MinSideWidth, () => MaxSide(rightPanel),
                w => Session.SetPanelSize(PanelEdge.Left, w), Session.ToggleLeftZone, "ドラッグで幅を変更・ダブルクリックで閉じる（Ctrl+Alt+L）");
            rightSplit = new Splitter(rightPanel, PanelEdge.Right, () => ComposerState.MinSideWidth, () => MaxSide(patternList),
                w => Session.SetPanelSize(PanelEdge.Right, w), Session.ToggleRightZone, "ドラッグで幅を変更・ダブルクリックで閉じる（Ctrl+Alt+R）");
            body.Add(leftSplit);
            body.Add(center);
            body.Add(rightSplit);
            body.Add(rightPanel);
            body.RegisterCallback<GeometryChangedEvent>(_ => ApplyLayout());
            Add(body);

            statusBar = new StatusBar(Context);
            Add(statusBar);
            keyHelp = new KeyHelpOverlay(Context);
            Add(keyHelp);

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            // Space / Enter は再生のショートカット。フォーカスが残った Button（▶ Audition 等）まで Submit で押されると
            // 再生が切り替わらず「無反応」に見えるので、文字入力中以外は Submit を止める（ボタンはクリックで押す）
            RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            // フォーカスを持てない所（アレンジ帯のブロック等）をクリックしてもフォーカスが画面の外へ抜けないよう、
            // 大枠にフォーカスを持たせる（クリックした所に一番近いフォーカス可能な親が受け取る）。Tab 移動の対象にはしない
            foreach (var frame in new VisualElement[] { toolbar, readOnlyBanner, body, statusBar, keyHelp })
            {
                frame.focusable = true;
                frame.tabIndex = -1;
            }
            tooltips = new TooltipOverlay(this, platform);
        }

        void BuildCenter()
        {
            center = new VisualElement();
            center.AddToClassList("cd-center");
            // Song が無いときだけ出す開始画面
            startScreen = new StartScreen(Context);
            startScreen.style.display = DisplayStyle.None;
            center.Add(startScreen);
            // 上：アレンジ帯（曲全体の並び）→ 見出し → パターン編集 → 下ゾーン
            arrangeBand = new ArrangeView(Context);
            center.Add(arrangeBand);
            bandSplit = new Splitter(arrangeBand, PanelEdge.Band, () => ComposerState.MinBandHeight,
                () => Mathf.Min(ComposerState.MaxBandHeight, mainArea.resolvedStyle.height + arrangeBand.resolvedStyle.height - PanelLayout.EditorMinHeight),
                h => Session.SetPanelSize(PanelEdge.Band, h), Session.ToggleBandZone, "ドラッグで高さを変更・ダブルクリックで閉じる（Ctrl+Alt+A）");
            center.Add(bandSplit);
            editorHeader = new EditorHeader(Context);
            center.Add(editorHeader);
            mainArea = new VisualElement { focusable = true };
            mainArea.AddToClassList("cd-main");
            mainArea.RegisterCallback<PointerDownEvent>(_ => FocusMain());
            // PatternViewKind の値の順に並べる
            views = new IPatternView[] { new TrackerView(Context), new StepView(Context), new PianoRollView(Context) };
            foreach (var v in views) mainArea.Add((VisualElement)v);

            // Cubase の Lower Zone：上 = パターン編集、下 = ミキサー / 楽器エディタ（境界はドラッグで調整）
            center.Add(mainArea);
            lowerZone = new LowerZone(Context);
            lowerSplit = new Splitter(lowerZone, PanelEdge.Lower, () => ComposerState.MinLowerHeight,
                () => Mathf.Min(ComposerState.MaxLowerHeight, mainArea.resolvedStyle.height + lowerZone.resolvedStyle.height - PanelLayout.EditorMinHeight),
                h => Session.SetPanelSize(PanelEdge.Lower, h), Session.ToggleLowerZone, "ドラッグで高さを変更・ダブルクリックで閉じる（Ctrl+Alt+E）");
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
            readOnlyBanner.style.display = Session.IsReadOnly ? DisplayStyle.Flex : DisplayStyle.None;
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

        /// <summary>毎フレームの更新（再生位置・メーター・ツールチップ等）。</summary>
        void Tick()
        {
            Session.SyncMidiInput();
            tooltips?.Tick();
            statusBar?.Tick();
            Mastering?.Tick();
            bool moved = Transport.Poll(out bool patternChanged);
            var e = Transport.Engine;
            if (e == null) return;
            recorder.Drain(e);
            toolbar?.UpdatePlayhead();
            lowerZone?.UpdatePlayback();
            if (visible.band) arrangeBand?.UpdatePlayhead();
            if (!moved || toolbar == null || state.song == null) return;

            int pp = e.IsPlaying ? e.PlayingPattern : -1;
            if (state.follow && pp >= 0 && pp != state.patternIndex) Session.FollowPlayback(pp, e.PlayingOrder);
            else if (patternChanged) RefreshAll();

            CurrentView.UpdatePlayhead();
            statusBar.Refresh();
            if (patternChanged) patternList.Refresh();
        }

        // =====================================================================
        // 入力
        // =====================================================================

        enum FocusKind { None, Control, Text }

        // 部品の型ではなく USS のクラスで見分ける（Integer/Long/Float/Double 等の入力欄・各種スライダーをまとめて拾える）
        static readonly string TextFieldClass = TextInputBaseField<string>.ussClassName;
        static readonly string[] ControlClasses =
        {
            BaseSlider<float>.ussClassName, BasePopupField<string, string>.ussClassName, EnumField.ussClassName,
            "unity-object-field", // エディタの ObjectField
        };

        /// <summary>フォーカス中の部品の種類（作曲画面の本体にフォーカスがあれば None）。</summary>
        FocusKind FocusedKind()
        {
            for (var v = focusController?.focusedElement as VisualElement; v != null; v = v.parent)
            {
                if (v == mainArea) return FocusKind.None;
                if (v.ClassListContains(TextFieldClass)) return FocusKind.Text;
                foreach (var c in ControlClasses)
                    if (v.ClassListContains(c)) return FocusKind.Control;
            }
            return FocusKind.None;
        }

        /// <summary>
        /// フォーカス中の部品にキーを譲るか。文字入力欄はすべて、スライダー等の操作部品は Space（再生/停止）以外を譲る。
        /// </summary>
        bool FocusTakesKey(KeyCode key)
        {
            var kind = FocusedKind();
            return kind == FocusKind.Text || (kind == FocusKind.Control && key != KeyCode.Space);
        }

        /// <summary>文字入力欄にフォーカスがある（キーはすべて入力欄へ渡す）。</summary>
        public bool IsTyping => FocusedKind() == FocusKind.Text;

        void OnSubmit(NavigationSubmitEvent ev)
        {
            if (!IsTyping) ev.StopImmediatePropagation();
        }

        void OnKeyUp(KeyUpEvent ev)
        {
            if (!held.Remove(ev.keyCode)) return;
            if (ComposerKeymap.PianoOffset(ev.keyCode) >= 0 && state.song != null) Transport.ReleasePreview(state.cursorCh);
        }

        void OnKeyDown(KeyDownEvent ev)
        {
            if (ev.keyCode == KeyCode.None || FocusTakesKey(ev.keyCode)) return;
            // ウィンドウ全体のキーを外（エディタの Shortcut Manager）が受け持つなら、ここで処理すると二重に効く
            if (WindowCommandsHandledOutside && ComposerKeymap.IsWindowCommand(ev.keyCode, ev.actionKey, ev.shiftKey, ev.altKey)) return;
            if (HandleKey(ev.keyCode, ev.actionKey, ev.shiftKey, ev.altKey))
            {
                held.Add(ev.keyCode);
                ev.StopImmediatePropagation();
            }
        }
    }
}
