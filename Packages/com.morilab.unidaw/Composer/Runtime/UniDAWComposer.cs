using System.IO;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// ゲーム内 DAW：エディタと同じコンポーザー画面（<see cref="ComposerRoot"/>）を UIDocument に出す。
    /// 曲は <see cref="Application.persistentDataPath"/>/UniDAW/Songs に JSON で自動保存、音は同じ GameObject の <see cref="UniDAWPlayer"/> から出す。
    /// Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z）で Undo / Redo。
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(AudioSource), typeof(UniDAWPlayer))]
    [AddComponentMenu("UniDAW/UniDAW Composer (In-Game)")]
    public sealed class UniDAWComposer : MonoBehaviour
    {
        public const string StyleSheetPath = "Packages/com.morilab.unidaw/Composer/UI/UniDAW.uss";
        public const string ThemePath = "Packages/com.morilab.unidaw/Composer/Runtime/UniDAWRuntimeTheme.tss";

        [Tooltip("コンポーザーの見た目（UniDAW.uss）")]
        public StyleSheet styleSheet;
        [Tooltip("UIDocument に PanelSettings が無いときに作るパネルのテーマ")]
        public ThemeStyleSheet theme;
        [Tooltip("日本語を表示できるフォント。空なら OS のフォント（Yu Gothic UI / Meiryo / Hiragino Sans 等）から作る")]
        public FontAsset font;
        [Tooltip("曲・取り込み・書き出しの置き場所。空なら persistentDataPath/UniDAW")]
        public string dataFolder;
        [Tooltip("起動時に前回の曲を開く")]
        public bool openLastSong = true;

        static readonly string[] SystemFonts = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Noto Sans CJK JP", "Noto Sans JP" };

        RuntimeDawPlatform platform;
        IDawPlatform previous;
        RuntimeSongFiles files;
        ComposerRoot composer;
        PanelSettings createdPanel;

        public ComposerRoot Composer => composer;
        public RuntimeDawPlatform Platform => platform;
        public RuntimeSongFiles Files => files;

        void Reset() => FillDefaults();
        void OnValidate() => FillDefaults();

        void FillDefaults()
        {
#if UNITY_EDITOR
            if (styleSheet == null) styleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (theme == null) theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
#endif
        }

        void Start()
        {
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
            {
                createdPanel = ScriptableObject.CreateInstance<PanelSettings>();
                createdPanel.name = "UniDAW Panel";
                createdPanel.themeStyleSheet = theme;
                createdPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                doc.panelSettings = createdPanel;
            }

            var root = string.IsNullOrEmpty(dataFolder) ? Path.Combine(Application.persistentDataPath, "UniDAW") : dataFolder;
            platform = new RuntimeDawPlatform(new PlayerAudioHost(GetComponent<UniDAWPlayer>()), root);
            previous = DawPlatform.Current;
            DawPlatform.Current = platform;

            var state = new ComposerState();
            composer = new ComposerRoot(state, s => files = new RuntimeSongFiles(s, platform, root));
            platform.Dirtied += files.MarkDirty;

            var ui = doc.rootVisualElement;
            if (styleSheet != null) ui.styleSheets.Add(styleSheet);
            var f = font != null ? font : FindSystemFont();
            if (f != null) ui.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(f));
            ui.Add(composer);
            // メニューの基準は画面そのもの（破棄で先に外れ、開いているメニューも一緒に閉じる）
            platform.MenuRoot = composer;
            platform.TrackPointer(ui);
            ui.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            if (openLastSong) files.OpenLast();
            composer.RefreshAll();
            composer.FocusMain();
        }

        static FontAsset FindSystemFont()
        {
            foreach (var name in SystemFonts)
            {
                var fa = FontAsset.CreateFontAsset(name, "Regular");
                if (fa != null) return fa;
            }
            return null;
        }

        void OnKeyDown(KeyDownEvent ev)
        {
            if (!ev.actionKey || composer == null || composer.IsTyping) return;
            bool done = ev.keyCode == KeyCode.Z && !ev.shiftKey ? platform.Undo()
                : ev.keyCode == KeyCode.Y || (ev.keyCode == KeyCode.Z && ev.shiftKey) ? platform.Redo()
                : false;
            if (done || ev.keyCode == KeyCode.Z || ev.keyCode == KeyCode.Y) ev.StopImmediatePropagation();
        }

        void Update() => platform?.Tick();

        void OnApplicationPause(bool paused)
        {
            if (paused) files?.SaveAll();
        }

        void OnDestroy()
        {
            if (platform == null) return;
            platform.Unload();
            composer?.Dispose();
            composer?.RemoveFromHierarchy();
            if (DawPlatform.Current == platform) DawPlatform.Current = previous;
            if (createdPanel != null)
            {
                if (Application.isPlaying) Destroy(createdPanel);
                else DestroyImmediate(createdPanel);
            }
            platform = null;
        }
    }
}
