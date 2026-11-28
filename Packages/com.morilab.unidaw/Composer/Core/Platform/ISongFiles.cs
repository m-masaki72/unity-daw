using System.Collections.Generic;
using UniDAW.Midi;

namespace UniDAW.Composer
{
    /// <summary>曲の一覧に出す 1 曲（id は保存先ごとの識別子。エディタではアセットのパス）。</summary>
    public readonly struct SongEntry
    {
        public readonly string id, name, folder;

        public SongEntry(string id, string name, string folder)
        {
            this.id = id;
            this.name = name;
            this.folder = folder;
        }
    }

    /// <summary>
    /// 曲ファイルの操作（作る・開く・読み込む・書き出す）。UI はここだけを呼ぶ。
    /// エディタではアセットとファイルダイアログ（<c>SongFileCommands</c>）、ゲーム内では別の保存先に差し替える。
    /// </summary>
    public interface ISongFiles
    {
        /// <summary>開ける曲の一覧。</summary>
        IReadOnlyList<SongEntry> ListSongs();
        /// <summary>一覧の曲を開く。</summary>
        void Open(string id);
        /// <summary>曲の id（保存されていなければ null）。</summary>
        string IdOf(Song song);
        /// <summary>曲の保存先を見せる（エディタではプロジェクトビューで示す）。できなければ false。</summary>
        bool CanReveal { get; }
        void Reveal(Song song);

        void NewSong();
        void OpenSong();
        void OpenSamples();
        void ImportMidi(VoiceAssignMode mode);
        /// <summary>指定した MIDI ファイルを取り込む（ドラッグ＆ドロップ等）。</summary>
        void ImportMidiFrom(string path, VoiceAssignMode mode);
        void ImportFamiTracker();
        void ExportWav();
        void ExportStems();
        void ExportMidi();
        void MakeEditableCopy();
    }
}
