using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniDAW.Midi;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// ゲーム内の曲ファイル操作。曲は <c>&lt;保存先&gt;/Songs/*.json</c>（Song の JSON）に置き、変更は少し待ってから自動保存する。
    /// ファイルダイアログは無いので、取り込みは <c>Import/</c> に置いたファイルをメニューから選び、書き出しは <c>Export/</c> へ出す。
    /// </summary>
    public sealed class RuntimeSongFiles : ISongFiles
    {
        const string LastSongKey = "LastSong";
        /// <summary>最後の変更からこの秒数たったら保存する。</summary>
        const double SaveDelay = 1.0;

        readonly ComposerSession s;
        readonly IDawPlatform platform;
        readonly Dictionary<Song, string> paths = new Dictionary<Song, string>();
        readonly HashSet<Song> dirty = new HashSet<Song>();
        double dirtyAt;

        public string Root { get; }
        public string SongsFolder => Path.Combine(Root, "Songs");
        public string ImportFolder => Path.Combine(Root, "Import");
        public string ExportFolder => Path.Combine(Root, "Export");

        public RuntimeSongFiles(ComposerSession session, IDawPlatform platform, string root)
        {
            s = session;
            this.platform = platform;
            Root = root;
            Directory.CreateDirectory(SongsFolder);
            Directory.CreateDirectory(ImportFolder);
            Directory.CreateDirectory(ExportFolder);
            platform.Update += AutoSave;
            platform.Unloading += SaveAll;
        }

        // ---- 保存

        /// <summary>曲が変わった印を付ける（少し待ってから保存）。</summary>
        public void MarkDirty(Song song)
        {
            if (song == null || !paths.ContainsKey(song)) return;
            dirty.Add(song);
            dirtyAt = platform.Now;
        }

        void AutoSave()
        {
            if (dirty.Count > 0 && platform.Now - dirtyAt >= SaveDelay) SaveAll();
        }

        public void SaveAll()
        {
            foreach (var song in dirty.ToArray())
                if (song != null && paths.TryGetValue(song, out var p)) Write(song, p);
            dirty.Clear();
        }

        static void Write(Song song, string path)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(song, true));
            AtomicFile.Commit(tmp, path);
        }

        /// <summary>JSON から曲を読む。</summary>
        public static Song Read(string path)
        {
            var song = ScriptableObject.CreateInstance<Song>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), song);
            song.name = Path.GetFileNameWithoutExtension(path);
            song.Validate();
            return song;
        }

        /// <summary>新しいファイルとして保存して開く。</summary>
        Song SaveNew(Song song, string baseName)
        {
            var path = UniquePath(SafeName(baseName));
            song.name = Path.GetFileNameWithoutExtension(path);
            Write(song, path);
            paths[song] = path;
            Select(song);
            return song;
        }

        void Select(Song song)
        {
            if (song != null && paths.TryGetValue(song, out var p)) platform.SetString(LastSongKey, p);
            s.SetSong(song);
        }

        string UniquePath(string name)
        {
            var path = Path.Combine(SongsFolder, name + ".json");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(SongsFolder, $"{name} {i}.json");
            return path;
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Song";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>前回開いていた曲を開く。無ければ何もしない。</summary>
        public void OpenLast()
        {
            var p = platform.GetString(LastSongKey, null);
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) Open(p);
        }

        // ---- ISongFiles

        public IReadOnlyList<SongEntry> ListSongs() =>
            Directory.GetFiles(SongsFolder, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(p => new SongEntry(p, Path.GetFileNameWithoutExtension(p), "Songs"))
                .ToList();

        public void Open(string id)
        {
            var loaded = paths.FirstOrDefault(kv => kv.Value == id).Key;
            if (loaded != null)
            {
                Select(loaded);
                return;
            }
            try
            {
                var song = Read(id);
                paths[song] = id;
                Select(song);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                s.Notify("曲を読み込めませんでしたわ… " + ex.Message);
            }
        }

        public string IdOf(Song song) => song != null && paths.TryGetValue(song, out var p) ? p : null;

        public bool CanReveal => false;
        public void Reveal(Song song) { }

        public void NewSong()
        {
            var song = Song.CreateDefault(64);
            song.title = "New Song";
            SaveNew(song, song.title);
        }

        /// <summary>保存済みの曲をメニューから選んで開く。</summary>
        public void OpenSong()
        {
            var list = ListSongs();
            if (list.Count == 0)
            {
                s.Notify("保存された曲がまだありませんわ");
                return;
            }
            var menu = new DawMenu();
            foreach (var e in list)
            {
                var id = e.id;
                menu.AddItem(e.name, s.Song != null && IdOf(s.Song) == id, () => Open(id));
            }
            menu.ShowAsContext();
        }

        /// <summary>デモ曲（既定の曲）を新しく作って開く。</summary>
        public void OpenSamples()
        {
            var song = Song.CreateDefault(64);
            song.title = "Demo";
            SaveNew(song, song.title);
        }

        /// <summary>Import フォルダのファイルをメニューで選ばせる。無ければ置き場所を知らせる。</summary>
        void PickImport(string what, Func<string, bool> match, Action<string> import)
        {
            var files = Directory.GetFiles(ImportFolder).Where(match).OrderBy(p => p).ToArray();
            if (files.Length == 0)
            {
                s.Notify($"{what} を {ImportFolder} に置いてから選んでくださいませ");
                return;
            }
            var menu = new DawMenu();
            foreach (var f in files)
            {
                var path = f;
                menu.AddItem(Path.GetFileName(path), false, () => import(path));
            }
            menu.ShowAsContext();
        }

        static bool HasExt(string path, params string[] exts) => exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        public void ImportMidi(VoiceAssignMode mode) =>
            PickImport("MIDI ファイル（.mid）", p => HasExt(p, ".mid", ".midi"), p => ImportMidiFrom(p, mode));

        public void ImportMidiFrom(string path, VoiceAssignMode mode)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var song = MidiToSong.Import(File.ReadAllBytes(path), new MidiImportOptions { mode = mode }, name, out var report);
                SaveNew(song, name);
                s.Notify($"インポート完了: {report.patterns} patterns, dropped {report.droppedNotes}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                s.Notify("MIDI の読み込みに失敗しましたわ… " + ex.Message);
            }
        }

        public void ImportFamiTracker() =>
            PickImport("FamiTracker の Export text（.txt）", p => HasExt(p, ".txt"), path =>
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    var song = FamiTrackerImport.Import(FamiTrackerImport.Decode(File.ReadAllBytes(path)), name, out var report);
                    SaveNew(song, name);
                    s.Notify("インポート完了: " + report);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    s.Notify("FamiTracker テキストの読み込みに失敗しましたわ… " + ex.Message);
                }
            });

        string ExportPath(string ext) => Path.Combine(ExportFolder, SafeName(s.Song.title ?? s.Song.name) + ext);

        public void ExportWav()
        {
            if (s.Song == null) return;
            var path = ExportPath(".wav");
            WavWriter.Write16(path, ChipEngine.RenderSongStereo(s.Song, 48000), 2, 48000);
            s.Notify("WAV を書き出しましたわ: " + path);
        }

        public void ExportStems()
        {
            if (s.Song == null) return;
            var baseName = SafeName(s.Song.title ?? s.Song.name);
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var path = Path.Combine(ExportFolder, $"{baseName}_{c + 1}_{Song.ChannelNames[c]}.wav");
                WavWriter.Write16(path, OfflineRenderer.RenderChannelStereo(s.Song, 48000, c), 2, 48000);
            }
            s.Notify($"ステムを {Song.ChannelCount} 本書き出しましたわ: {ExportFolder}");
        }

        public void ExportMidi()
        {
            if (s.Song == null) return;
            var path = ExportPath(".mid");
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, SongToMidi.Export(s.Song));
            AtomicFile.Commit(tmp, path);
            s.Notify("MIDI を書き出しましたわ: " + path);
        }

        /// <summary>今の曲を別ファイルとして複製して開く。</summary>
        public void MakeEditableCopy()
        {
            if (s.Song == null) return;
            var copy = ScriptableObject.CreateInstance<Song>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(s.Song), copy);
            copy.Validate();
            SaveNew(copy, (s.Song.title ?? s.Song.name) + " copy");
        }
    }
}
