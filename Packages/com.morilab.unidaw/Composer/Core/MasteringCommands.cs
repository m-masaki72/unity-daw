using System;
using System.Threading.Tasks;

namespace UniDAW.Composer
{
    /// <summary>
    /// マスターチェーンの編集と、曲の解析・ラウドネス合わせ（バックグラウンドでオフラインレンダリング）。
    /// 解析中も曲は編集でき、終わったら <see cref="Tick"/> が結果を取り込む。
    /// </summary>
    public sealed class MasteringCommands
    {
        readonly ComposerSession session;
        Task<MasterReport> analyzeTask;
        Task<SongMaster> matchTask;
        Song taskSong, reportSong;
        /// <summary>解析スレッドに渡す曲の複製（編集中の曲をバックグラウンドで読まないため）。</summary>
        Song snapshot;

        public MasteringCommands(ComposerSession session) => this.session = session;

        Song Song => session.State.song;
        public SongMaster Master => Song?.master;

        /// <summary>最後の解析結果（曲を切り替えると消える）。</summary>
        public MasterReport Report { get; private set; }
        public string Status { get; private set; } = "";
        public bool Busy => analyzeTask != null || matchTask != null;

        public bool Enabled => Master?.enabled ?? false;
        public bool Limiter => Master?.limiter ?? true;
        public float TargetLufs => Master?.targetLufs ?? -14f;
        public float Get(MasterParam p) => Master?.Get(p) ?? SongMaster.Range(p).def;

        // ---- 編集（フェーダーはドラッグ中ライブ、離したら Undo 付きで確定） ----------------

        public void Preview(MasterParam p, float v)
        {
            if (Master == null || session.IsReadOnly) return;
            Master.Set(p, v);
        }

        public void Commit(MasterParam p, float start, float end)
        {
            if (Master == null) return;
            if (!session.IsReadOnly) Master.Set(p, start);
            session.ModifySong("Master " + p, s => s.master.Set(p, end));
        }

        public void SetEnabled(bool on) => session.ModifySong(on ? "Master On" : "Master Off", s => s.master.enabled = on);
        public void SetLimiter(bool on) => session.ModifySong("Master Limiter", s => s.master.limiter = on);
        public void SetTarget(float lufs) => session.ModifySong("Loudness Target", s => s.master.targetLufs = SongMaster.ClampTarget(lufs));

        /// <summary>チェーンを初期値に戻す（ON/OFF と目標ラウドネスは保つ）。</summary>
        public void ResetChain() => session.ModifySong("Reset Master", s =>
        {
            var m = new SongMaster { enabled = s.master.enabled, targetLufs = s.master.targetLufs };
            s.master = m;
        });

        // ---- 再生中のメーターと A/B ---------------------------------------------------

        /// <summary>再生中のラウドネス（マスターチェーンの前と後）。エンジンが無ければ null。</summary>
        public LiveLoudness Live => session.Transport?.Engine?.Loudness;
        /// <summary>A/B：マスターチェーンを外して聴く（チェーンで変わった音量は補正する）。曲には保存しない。</summary>
        public bool Audition { get; private set; }
        /// <summary>A/B で外した音に掛けている補正（dB）。</summary>
        public float AuditionGainDb { get; private set; }
        /// <summary>補正量をいまの再生で測れているか（false の間は直前の値）。</summary>
        public bool AuditionMeasured { get; private set; }

        public void SetAudition(bool on)
        {
            Audition = on;
            TickLive();
            session.NotifyChanged();
        }

        /// <summary>毎フレーム：メーターを集計し、A/B の補正をエンジンに渡す。</summary>
        void TickLive()
        {
            var e = session.Transport?.Engine;
            if (e == null) return;
            var live = e.Loudness;
            if (live == null) e.Loudness = live = new LiveLoudness(e.SampleRate);
            live.Poll();
            var d = live.ChainGainDb;
            AuditionMeasured = d.HasValue;
            if (d.HasValue) AuditionGainDb = Math.Max(-24f, Math.Min(24f, d.Value));
            e.AuditionBypass = Audition && Enabled;
            e.AuditionGainDb = AuditionGainDb;
        }

        // ---- 解析 ---------------------------------------------------------------------

        public void Analyze()
        {
            var song = Song;
            if (song == null || Busy) return;
            taskSong = song;
            song = snapshot = Snapshot(song);
            var master = song.master.Clone();
            Status = "解析中…（曲をレンダリングしています）";
            analyzeTask = Task.Run(() => MasterAnalysis.Analyze(song, master));
            session.NotifyChanged();
        }

        /// <summary>目標ラウドネスに合うようにゲインを決める（マスターとリミッターも ON にする）。</summary>
        public void MatchLoudness()
        {
            var song = Song;
            if (song == null || Busy || session.IsReadOnly) return;
            taskSong = song;
            song = snapshot = Snapshot(song);
            var master = song.master.Clone();
            Status = $"ラウドネスを {master.targetLufs:0.#} LUFS に合わせています…";
            matchTask = Task.Run(() => MasterAnalysis.MatchLoudness(song, master, out _));
            session.NotifyChanged();
        }

        static Song Snapshot(Song song)
        {
            var copy = UnityEngine.Object.Instantiate(song);
            copy.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            copy.Validate();
            return copy;
        }

        /// <summary>複製を捨てる。中身は C# のフィールドだけなので、走り終わっていないスレッドが読んでいても壊れない。</summary>
        void DropSnapshot()
        {
            if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
            snapshot = null;
        }

        /// <summary>毎フレーム：終わった解析を取り込む。</summary>
        public void Tick()
        {
            TickLive();
            if (Report != null && reportSong != Song)
            {
                Report = null;
                session.NotifyChanged();
            }
            if (taskSong != null && taskSong != Song)
            {
                // 曲が切り替わった：結果は捨てる
                analyzeTask = null;
                matchTask = null;
                taskSong = null;
                DropSnapshot();
                Report = null;
                Status = "";
                session.NotifyChanged();
                return;
            }
            if (analyzeTask != null && analyzeTask.IsCompleted)
            {
                var t = analyzeTask;
                analyzeTask = null;
                taskSong = null;
                DropSnapshot();
                if (t.IsFaulted)
                {
                    Status = "解析に失敗しました：" + t.Exception?.GetBaseException().Message;
                    Report = null;
                }
                else
                {
                    Report = t.Result;
                    reportSong = Song;
                    Status = "";
                }
                session.NotifyChanged();
            }
            if (matchTask != null && matchTask.IsCompleted)
            {
                var t = matchTask;
                matchTask = null;
                taskSong = null;
                DropSnapshot();
                if (t.IsFaulted) Status = "ラウドネス合わせに失敗しました：" + t.Exception?.GetBaseException().Message;
                else if (t.Result == null) Status = "音が出ていないので合わせられません";
                else
                {
                    float gain = t.Result.gainDb;
                    session.ModifySong("Match Loudness", s =>
                    {
                        s.master.enabled = true;
                        s.master.limiter = true;
                        s.master.gainDb = gain;
                    });
                    Status = "";
                    session.Notify($"マスターのゲインを {gain:+0.0;-0.0;0} dB にしました");
                    Analyze();
                    return;
                }
                session.NotifyChanged();
            }
        }
    }
}
