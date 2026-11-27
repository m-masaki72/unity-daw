using System.IO;
using System.Linq;
using UniDAW.Composer;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// UniDAW コンポーザーのエディタウィンドウ。画面そのものは <see cref="ComposerRoot"/>（ゲーム内と共通）で、
    /// ここはエディタ固有の配線（スタイルシート・Shortcut Manager・アセットのドラッグ＆ドロップ）だけを受け持つ。
    /// </summary>
    public class UniDAWWindow : EditorWindow
    {
        public const string PackageRoot = "Packages/com.morilab.unidaw";
        public const string StyleSheetPath = PackageRoot + "/Composer/UI/UniDAW.uss";

        [SerializeField] ComposerState state = new ComposerState();

        ComposerRoot composer;
        SongFileCommands files;

        // ---- テスト・外部向けの窓口
        public ComposerRoot Composer => composer;
        public Song Song => state.song;
        public Pattern CurrentPattern => state.CurrentPattern;
        public int CursorRow => state.cursorRow;
        public int CursorChannel => state.cursorCh;
        public int CursorCol => state.cursorCol;

        public void SetSong(Song s) => composer.Session.SetSong(s);
        public void SetCursor(int row, int ch, int col) => composer.Session.SetCursor(row, ch, col);
        public void SetTrackLength(int ch, int len) => composer.Session.SetTrackLength(ch, len);
        public void StepClick(int ch, int row, bool toggle) => composer.Cells.StepClick(ch, row, toggle);
        public RollEditor Roll => composer.Roll;
        public Transport Transport => composer.Transport;
        public MixerCommands Mixer => composer.Mixer;
        public OrderCommands Order => composer.Order;
        public AssistCommands Assist => composer.Assist;
        public ComposerSession Session => composer.Session;
        public ComposerState State => state;
        public string FxHelpAtCursor() => composer.Session.FxHelpAtCursor();
        public void RefreshAll() => composer.RefreshAll();

        /// <summary>キー処理（テストからも呼べるよう public）。処理したら true。</summary>
        public bool HandleKey(KeyCode k, bool ctrl, bool shift, bool alt = false) => composer.HandleKey(k, ctrl, shift, alt);

        /// <summary>Shortcut Manager から：ウィンドウ全体のキー（<see cref="ComposerKeymap.WindowCommands"/>）を実行する。</summary>
        internal void RunWindowCommand(KeyCode k, bool ctrl, bool alt) => composer.Keymap.HandleKey(k, ctrl, false, alt);

        /// <summary>文字入力欄にフォーカスがある（キーはすべて入力欄へ渡す）。</summary>
        internal bool IsTyping => composer != null && composer.IsTyping;

        /// <summary>ステータスバーに出ている結果メッセージ（テスト用）。</summary>
        public string StatusMessage => composer?.StatusMessage;

        UniDAWShortcuts.Context shortcutContext;

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
            composer = new ComposerRoot(state, s => files = new SongFileCommands(s)) { WindowCommandsHandledOutside = true };
            shortcutContext = new UniDAWShortcuts.Context(this);
            UnityEditor.ShortcutManagement.ShortcutManager.RegisterContext(shortcutContext);
        }

        void OnDisable()
        {
            UnityEditor.ShortcutManagement.ShortcutManager.UnregisterContext(shortcutContext);
            composer?.Dispose();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            AddStyleSheet(root);
            root.Add(composer);
            root.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            root.RegisterCallback<DragPerformEvent>(OnDragPerform);
            composer.RefreshAll();
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

        // =====================================================================
        // ドラッグ＆ドロップ（Song アセット・MIDI ファイル）
        // =====================================================================

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
