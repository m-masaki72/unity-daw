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
    public sealed class SongFileCommands : ISongFiles
    {
        readonly ComposerSession s;

        public SongFileCommands(ComposerSession session) => s = session;

        public IReadOnlyList<SongEntry> ListSongs() =>
            UniDAWAssets.FindSongPaths()
                .Select(p => new SongEntry(p, Path.GetFileNameWithoutExtension(p), Path.GetDirectoryName(p).Replace('\\', '/')))
                .ToList();

        public void Open(string id) => s.SetSong(AssetDatabase.LoadAssetAtPath<Song>(id));

        public string IdOf(Song song)
        {
            var path = song != null ? AssetDatabase.GetAssetPath(song) : null;
            return string.IsNullOrEmpty(path) ? null : path;
        }

        public bool CanReveal => true;
        public void Reveal(Song song) => EditorGUIUtility.PingObject(song);

        public static bool IsMidiPath(string path) =>
            path != null && (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));

        public void NewSong()
        {
            var path = EditorUtility.SaveFilePanelInProject("New Song", "NewSong", "asset", "Song の保存先");
            if (string.IsNullOrEmpty(path)) return;
            var song = Song.CreateDefault(64);
            song.title = Path.GetFileNameWithoutExtension(path);
            s.SetSong(UniDAWAssets.SaveSong(song, path));
        }

        /// <summary>プロジェクト内の Song アセットをファイルダイアログで選んで開く。</summary>
        public void OpenSong()
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
        public void OpenSamples()
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

        public void ImportMidi(VoiceAssignMode mode)
        {
            var path = EditorUtility.OpenFilePanel("Import MIDI", "", "mid,midi");
            if (!string.IsNullOrEmpty(path)) ImportMidiFrom(path, mode);
        }

        public void ImportMidiFrom(string path, VoiceAssignMode mode)
        {
            try
            {
                var song = UniDAWAssets.ImportMidiFile(path, new MidiImportOptions { mode = mode }, out var report);
                var dst = EditorUtility.SaveFilePanelInProject("Save Imported Song", Path.GetFileNameWithoutExtension(path), "asset", "変換した Song の保存先");
                if (string.IsNullOrEmpty(dst))
                {
                    UnityEngine.Object.DestroyImmediate(song);
                    return;
                }
                s.SetSong(UniDAWAssets.SaveSong(song, dst));
                Debug.Log($"[UniDAW] MIDI import {Path.GetFileName(path)}: {report}");
                s.Notify($"インポート完了: {report.patterns} patterns, dropped {report.droppedNotes}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("UniDAW", "MIDI の読み込みに失敗しましたわ…\n" + ex.Message, "OK");
            }
        }

        /// <summary>FamiTracker の「Export text」（.txt）を読み、Song アセットとして保存して開く。</summary>
        public void ImportFamiTracker()
        {
            var path = EditorUtility.OpenFilePanel("Import FamiTracker Text", "", "txt");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var song = FamiTrackerImport.Import(FamiTrackerImport.Decode(File.ReadAllBytes(path)), name, out var report);
                var dst = EditorUtility.SaveFilePanelInProject("Save Imported Song", name, "asset", "変換した Song の保存先");
                if (string.IsNullOrEmpty(dst))
                {
                    UnityEngine.Object.DestroyImmediate(song);
                    return;
                }
                s.SetSong(UniDAWAssets.SaveSong(song, dst));
                Debug.Log($"[UniDAW] FamiTracker import {Path.GetFileName(path)}: {report}");
                s.Notify("インポート完了: " + report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("UniDAW", "FamiTracker テキストの読み込みに失敗しましたわ…\n" + ex.Message, "OK");
            }
        }

        string DefaultFileName(string ext) => (s.Song.title ?? s.Song.name) + ext;

        public void ExportWav()
        {
            if (s.Song == null) return;
            var path = EditorUtility.SaveFilePanel("Export WAV", "", DefaultFileName(".wav"), "wav");
            if (string.IsNullOrEmpty(path)) return;
            UniDAWAssets.ExportWav(s.Song, path);
            s.Notify("WAV を書き出しましたわ");
        }

        public void ExportStems()
        {
            if (s.Song == null) return;
            var dir = EditorUtility.SaveFolderPanel("Export Stems（チャンネル別 WAV）", "", "");
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                EditorUtility.DisplayProgressBar("UniDAW", "ステムを書き出しています…", 0.5f);
                var paths = UniDAWAssets.ExportStems(s.Song, dir, SafeName(s.Song.title ?? s.Song.name));
                s.Notify($"ステムを {paths.Length} 本書き出しましたわ");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Song";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public void ExportMidi()
        {
            if (s.Song == null) return;
            var path = EditorUtility.SaveFilePanel("Export MIDI", "", DefaultFileName(".mid"), "mid");
            if (string.IsNullOrEmpty(path)) return;
            UniDAWAssets.ExportMidi(s.Song, path);
            s.Notify("MIDI を書き出しましたわ");
        }

        public void MakeEditableCopy()
        {
            if (s.Song == null) return;
            var path = EditorUtility.SaveFilePanelInProject("Editable Copy", s.Song.name, "asset", "編集用 Song の保存先");
            if (!string.IsNullOrEmpty(path)) s.SetSong(UniDAWAssets.MakeEditableCopy(s.Song, path));
        }
    }
}
