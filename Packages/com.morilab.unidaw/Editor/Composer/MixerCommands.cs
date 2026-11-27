using System;

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
            if (!session.IsReadOnly) DawPlatform.Current.MarkDirty(Song);
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

        // ---- センドエフェクト ---------------------------------------------------------

        public float SendLevel(int ch, SendKind kind) => Mix(ch)?.Send(kind) ?? 0f;
        public float Fx(FxParam p) => Song?.effects?.Get(p) ?? SongEffects.Range(p).def;
        public bool PingPong => Song?.effects?.delayPingPong ?? true;
        public int DelayChoice => Song?.effects?.DelayChoice ?? 2;

        public void PreviewSend(int ch, SendKind kind, float v) => Live(() => Mix(ch).SetSend(kind, v));

        public void CommitSend(int ch, SendKind kind, float start, float end) =>
            Commit(kind == SendKind.Delay ? "Delay Send" : "Reverb Send", () => Mix(ch).SetSend(kind, start), s => s.Mix(ch).SetSend(kind, end));

        public void PreviewFx(FxParam p, float v) => Live(() => Song.effects.Set(p, v));

        public void CommitFx(FxParam p, float start, float end) =>
            Commit("Effect " + p, () => Song.effects.Set(p, start), s => s.effects.Set(p, end));

        public void SetDelayChoice(int i) => session.ModifySong("Delay Time", s =>
            s.effects.delayBeats = SongEffects.DelayBeatChoices[Math.Max(0, Math.Min(SongEffects.DelayBeatChoices.Length - 1, i))]);

        public void SetPingPong(bool on) => session.ModifySong("Delay Ping-Pong", s => s.effects.delayPingPong = on);

        // ---- EQ / サイドチェイン -------------------------------------------------------

        public float Param(int ch, MixParam p) => Mix(ch)?.Get(p) ?? 0f;
        public int SidechainSource => Song?.effects?.sidechainSource ?? 3;

        public void PreviewParam(int ch, MixParam p, float v) => Live(() => Mix(ch).Set(p, v));

        public void CommitParam(int ch, MixParam p, float start, float end) =>
            Commit(p == MixParam.Duck ? "Sidechain Duck" : "Channel EQ", () => Mix(ch).Set(p, start), s => s.Mix(ch).Set(p, end));

        public void SetSidechainSource(int ch) => session.ModifySong("Sidechain Source", s =>
            s.effects.sidechainSource = Math.Max(0, Math.Min(Song.ChannelCount - 1, ch)));

        /// <summary>ミキサー設定を初期値（0 dB・センター・ミュート/ソロなし・送りなし）へ。エフェクトの設定も既定に戻す。</summary>
        public void ResetMixer() => session.ModifySong("Reset Mixer", s =>
        {
            s.mixer = Song.DefaultMixer();
            s.masterDb = 0f;
            s.effects = new SongEffects();
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
