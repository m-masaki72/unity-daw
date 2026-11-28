using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>アセット生成・入出力の共通処理（ウィンドウと CLI から使う）。</summary>
    public static class UniDAWAssets
    {
        /// <summary>プロジェクト内の Song アセットをパス順に列挙する。</summary>
        public static string[] FindSongPaths()
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:" + nameof(Song))) paths.Add(AssetDatabase.GUIDToAssetPath(g));
            paths.Sort(System.StringComparer.OrdinalIgnoreCase);
            return paths.ToArray();
        }

        static void EnsureFolder(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;
            var parent = Path.GetDirectoryName(assetDir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetDir));
        }

        /// <summary>既存アセットがあれば中身を差し替え（参照を保つ）、無ければ作成。</summary>
        public static Song SaveSong(Song song, string assetPath)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Song>(assetPath);
            if (existing != null && AssetDatabase.IsMainAsset(existing) && !AssetDatabase.GetAssetPath(existing).EndsWith(".mid"))
            {
                Undo.RecordObject(existing, "Replace Song");
                EditorUtility.CopySerialized(song, existing);
                existing.name = Path.GetFileNameWithoutExtension(assetPath);
                Object.DestroyImmediate(song);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }
            AssetDatabase.CreateAsset(song, assetPath);
            AssetDatabase.SaveAssets();
            return song;
        }

        /// <summary>読み取り専用（MIDI インポート結果など）の Song を編集可能アセットとして複製。</summary>
        public static Song MakeEditableCopy(Song src, string assetPath)
        {
            var copy = SongIO.Clone(src);
            copy.name = Path.GetFileNameWithoutExtension(assetPath);
            return SaveSong(copy, assetPath);
        }

        public static bool IsReadOnly(Song song)
        {
            if (song == null) return false;
            var path = AssetDatabase.GetAssetPath(song);
            return path.EndsWith(".mid", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", System.StringComparison.OrdinalIgnoreCase);
        }

        // 中身は SongIO（ホストに依存しない読み書き）。既存のスクリプト向けに残す
        public static Song ImportMidiFile(string midiPath, UniDAW.Midi.MidiImportOptions opt, out UniDAW.Midi.MidiImportReport report) => SongIO.ImportMidi(midiPath, opt, out report);
        public static void ExportWav(Song song, string path, int sampleRate = SongIO.DefaultSampleRate) => SongIO.ExportWav(song, path, sampleRate);
        public static string[] ExportStems(Song song, string dir, string baseName, int sampleRate = SongIO.DefaultSampleRate) => SongIO.ExportStems(song, dir, baseName, sampleRate);
        public static void ExportMidi(Song song, string path) => SongIO.ExportMidi(song, path);
    }
}
