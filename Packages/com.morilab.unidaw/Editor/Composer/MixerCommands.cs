using System;
using UnityEditor;

namespace UniDAW.Editor
{
    /// <summary>
    /// ミキサー操作（音量・パン・ミュート・ソロ・マスター）。設定は曲（<see cref="Song.mixer"/>）に保存され、
    /// 試聴・ゲーム内再生・WAV 書き出しのすべてに効く。
    /// フェーダーはドラッグ中に Preview* で直接鳴らし、離したときの Commit* で Undo 1 回ぶんにまとめる。
    /// ミュート/ソロは DAW と同じく Undo に積まない（読み取り専用の曲でも試聴のため切り替えられる）。
    /// </summary>
    public sealed class MixerCommands
    {
        readonly ComposerSession session;

        public MixerCommands(ComposerSession session) => this.session = session;

        Song Song => session.Song;
        ChannelMix Mix(int ch) => Song?.Mix(ch);

        public float VolumeDb(int ch) => Mix(ch)?.volumeDb ?? 0f;
        public float Pan(int ch) => Mix(ch)?.pan ?? 0f;
        public float MasterDb => Song?.masterDb ?? 0f;
        public bool IsMuted(int ch) => Mix(ch)?.mute ?? false;
        public bool IsSoloed(int ch) => Mix(ch)?.solo ?? false;
        public bool AnySolo
        {
            get
            {
                for (int c = 0; c < Song.ChannelCount; c++) if (IsSoloed(c)) return true;
                return false;
            }
        }

        /// <summary>ソロ等の結果、実際に鳴らないチャンネルか（表示用）。</summary>
        public bool IsSilenced(int ch) => IsMuted(ch) || (AnySolo && !IsSoloed(ch));

        // ---- ミュート / ソロ ---------------------------------------------------------

        public void ToggleMute(int ch) => SetFlag(ch, m => m.mute = !m.mute);
        public void ToggleSolo(int ch) => SetFlag(ch, m => m.solo = !m.solo);

        /// <summary>Ctrl+クリック：このチャンネルだけソロ（すでに単独ソロなら解除）。</summary>
        public void ExclusiveSolo(int ch)
        {
            bool only = IsSoloed(ch);
            for (int c = 0; c < Song.ChannelCount; c++) if (c != ch && IsSoloed(c)) only = false;
            SetAll(c => Mix(c).solo = !only && c == ch);
        }

        /// <summary>全チャンネルのミュートとソロを解除（Cubase の「すべてのミュート/ソロを解除」）。</summary>
        public void ClearMuteSolo() => SetAll(c =>
        {
            Mix(c).mute = false;
            Mix(c).solo = false;
        });

        void SetFlag(int ch, Action<ChannelMix> change)
        {
            var m = Mix(ch);
            if (m == null) return;
            change(m);
            Touched();
        }

        void SetAll(Action<int> change)
        {
            if (Song == null) return;
            for (int c = 0; c < Song.ChannelCount; c++) change(c);
            Touched();
        }

        void Touched()
        {
            if (!session.IsReadOnly) EditorUtility.SetDirty(Song);
            session.NotifyChanged();
        }

        // ---- フェーダー（ドラッグ中はライブ、離したら Undo 付きで確定） --------------------

        public void PreviewVolume(int ch, float db) => Live(() => Mix(ch).volumeDb = ChannelMix.ClampDb(db));
        public void PreviewPan(int ch, float pan) => Live(() => Mix(ch).pan = ChannelMix.ClampPan(pan));
        public void PreviewMaster(float db) => Live(() => Song.masterDb = ChannelMix.ClampDb(db));

        public void CommitVolume(int ch, float start, float end) =>
            Commit("Channel Volume", () => Mix(ch).volumeDb = start, s => s.Mix(ch).volumeDb = ChannelMix.ClampDb(end));

        public void CommitPan(int ch, float start, float end) =>
            Commit("Channel Pan", () => Mix(ch).pan = start, s => s.Mix(ch).pan = ChannelMix.ClampPan(end));

        public void CommitMaster(float start, float end) =>
            Commit("Master Volume", () => Song.masterDb = start, s => s.masterDb = ChannelMix.ClampDb(end));

        /// <summary>ミキサー設定を初期値（0 dB・センター・ミュート/ソロなし）へ。</summary>
        public void ResetMixer() => session.ModifySong("Reset Mixer", s =>
        {
            s.mixer = Song.DefaultMixer();
            s.masterDb = 0f;
        });

        void Live(Action change)
        {
            // 読み取り専用の曲は確定できないので、ドラッグ中の音も変えない
            if (Song == null || session.IsReadOnly) return;
            change();
        }

        void Commit(string undoName, Action revert, Action<Song> apply)
        {
            if (Song == null) return;
            // Undo には「ドラッグ前 → 確定値」の 1 段で記録させる
            if (!session.IsReadOnly) revert();
            session.ModifySong(undoName, apply);
        }
    }
}
