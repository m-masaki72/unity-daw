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
    public sealed class RuntimeSongFiles : SongFilesBase, IDisposable
    {
        const string LastSongKey = "LastSong";
        /// <summary>最後の変更からこの秒数たったら保存する。</summary>
        const double SaveDelay = 1.0;

        readonly IDawPlatform platform;
        readonly Dictionary<Song, string> paths = new Dictionary<Song, string>();
        readonly HashSet<Song> dirty = new HashSet<Song>();
        double dirtyAt;

        public string Root { get; }
        public string SongsFolder => Path.Combine(Root, "Songs");
        public string ImportFolder => Path.Combine(Root, "Import");
        public string ExportFolder => Path.Combine(Root, "Export");

        public RuntimeSongFiles(ComposerSession session, IDawPlatform platform, string root) : base(session)
        {
            this.platform = platform;
            Root = root;
            Directory.CreateDirectory(SongsFolder);
            Directory.CreateDirectory(ImportFolder);
            Directory.CreateDirectory(ExportFolder);
            platform.Update += AutoSave;
            platform.Unloading += SaveAll;
            session.SongModified += MarkDirty;
        }

        /// <summary>保存し残しを書き、購読を外す。</summary>
        public void Dispose()
        {
            SaveAll();
            platform.Update -= AutoSave;
            platform.Unloading -= SaveAll;
            s.SongModified -= MarkDirty;
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

        /// <summary>新しいファイルとして保存する。</summary>
        protected override Song Store(Song song, string suggestedName, string title)
        {
            var path = UniquePath(SongIO.SafeName(suggestedName));
            song.name = Path.GetFileNameWithoutExtension(path);
            Write(song, path);
            paths[song] = path;
            return song;
        }

        void SaveNew(Song song, string baseName) => Show(Store(song, baseName, null));

        protected override void Show(Song song)
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

        /// <summary>前回開いていた曲を開く。無ければ何もしない。</summary>
        public void OpenLast()
        {
            var p = platform.GetString(LastSongKey, null);
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) Open(p);
        }

        // ---- ISongFiles

        public override IReadOnlyList<SongEntry> ListSongs() =>
            Directory.GetFiles(SongsFolder, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(p => new SongEntry(p, Path.GetFileNameWithoutExtension(p), "Songs"))
                .ToList();

        public override void Open(string id)
        {
            var loaded = paths.FirstOrDefault(kv => kv.Value == id).Key;
            if (loaded != null)
            {
                Show(loaded);
                return;
            }
            try
            {
                var song = Read(id);
                paths[song] = id;
                Show(song);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                s.Notify("曲を読み込めませんでしたわ… " + ex.Message);
            }
        }

        public override string IdOf(Song song) => song != null && paths.TryGetValue(song, out var p) ? p : null;

        public override void NewSong()
        {
            var song = Song.CreateDefault(64);
            song.title = "New Song";
            SaveNew(song, song.title);
        }

        /// <summary>保存済みの曲をメニューから選んで開く。</summary>
        public override void OpenSong()
        {
            var list = ListSongs();
            if (list.Count == 0)
            {
                s.Notify("保存された曲がまだありませんわ");
                return;
            }
            var menu = new DawMenu(platform);
            foreach (var e in list)
            {
                var id = e.id;
                menu.AddItem(e.name, s.Song != null && IdOf(s.Song) == id, () => Open(id));
            }
            menu.ShowAsContext();
        }

        /// <summary>デモ曲（既定の曲）を新しく作って開く。</summary>
        public override void OpenSamples()
        {
            var song = Song.CreateDefault(64);
            song.title = "Demo";
            SaveNew(song, song.title);
        }

        /// <summary>Import フォルダのファイルをメニューで選ばせる。無ければ置き場所を知らせる。</summary>
        protected override void PickImportFile(string title, string what, string[] exts, Action<string> picked)
        {
            var files = Directory.GetFiles(ImportFolder)
                .Where(p => exts.Any(e => p.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p).ToArray();
            if (files.Length == 0)
            {
                s.Notify($"{what} を {ImportFolder} に置いてから選んでくださいませ");
                return;
            }
            var menu = new DawMenu(platform);
            foreach (var f in files)
            {
                var path = f;
                menu.AddItem(Path.GetFileName(path), false, () => picked(path));
            }
            menu.ShowAsContext();
        }

        /// <summary>Export フォルダに曲名で書き出す（ダイアログは無い）。</summary>
        protected override string PickExportPath(string title, string baseName, string ext)
        {
            Directory.CreateDirectory(ExportFolder);
            return Path.Combine(ExportFolder, baseName + ext);
        }

        protected override string PickExportFolder(string title) => ExportFolder;
    }
}
