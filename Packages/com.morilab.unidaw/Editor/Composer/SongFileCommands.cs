using System;
using System.IO;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// ファイル操作（新規・デモ・MIDI 入出力・WAV 書き出し・複製）。保存先ダイアログを出し、
    /// 実処理は <see cref="UniDAWAssets"/> に任せ、結果の曲をセッションに渡す。
    /// </summary>
    public sealed class SongFileCommands
    {
        readonly ComposerSession s;

        public SongFileCommands(ComposerSession session) => s = session;

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

        public void CreateDemo()
        {
            bool scene = EditorUtility.DisplayDialog("UniDAW", "デモシーン（映像連携のサンプル）も作成しますか？\n現在のシーンは閉じられます。", "作成する", "曲だけ");
            s.SetSong(UniDAWAssets.CreateDemo(createScene: scene));
        }

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
