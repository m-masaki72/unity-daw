using System;
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
    /// ゲームからは <see cref="OpenSong"/> / <see cref="OpenFile"/> で曲を開き、<see cref="PlaySong"/> / <see cref="Stop"/> で鳴らし、
    /// <see cref="Visible"/> で出し入れする（Start より前に呼んでもよい）。
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(AudioSource), typeof(UniDAWPlayer))]
    [AddComponentMenu("UniDAW/UniDAW Composer (In-Game)")]
    public sealed class UniDAWComposer : MonoBehaviour
    {
        public const string StyleSheetPath = "Packages/com.morilab.unidaw/Composer/UI/UniDAW.uss";
        public const string ThemePath = "Packages/com.morilab.unidaw/Composer/Runtime/UniDAWRuntimeTheme.tss";
        public const string PanelPath = "Packages/com.morilab.unidaw/Composer/Runtime/UniDAWPanel.asset";

        public enum TouchButtonMode { Auto, Always, Never }

        [Tooltip("コンポーザーの見た目（UniDAW.uss）")]
        public StyleSheet styleSheet;
        [Tooltip("UIDocument に PanelSettings が無いときに作るパネルのテーマ")]
        public ThemeStyleSheet theme;
        [Tooltip("UIDocument に PanelSettings が無いときに複製する元（日本語の改行に使う ICU データ入り）。空なら素のパネルを作る")]
        public PanelSettings panelTemplate;
        [Tooltip("画面全体のフォントを差し替えるとき。空なら同梱の BIZ UDPGothic（トラッカーは JetBrains Mono）")]
        public FontAsset font;
        [Tooltip("曲・取り込み・書き出しの置き場所。空なら persistentDataPath/UniDAW")]
        public string dataFolder;
        [Tooltip("起動時に前回の曲を開く")]
        public bool openLastSong = true;
        [Tooltip("起動時に開く曲（複製して開き、元のアセットは書き換えない）。前回の曲より優先")]
        public Song startSong;
        [Tooltip("画面の拡大率。0 なら画面の DPI から決める（PanelSettings を自分で作ったときだけ効く）")]
        [Min(0f)] public float uiScale;
        [Tooltip("タッチで右クリックを使うボタン。Auto はモバイルのときだけ出す")]
        public TouchButtonMode touchRightClick = TouchButtonMode.Auto;

        RuntimeDawPlatform platform;
        IDawPlatform previous;
        bool installed;
        RuntimeSongFiles files;
        ComposerRoot composer;
        PanelSettings createdPanel;
        TouchRightClick touch;
        bool visible = true;
        bool destroyed;

        public ComposerRoot Composer => Init() ? composer : null;
        public RuntimeDawPlatform Platform => Init() ? platform : null;
        public RuntimeSongFiles Files => Init() ? files : null;
        public TouchRightClick Touch => touch;

        /// <summary>いま開いている曲。</summary>
        public Song CurrentSong => composer?.Session.Song;

        /// <summary>曲が書き換えられたとき（自動保存の前）。</summary>
        public event Action<Song> SongModified;

        void Reset() => FillDefaults();
        void OnValidate() => FillDefaults();

        void FillDefaults()
        {
#if UNITY_EDITOR
            if (styleSheet == null) styleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (theme == null) theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (panelTemplate == null) panelTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
#endif
        }

        void Start()
        {
            bool fresh = composer == null;
            if (!Init() || !fresh) return;
            if (startSong != null) files.OpenCopy(startSong);
            else if (openLastSong) files.OpenLast();
            composer.RefreshAll();
            composer.FocusMain();
        }

        /// <summary>画面を作る（2 回目からは何もしない）。破棄の後は false。</summary>
        bool Init()
        {
            if (composer != null) return true;
            if (destroyed || this == null) return false;
            var doc = GetComponent<UIDocument>();
            if (doc.panelSettings == null)
            {
                // 実行時に素で作ったパネルには ICU データが入らない（日本語の改行が簡易になる）ので、アセットを複製する
                createdPanel = panelTemplate != null ? Instantiate(panelTemplate) : ScriptableObject.CreateInstance<PanelSettings>();
                createdPanel.name = "UniDAW Panel";
                if (theme != null) createdPanel.themeStyleSheet = theme;
                createdPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                createdPanel.scale = uiScale > 0 ? uiScale : AutoScale();
                doc.panelSettings = createdPanel;
            }

            var root = string.IsNullOrEmpty(dataFolder) ? Path.Combine(Application.persistentDataPath, "UniDAW") : dataFolder;
            platform = new RuntimeDawPlatform(new PlayerAudioHost(GetComponent<UniDAWPlayer>()), root);
            // 共通設定（キー割り当て等）の置き場所。エディタではエディタのホストが入っているのでそのまま使う
            installed = !DawPlatform.IsInstalled;
            if (installed)
            {
                previous = DawPlatform.Current;
                DawPlatform.Current = platform;
            }

            var state = new ComposerState();
            composer = new ComposerRoot(state, s => files = new RuntimeSongFiles(s, platform, root), platform);
            composer.Session.SongModified += RaiseSongModified;

            var ui = doc.rootVisualElement;
            if (styleSheet != null) ui.styleSheets.Add(styleSheet);
            // 既定のフォントは UniDAW.uss が同梱のものを指定する
            if (font != null) ui.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(font));
            ui.Add(composer);
            // メニューの基準は画面そのもの（破棄で先に外れ、開いているメニューも一緒に閉じる）
            platform.MenuRoot = composer;
            platform.TrackPointer(ui);
            ui.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            if (touchRightClick == TouchButtonMode.Always || (touchRightClick == TouchButtonMode.Auto && Application.isMobilePlatform))
                touch = new TouchRightClick(composer);
            ApplyVisible();
            return true;
        }

        void RaiseSongModified(Song song) => SongModified?.Invoke(song);

        /// <summary>画面の DPI から拡大率を決める（デスクトップは 96 dpi、モバイルは 160 dpi を 1 倍とする）。</summary>
        public static float AutoScale()
        {
            float dpi = Screen.dpi;
            if (dpi <= 0) return 1f;
            float scale = dpi / (Application.isMobilePlatform ? 160f : 96f);
            return Mathf.Clamp(Mathf.Round(scale * 4f) / 4f, 1f, 4f);
        }

        /// <summary>画面の拡大率（UIDocument に PanelSettings を指定していないときだけ変えられる）。</summary>
        public float UiScale
        {
            get => createdPanel != null ? createdPanel.scale : GetComponent<UIDocument>().panelSettings?.scale ?? 1f;
            set
            {
                uiScale = Mathf.Max(0f, value);
                if (createdPanel != null) createdPanel.scale = uiScale > 0 ? uiScale : AutoScale();
            }
        }

        /// <summary>画面を出すか（隠しても曲と再生はそのまま）。</summary>
        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                ApplyVisible();
            }
        }

        void ApplyVisible()
        {
            if (composer == null) return;
            composer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) composer.FocusMain();
            else files?.SaveAll();
        }

        // ---- ゲームから使う

        /// <summary>
        /// ゲームの曲（アセット）を複製して開く（元は書き換えない）。<paramref name="keepEdits"/> なら、
        /// 前に同じ名前で開いた曲があればそれを開く（プレイヤーの編集を残す）。開いた曲を返す。
        /// </summary>
        public Song OpenSong(Song song, string name = null, bool keepEdits = true)
        {
            if (!Init()) return null;
            var opened = files.OpenCopy(song, name, keepEdits);
            composer.RefreshAll();
            return opened;
        }

        /// <summary>保存済みの曲（Songs フォルダの .json）を開く。開けたら true。</summary>
        public bool OpenFile(string path)
        {
            if (!Init() || string.IsNullOrEmpty(path)) return false;
            var full = path;
            files.Open(full);
            composer.RefreshAll();
            return CurrentSong != null && files.IdOf(CurrentSong) == full;
        }

        /// <summary>曲の頭から鳴らす。</summary>
        public void PlaySong()
        {
            if (Init() && CurrentSong != null) composer.Transport.PlaySong();
        }

        /// <summary>今のパターンだけを繰り返し鳴らす。</summary>
        public void PlayPattern()
        {
            if (Init() && CurrentSong != null) composer.Transport.PlayPattern();
        }

        public void Stop() => composer?.Transport.Stop();

        public bool IsPlaying => composer != null && composer.Transport.IsPlaying;

        /// <summary>保存し残しをすぐ書く（シーンを切り替える前など）。</summary>
        public void Save() => files?.SaveAll();

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
            destroyed = true;
            if (platform == null) return;
            platform.Unload();
            if (composer != null) composer.Session.SongModified -= RaiseSongModified;
            composer?.Dispose();
            composer?.RemoveFromHierarchy();
            if (installed && DawPlatform.Current == platform) DawPlatform.Current = previous;
            if (createdPanel != null)
            {
                if (Application.isPlaying) Destroy(createdPanel);
                else DestroyImmediate(createdPanel);
            }
            platform = null;
            composer = null;
        }
    }
}
