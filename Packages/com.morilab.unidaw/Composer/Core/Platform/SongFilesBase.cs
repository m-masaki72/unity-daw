using System;
using System.Collections.Generic;
using UniDAW.Midi;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// <see cref="ISongFiles"/> の共通の流れ（取り込み・書き出し・複製）。ホストごとの違いは
    /// ファイルの選び方（<see cref="PickImportFile"/> / <see cref="PickExportPath"/> / <see cref="PickExportFolder"/>）と
    /// 曲の保存先（<see cref="Store"/>）だけを実装する。中身の読み書きは <see cref="SongIO"/>。
    /// </summary>
    public abstract class SongFilesBase : ISongFiles
    {
        protected readonly ComposerSession s;

        protected SongFilesBase(ComposerSession session) => s = session;

        // ---- ホストごとに実装する

        public abstract IReadOnlyList<SongEntry> ListSongs();
        public abstract void Open(string id);
        public abstract string IdOf(Song song);
        public virtual bool CanReveal => false;
        public virtual void Reveal(Song song) { }
        public abstract void NewSong();
        public abstract void OpenSong();
        public abstract void OpenSamples();

        /// <summary>取り込むファイルを選ばせ、選ばれたら <paramref name="picked"/> を呼ぶ（その場でも後でもよい）。</summary>
        /// <param name="title">ダイアログの見出し。</param>
        /// <param name="what">ファイルの種類の説明（見つからないときの案内に使う）。</param>
        /// <param name="exts">拡張子（「.mid」のようにドット付き）。</param>
        protected abstract void PickImportFile(string title, string what, string[] exts, Action<string> picked);

        /// <summary>取り込んだ・複製した曲を保存する。保存した曲（既存の曲に上書きしたらその曲）を返す。やめたら null。</summary>
        protected abstract Song Store(Song song, string suggestedName, string title);

        /// <summary>書き出し先のファイルを決め、決まったら <paramref name="picked"/> を呼ぶ（その場でも後でもよい。やめたら呼ばない）。</summary>
        protected abstract void PickExportPath(string title, string baseName, string ext, Action<string> picked);

        /// <summary>書き出し先のフォルダを決め、決まったら <paramref name="picked"/> を呼ぶ（やめたら呼ばない）。</summary>
        protected abstract void PickExportFolder(string title, string baseName, Action<string> picked);

        /// <summary>失敗を知らせる。</summary>
        protected virtual void ReportError(string message) => s.Notify(message);

        /// <summary>時間のかかる処理を進捗表示つきで実行する。</summary>
        protected virtual void RunWithProgress(string text, Action work) => work();

        /// <summary>曲を画面に出す。</summary>
        protected virtual void Show(Song song) => s.SetSong(song);

        // ---- 共通の流れ

        public void ImportMidi(VoiceAssignMode mode) =>
            PickImportFile("Import MIDI", "MIDI ファイル（.mid）", new[] { ".mid", ".midi" }, p => ImportMidiFrom(p, mode));

        public void ImportMidiFrom(string path, VoiceAssignMode mode) =>
            Import(path, "MIDI の読み込みに失敗しましたわ… ", () =>
            {
                var song = SongIO.ImportMidi(path, new MidiImportOptions { mode = mode }, out var report);
                return (song, $"{report.patterns} patterns, dropped {report.droppedNotes}");
            });

        public void ImportFamiTracker() =>
            PickImportFile("Import FamiTracker Text", "FamiTracker の Export text（.txt）", new[] { ".txt" }, path =>
                Import(path, "FamiTracker テキストの読み込みに失敗しましたわ… ", () =>
                {
                    var song = SongIO.ImportFamiTracker(path, out var report);
                    return (song, report.ToString());
                }));

        void Import(string path, string failure, Func<(Song song, string report)> read)
        {
            try
            {
                var (song, report) = read();
                var stored = Store(song, song.name, "Save Imported Song");
                if (stored == null)
                {
                    UnityEngine.Object.DestroyImmediate(song);
                    return;
                }
                Show(stored);
                Debug.Log($"[UniDAW] import {System.IO.Path.GetFileName(path)}: {report}");
                s.Notify("インポート完了: " + report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ReportError(failure + ex.Message);
            }
        }

        public void ExportWav() => Export("Export WAV", ".wav", "WAV", (song, path) => SongIO.ExportWav(song, path));

        public void ExportMidi() => Export("Export MIDI", ".mid", "MIDI", SongIO.ExportMidi);

        void Export(string title, string ext, string what, Action<Song, string> write)
        {
            var song = s.Song;
            if (song == null) return;
            PickExportPath(title, SongIO.BaseName(song), ext, path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    RunWithProgress($"{what} を書き出しています…", () => write(song, path));
                    s.Notify($"{what} を書き出しましたわ: {path}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    ReportError($"{what} を書き出せませんでしたわ… " + ex.Message);
                }
            });
        }

        public void ExportStems()
        {
            var song = s.Song;
            if (song == null) return;
            var baseName = SongIO.BaseName(song);
            PickExportFolder("Export Stems（チャンネル別 WAV）", baseName, dir =>
            {
                if (string.IsNullOrEmpty(dir)) return;
                try
                {
                    string[] paths = null;
                    RunWithProgress("ステムを書き出しています…", () => paths = SongIO.ExportStems(song, dir, baseName));
                    s.Notify($"ステムを {paths.Length} 本書き出しましたわ: {dir}");
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    ReportError("ステムを書き出せませんでしたわ… " + ex.Message);
                }
            });
        }

        /// <summary>今の曲を別の曲として複製して開く（読み取り専用の曲を編集できるようにする）。</summary>
        public void MakeEditableCopy()
        {
            if (s.Song == null) return;
            var copy = SongIO.Clone(s.Song);
            var stored = Store(copy, SongIO.BaseName(s.Song) + " copy", "Editable Copy");
            if (stored == null) UnityEngine.Object.DestroyImmediate(copy);
            else Show(stored);
        }
    }
}
