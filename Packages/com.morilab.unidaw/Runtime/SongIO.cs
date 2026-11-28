using System.IO;
using UniDAW.Midi;
using UnityEngine;

namespace UniDAW
{
    /// <summary>
    /// 曲ファイルの読み書き（ホストに依存しない部分）。エディタのアセット操作・ゲーム内の保存・CLI から共通で使う。
    /// 書き込みは一時ファイル経由で置き換える。
    /// </summary>
    public static class SongIO
    {
        public const int DefaultSampleRate = 48000;

        /// <summary>ファイル名に使えない文字を「_」にする（空なら "Song"）。</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Song";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>書き出しの既定のファイル名（曲名、無ければアセット名）。</summary>
        public static string BaseName(Song song) => SafeName(string.IsNullOrWhiteSpace(song.title) ? song.name : song.title);

        public static void ExportWav(Song song, string path, int sampleRate = DefaultSampleRate)
        {
            var tmp = path + ".tmp";
            WavWriter.Write16(tmp, ChipEngine.RenderSongStereo(song, sampleRate), 2, sampleRate);
            AtomicFile.Commit(tmp, path);
        }

        /// <summary>チャンネルごとの WAV（ステム）を <paramref name="dir"/> に書き出す。書いたファイルのパスを返す。</summary>
        public static string[] ExportStems(Song song, string dir, string baseName, int sampleRate = DefaultSampleRate)
        {
            Directory.CreateDirectory(dir);
            var paths = new string[Song.ChannelCount];
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                paths[c] = Path.Combine(dir, $"{baseName}_{c + 1}_{Song.ChannelNames[c]}.wav");
                var tmp = paths[c] + ".tmp";
                WavWriter.Write16(tmp, OfflineRenderer.RenderChannelStereo(song, sampleRate, c), 2, sampleRate);
                AtomicFile.Commit(tmp, paths[c]);
            }
            return paths;
        }

        public static void ExportMidi(Song song, string path)
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, SongToMidi.Export(song));
            AtomicFile.Commit(tmp, path);
        }

        /// <summary>MIDI ファイルを曲にする（曲名・アセット名はファイル名）。</summary>
        public static Song ImportMidi(string path, MidiImportOptions opt, out MidiImportReport report)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var song = MidiToSong.Import(File.ReadAllBytes(path), opt, name, out report);
            song.name = name;
            return song;
        }

        /// <summary>FamiTracker の「Export text」（.txt）を曲にする。</summary>
        public static Song ImportFamiTracker(string path, out FamiTrackerReport report)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var song = FamiTrackerImport.Import(FamiTrackerImport.Decode(File.ReadAllBytes(path)), name, out report);
            song.name = name;
            return song;
        }

        /// <summary>曲の中身を丸ごと複製する（保存先は持たない）。</summary>
        public static Song Clone(Song src)
        {
            var copy = ScriptableObject.CreateInstance<Song>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(src), copy);
            copy.name = src.name;
            copy.Validate();
            return copy;
        }
    }
}
