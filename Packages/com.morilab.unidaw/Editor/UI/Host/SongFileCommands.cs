using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniDAW.Composer;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// ファイル操作（新規・デモ・MIDI 入出力・WAV 書き出し・複製）。保存先ダイアログを出し、
    /// 実処理は <see cref="UniDAWAssets"/> に任せ、結果の曲をセッションに渡す。
    /// </summary>
    public sealed class SongFileCommands : SongFilesBase
    {
        public SongFileCommands(ComposerSession session) : base(session) { }

        public override IReadOnlyList<SongEntry> ListSongs() =>
            UniDAWAssets.FindSongPaths()
                .Select(p => new SongEntry(p, Path.GetFileNameWithoutExtension(p), Path.GetDirectoryName(p).Replace('\\', '/')))
                .ToList();

        public override void Open(string id) => s.SetSong(AssetDatabase.LoadAssetAtPath<Song>(id));

        public override string IdOf(Song song)
        {
            var path = song != null ? AssetDatabase.GetAssetPath(song) : null;
            return string.IsNullOrEmpty(path) ? null : path;
        }

        public override bool CanReveal => true;
        public override void Reveal(Song song) => EditorGUIUtility.PingObject(song);

        public static bool IsMidiPath(string path) =>
            path != null && (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));

        public override void NewSong()
        {
            var path = EditorUtility.SaveFilePanelInProject("New Song", "NewSong", "asset", "Song の保存先");
            if (string.IsNullOrEmpty(path)) return;
            var song = Song.CreateDefault(64);
            song.title = Path.GetFileNameWithoutExtension(path);
            s.SetSong(UniDAWAssets.SaveSong(song, path));
        }

        /// <summary>プロジェクト内の Song アセットをファイルダイアログで選んで開く。</summary>
        public override void OpenSong()
        {
            var path = EditorUtility.OpenFilePanel("Open Song", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(path)) return;
            var rel = FileUtil.GetProjectRelativePath(path.Replace('\\', '/'));
            var song = string.IsNullOrEmpty(rel) ? null : AssetDatabase.LoadAssetAtPath<Song>(rel);
            if (song == null)
            {
                s.Notify("プロジェクト内の Song アセットを選んでくださいませ");
                return;
            }
            s.SetSong(song);
        }

        /// <summary>
        /// パッケージの Samples~/Demo を Assets/Samples/UniDAW/&lt;version&gt;/Demo に取り込み、デモ曲を開く。
        /// 埋め込み・ローカルのパッケージは Package Manager に Import ボタンが出ないため、自前でコピーする。
        /// 取り込み済みのファイルは上書きせず、足りないもの（新しいデモ曲など）だけ足す。
        /// </summary>
        public override void OpenSamples()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SongFileCommands).Assembly);
            var src = info == null ? null : Path.Combine(info.resolvedPath, "Samples~", "Demo");
            if (src == null || !Directory.Exists(src))
            {
                UnityEditor.PackageManager.UI.Window.Open(PackageName);
                return;
            }
            var dst = DemoFolder(info.version);
            int added = CopyMissing(src, dst);
            AssetDatabase.Refresh();
            var song = AssetDatabase.LoadAssetAtPath<Song>(dst + "/DemoSong.asset");
            if (song != null) s.SetSong(song);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dst));
            s.Notify(added > 0 ? $"デモを {dst} に取り込みましたわ（{added} ファイル）" : $"デモは取り込み済みですわ（{dst}）");
        }

        /// <summary>前に取り込んだ Demo があればそこ（GUID が重複しないように）、無ければ今のバージョンのフォルダ。</summary>
        public static string DemoFolder(string version)
        {
            const string root = "Assets/Samples/UniDAW";
            if (Directory.Exists(root))
                foreach (var d in Directory.GetDirectories(root))
                    if (Directory.Exists(Path.Combine(d, "Demo")))
                        return $"{root}/{Path.GetFileName(d)}/Demo";
            return $"{root}/{version}/Demo";
        }

        /// <summary>src 以下のファイルのうち dst に無いものだけコピーする。コピーした数を返す。</summary>
        public static int CopyMissing(string src, string dst)
        {
            int n = 0;
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dst, Path.GetRelativePath(src, file));
                if (File.Exists(to)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(file, to);
                n++;
            }
            return n;
        }

        public const string PackageName = "com.morilab.unidaw";

        protected override void PickImportFile(string title, string what, string[] exts, Action<string> picked)
        {
            var path = EditorUtility.OpenFilePanel(title, "", string.Join(",", exts.Select(e => e.TrimStart('.'))));
            if (!string.IsNullOrEmpty(path)) picked(path);
        }

        /// <summary>プロジェクト内のアセットとして保存する（同じパスの Song があれば中身を差し替える）。</summary>
        protected override Song Store(Song song, string suggestedName, string title)
        {
            var dst = EditorUtility.SaveFilePanelInProject(title, suggestedName, "asset", "Song の保存先");
            return string.IsNullOrEmpty(dst) ? null : UniDAWAssets.SaveSong(song, dst);
        }

        protected override void PickExportPath(string title, string baseName, string ext, Action<string> picked)
        {
            var path = EditorUtility.SaveFilePanel(title, "", baseName + ext, ext.TrimStart('.'));
            if (!string.IsNullOrEmpty(path)) picked(path);
        }

        protected override void PickExportFolder(string title, string baseName, Action<string> picked)
        {
            var dir = EditorUtility.SaveFolderPanel(title, "", "");
            if (!string.IsNullOrEmpty(dir)) picked(dir);
        }

        protected override void ReportError(string message) => EditorUtility.DisplayDialog("UniDAW", message, "OK");

        protected override void RunWithProgress(string text, Action work)
        {
            try
            {
                EditorUtility.DisplayProgressBar("UniDAW", text, 0.5f);
                work();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
