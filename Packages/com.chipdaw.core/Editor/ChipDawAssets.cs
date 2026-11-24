using System.IO;
using ChipDaw.Midi;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChipDaw.Editor
{
    /// <summary>アセット生成・入出力の共通処理（ウィンドウと CLI から使う）。</summary>
    public static class ChipDawAssets
    {
        public const string DemoDir = "Assets/ChipDaw/Demo";
        public const string DemoSongPath = DemoDir + "/DemoSong.asset";
        public const string DemoMidiPath = DemoDir + "/Demo.mid";
        public const string DemoFromMidiPath = DemoDir + "/DemoFromMidi.asset";
        public const string DemoScenePath = DemoDir + "/ChipDawDemo.unity";

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
            var copy = Object.Instantiate(src);
            copy.name = Path.GetFileNameWithoutExtension(assetPath);
            return SaveSong(copy, assetPath);
        }

        public static bool IsReadOnly(Song song)
        {
            if (song == null) return false;
            var path = AssetDatabase.GetAssetPath(song);
            return path.EndsWith(".mid", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", System.StringComparison.OrdinalIgnoreCase);
        }

        public static Song ImportMidiFile(string midiPath, MidiImportOptions opt, out MidiImportReport report)
        {
            var bytes = File.ReadAllBytes(midiPath);
            var song = MidiToSong.Import(bytes, opt, Path.GetFileNameWithoutExtension(midiPath), out report);
            song.name = Path.GetFileNameWithoutExtension(midiPath);
            return song;
        }

        public static void ExportWav(Song song, string path, int sampleRate = 48000)
        {
            var mono = ChipEngine.RenderSong(song, sampleRate);
            WavWriter.WriteStereo16(path, mono, sampleRate);
        }

        public static void ExportMidi(Song song, string path)
        {
            var bytes = SongToMidi.Export(song);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>デモ曲・デモ MIDI・MIDI 変換結果・デモシーンを生成。</summary>
        public static Song CreateDemo(bool createScene = true)
        {
            EnsureFolder(DemoDir);
            var demo = SaveSong(DemoSongs.BuildDemoSong(), DemoSongPath);

            var midiBytes = DemoSongs.BuildDemoMidi();
            var full = Path.GetFullPath(DemoMidiPath);
            File.WriteAllBytes(full + ".tmp", midiBytes);
            if (File.Exists(full)) File.Delete(full);
            File.Move(full + ".tmp", full);
            AssetDatabase.ImportAsset(DemoMidiPath);

            var fromMidi = MidiToSong.Import(midiBytes, new MidiImportOptions(), "Demo (from MIDI)", out var report);
            fromMidi.name = "DemoFromMidi";
            SaveSong(fromMidi, DemoFromMidiPath);
            Debug.Log("[ChipDaw] Demo MIDI import: " + report);

            if (createScene) CreateDemoScene(demo);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // シーン切替でメモリ上の参照が差し替わることがあるので読み直して返す
            return AssetDatabase.LoadAssetAtPath<Song>(DemoSongPath);
        }

        public static void CreateDemoScene(Song song)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0, 1.5f, -8f);
                cam.transform.rotation = Quaternion.Euler(8f, 0, 0);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
            }

            var playerGo = new GameObject("ChipDaw Player", typeof(AudioSource), typeof(ChipDawPlayer));
            var player = playerGo.GetComponent<ChipDawPlayer>();
            player.song = song;
            player.playOnStart = true;

            var vis = new GameObject("ChipDaw Visualizer").AddComponent<ChipDawVisualizer>();
            vis.player = player;
            vis.channelTargets = new Transform[ChipEngine.Channels];
            for (int c = 0; c < ChipEngine.Channels; c++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = Song.ChannelNames[c];
                cube.transform.SetParent(vis.transform, false);
                cube.transform.localPosition = new Vector3(-3f + c * 2f, 0, 0);
                vis.channelTargets[c] = cube.transform;
            }
            EditorSceneManager.SaveScene(scene, DemoScenePath);
        }
    }
}
