using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UniDAW.Editor
{
    /// <summary>
    /// Claude への依頼（メロディ・コード進行）。範囲を決めて送り、返ってきた候補の 1 つ目を Undo 付きで書き込む。
    /// <see cref="Choose"/> で別の候補や元の内容に切り替えて聴き比べられる（書き込むのは曲のデータなので、トラッカーでもピアノロールでも同じに見える）。
    /// 文章化は <see cref="ClaudePrompt"/>、返答の検証は <see cref="ClaudeReply"/>、通信は差し替え可能な送信関数（既定は <see cref="ClaudeClient.Send"/>）。
    /// </summary>
    public sealed class ClaudeAssist
    {
        /// <summary>依頼文をモデルに送り、done(返答, 通信エラー) を呼ぶ。戻り値は中止。</summary>
        public delegate Action Sender(string model, string userMessage, Action<string, string> done);

        readonly ComposerSession s;
        readonly RollEditor roll;
        readonly Sender send;
        Action cancel;
        int requestId;

        public bool Busy { get; private set; }
        /// <summary>直近の状態（表示用）。</summary>
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        // ---- 直近の候補（切り替え用）
        List<ClaudeReply.Suggestion> candidates = new List<ClaudeReply.Suggestion>();
        ClaudePrompt.Request lastReq;
        Song lastSong;
        int lastPattern;
        List<RollNote> originalNotes;
        string originalChords;
        // 曲構成の依頼前の状態（パターン数・順序表）
        int originalPatternCount;
        List<int> originalOrder, originalTranspose;
        // ミックスの依頼前のミキサー
        List<ChannelMix> originalMixer;
        SongEffects originalEffects;
        // マスタリングの依頼前のマスターチェーン
        SongMaster originalMaster;
        // 音色の依頼：書き換える楽器と、依頼前の中身
        int lastInstrument;
        Instrument originalInstrument;
        // ノートの依頼に添えた文脈（固定小節の注意は送るたびに作り直す）
        string lastBaseExtra;

        // ---- 曲の下書き（曲構成 → セクションごとに各チャンネル）
        sealed class DraftStep
        {
            public int pattern, channel, order;
            public AiTarget target;
            public string section;
        }

        Song draftSong;
        string draftBrief;
        List<DraftStep> draftSteps;
        int draftNext, draftLead = -1;

        /// <summary>曲の下書きの途中か（「次へ」で進める・中止で止める）。</summary>
        public bool Drafting { get; private set; }

        /// <summary>下書きの進み具合（表示用）。</summary>
        public string DraftProgress
        {
            get
            {
                if (!Drafting) return "";
                if (draftSteps == null) return "下書き：曲構成" + (Busy ? "を考えています" : "の案を選んで「次へ」");
                if (draftNext >= draftSteps.Count) return "下書き：仕上げ";
                var st = draftSteps[draftNext];
                return $"下書き {draftNext}/{draftSteps.Count} 済み ・ 次は {st.section} の{ClaudePrompt.PartNames[st.channel]}";
            }
        }

        /// <summary>直近の講評（指摘と次の一手）。無ければ null。</summary>
        public ClaudeReply.Suggestion Critique { get; private set; }

        /// <summary>重い処理（曲の解析・ラウドネス合わせ）を走らせる。テストでは同期実行に差し替える。</summary>
        public Func<Action, Task> RunBackground = a => Task.Run(a);
        /// <summary>送る依頼の種類（既定の送信関数がシステムプロンプトとスキーマを選ぶ）。</summary>
        AiTarget sendingTarget;
        /// <summary>いま送っている依頼の種類（独自の送り手がプロンプトを選ぶため）。</summary>
        public AiTarget SendingTarget => sendingTarget;

        /// <summary>候補の数（0 なら切り替えるものがない）。</summary>
        public int CandidateCount => candidates.Count;
        /// <summary>いま書き込まれている候補（-1 は元の内容）。</summary>
        public int Chosen { get; private set; } = -1;
        /// <summary>試聴の開始行（直近の依頼範囲の頭）。</summary>
        public int AuditionRow => candidates.Count > 0 ? lastReq.from : State.cursorRow;
        public string CandidateComment(int i) => i >= 0 && i < candidates.Count ? candidates[i].comment : "";

        public ClaudeAssist(ComposerSession session, RollEditor roll, Sender sender = null)
        {
            s = session;
            this.roll = roll;
            send = sender ?? ((model, msg, done) =>
            {
                var (system, schema) = PromptFor(sendingTarget);
                return ClaudeClient.Send(model, system, schema, msg, done);
            });
        }

        /// <summary>依頼の種類ごとのシステムプロンプトと返答の形。</summary>
        public static (string system, string schema) PromptFor(AiTarget t)
        {
            switch (t)
            {
                case AiTarget.Structure: return (ClaudeStructure.SystemPrompt, ClaudeStructure.Schema);
                case AiTarget.Mix: return (ClaudeMix.SystemPrompt, ClaudeMix.Schema);
                case AiTarget.Master: return (ClaudeMaster.SystemPrompt, ClaudeMaster.Schema);
                case AiTarget.Instrument: return (ClaudeInstrument.SystemPrompt, ClaudeInstrument.Schema);
                case AiTarget.Critique: return (ClaudeCritique.SystemPrompt, ClaudeCritique.Schema);
                default: return (ClaudePrompt.SystemPrompt, ClaudePrompt.Schema);
            }
        }

        ComposerState State => s.State;

        public void SetTarget(AiTarget t) => SetOption(() => State.aiTarget = t);
        public void SetModel(int i) => SetOption(() => State.aiModel = Math.Max(0, Math.Min(ClaudePrompt.ModelIds.Length - 1, i)));
        public void SetPrompt(string text) => State.aiPrompt = text ?? "";
        public void SetLockBars(string text) => SetOption(() => State.aiLockBars = text ?? "");
        public void SetDraftAuto(bool on) => SetOption(() => State.aiDraftAuto = on);
        public bool IsChannelLocked(int ch) => (State.aiLockChannels >> ch & 1) != 0;
        public void SetChannelLock(int ch, bool on) =>
            SetOption(() => State.aiLockChannels = on ? State.aiLockChannels | 1 << ch : State.aiLockChannels & ~(1 << ch));

        void SetOption(Action set)
        {
            set();
            s.NotifyChanged();
        }

        void SetStatus(string text, bool error)
        {
            Status = text;
            StatusIsError = error;
            if (error) s.Notify(text);
            s.NotifyChanged();
        }

        /// <summary>依頼を送る。送れたら true（結果は後で書き込まれる）。</summary>
        public bool Request()
        {
            var song = s.Song;
            var p = s.CurrentPattern;
            if (song == null || p == null || Busy) return false;
            var target = State.aiTarget;
            int ch = State.cursorCh;
            if (!s.CanEdit()) return false;
            if (target == AiTarget.Draft) return StartDraft(song);
            // 下書きの途中でほかの依頼をしたら、下書きはそこで終える（ここまでの内容は残る）
            if (Drafting) EndDraft(null, false);
            switch (target)
            {
                case AiTarget.Structure: return RequestStructure(song, State.aiPrompt);
                case AiTarget.Mix: return RequestMix(song);
                case AiTarget.Master: return RequestMaster(song);
                case AiTarget.Instrument: return RequestInstrument(song);
                case AiTarget.Critique: return RequestCritique(song);
            }

            int tl = p.TrackLength(ch), from, to;
            var sel = roll.Selection;
            var picked = sel.Count > 0 ? roll.Notes().Where(n => sel.Contains(n.start)).ToList() : null;
            if (picked != null && picked.Count > 0)
            {
                from = picked.Min(n => n.start);
                to = picked.Max(n => n.End);
            }
            else
            {
                from = State.cursorRow % Math.Max(1, tl);
                to = from + State.assistRows;
            }
            return RequestNotes(song, target, ch, from, Math.Min(to, tl), State.aiPrompt, null, null);
        }

        int RowsPerBar => Math.Max(1, s.Song.rowsPerBeat) * 4;
        HashSet<int> LockedBars() => ClaudePrompt.ParseBars(State.aiLockBars);

        static string JoinExtra(string a, string b) =>
            string.IsNullOrWhiteSpace(a) ? b : string.IsNullOrWhiteSpace(b) ? a : a.TrimEnd() + "\n" + b;

        /// <summary>ノートの依頼文に添える文脈（固定した小節の注意は今の設定から作る）。</summary>
        string NoteExtra(string baseExtra, AiTarget target, int from, int to) =>
            JoinExtra(baseExtra, target == AiTarget.Chords ? null : ClaudePrompt.LockNote(LockedBars(), RowsPerBar, from, to));

        /// <summary>今のパターンの ch・行 from..to-1 にノート（とコード）を頼む。written は書き込めた後に呼ぶ。</summary>
        bool RequestNotes(Song song, AiTarget target, int ch, int from, int to, string text, string baseExtra, Action written)
        {
            if (target != AiTarget.Chords && IsChannelLocked(ch))
            {
                SetStatus($"ch{ch} {Song.ChannelNames[ch]} は固定中ですわ。固定を外してから頼んでくださいまし", true);
                return false;
            }
            if (target != AiTarget.Chords && to <= from)
            {
                SetStatus("トラックの終わりですわ。カーソルを手前に置いてくださいまし", true);
                return false;
            }

            var (min, max, _) = Song.Kinds[ch] == ChannelKind.Noise
                ? (ClaudePrompt.DrumMinKey, ClaudePrompt.DrumMaxKey, 0)
                : AssistCommands.PitchRange(ch);
            var req = new ClaudePrompt.Request
            {
                target = target, channel = ch, from = from, to = to, minPitch = min, maxPitch = max, text = text,
                extra = NoteExtra(baseExtra, target, from, to),
            };
            int pi = State.patternIndex;
            return Send(target, ClaudePrompt.BuildUserMessage(song, pi, State.orderIndex, req),
                $"{ClaudePrompt.TargetLabel(target, ch)}、行 {from}〜{to - 1}",
                NoteStale(song, pi, req),
                () =>
                {
                    lastReq = req;
                    lastSong = song;
                    lastPattern = pi;
                    lastBaseExtra = baseExtra;
                    originalNotes = target != AiTarget.Chords ? roll.Notes().Where(n => n.start >= req.from && n.start < req.to).ToList() : null;
                    originalChords = s.CurrentPattern.chords ?? "";
                },
                written: written);
        }

        Func<string> NoteStale(Song song, int pi, ClaudePrompt.Request req) =>
            () => s.Song == song && State.patternIndex == pi && (req.target == AiTarget.Chords || State.cursorCh == req.channel)
                ? null : "返答の間にパターンかチャンネルが変わったので、提案は書き込みませんでしたわ";

        bool RequestStructure(Song song, string request, Action written = null) =>
            Send(AiTarget.Structure, ClaudeStructure.BuildUserMessage(song, request), "曲構成",
                () => s.Song == song ? null : "返答の間に曲が変わったので、提案は書き込みませんでしたわ",
                () =>
                {
                    lastReq = new ClaudePrompt.Request { target = AiTarget.Structure };
                    lastSong = song;
                    lastPattern = -1;
                    originalPatternCount = song.patterns.Count;
                    originalOrder = new List<int>(song.order);
                    originalTranspose = new List<int>(song.orderTranspose);
                },
                written: written);

        bool RequestInstrument(Song song)
        {
            int ch = State.cursorCh, index = s.ChannelInstrument(ch);
            var inst = index >= 0 && index < song.instruments.Count ? song.instruments[index] : null;
            if (inst == null)
            {
                SetStatus("このチャンネルの楽器がありませんわ", true);
                return false;
            }
            string text = State.aiPrompt;
            return Send(AiTarget.Instrument, ClaudeInstrument.BuildUserMessage(song, ch, index, text), $"音色 #{index} {inst.name}",
                InstrumentStale(song, ch, index),
                () =>
                {
                    lastReq = new ClaudePrompt.Request { target = AiTarget.Instrument, channel = ch, text = text };
                    lastSong = song;
                    lastPattern = -1;
                    lastInstrument = index;
                    originalInstrument = inst.Clone();
                });
        }

        Func<string> InstrumentStale(Song song, int ch, int index) =>
            () => s.Song == song && s.ChannelInstrument(ch) == index && index < song.instruments.Count
                ? null : "返答の間に曲かチャンネルの楽器が変わったので、提案は書き込みませんでしたわ";

        bool RequestCritique(Song song) =>
            Send(AiTarget.Critique, ClaudeCritique.BuildUserMessage(song, State.aiPrompt), "講評",
                () => s.Song == song ? null : "返答の間に曲が変わったので、講評は捨てましたわ", () => { });

        // ---- 選んだ案を言葉で直す

        /// <summary>書き込んだ案を、依頼欄の言葉で直してもらえるか（ノートと音色の依頼）。</summary>
        public bool CanRefine => !Busy && Chosen >= 0 && Chosen < candidates.Count &&
                                 (ClaudePrompt.IsNoteTarget(lastReq.target) || lastReq.target == AiTarget.Instrument);

        /// <summary>
        /// いま書き込まれている案を、依頼欄の言葉（例「後半をもっと跳ねて」）で直してもらう。
        /// 新しい 3 案はこの案の変化形で、「元に戻す」は最初の依頼の前の内容に戻る。
        /// </summary>
        public bool Refine()
        {
            if (!CanRefine || !s.CanEdit()) return false;
            string feedback = State.aiPrompt;
            if (string.IsNullOrWhiteSpace(feedback))
            {
                SetStatus("直してほしいことを依頼欄に書いてくださいまし", true);
                return false;
            }
            string why = Mismatch();
            if (why != null)
            {
                SetStatus(why, true);
                return false;
            }
            var song = s.Song;
            string comment = candidates[Chosen].comment, message;
            Func<string> stale;
            if (lastReq.target == AiTarget.Instrument)
            {
                message = ClaudeInstrument.BuildUserMessage(song, lastReq.channel, lastInstrument, lastReq.text);
                stale = InstrumentStale(song, lastReq.channel, lastInstrument);
            }
            else
            {
                var req = lastReq;
                req.extra = NoteExtra(lastBaseExtra, req.target, req.from, req.to);
                message = ClaudePrompt.BuildUserMessage(song, lastPattern, State.orderIndex, req);
                stale = NoteStale(song, lastPattern, req);
            }
            // 元の内容（Choose(-1) で戻す先）は最初の依頼のまま残す
            return Send(lastReq.target, message + ClaudePrompt.RefineSection(comment, feedback), "直し", stale, () => { });
        }

        // ---- 講評の「次の一手」

        /// <summary>講評の次の一手 i をそのまま頼む（対象のパターン・チャンネルへ移ってから送る）。</summary>
        public bool RunNext(int i)
        {
            var next = Critique?.next;
            var song = s.Song;
            if (Busy || song == null || next == null || i < 0 || i >= next.Length || next[i] == null) return false;
            var n = next[i];
            var target = ClaudeCritique.ParseTarget(n.target);
            if (target == null)
            {
                SetStatus($"知らない依頼の種類ですわ（{n.target}）", true);
                return false;
            }
            if (n.pattern >= 0 && n.pattern < song.patterns.Count)
            {
                s.SelectPattern(n.pattern);
                int oi = song.order.IndexOf(n.pattern);
                if (oi >= 0) State.orderIndex = oi;
            }
            if (n.channel >= 0 && n.channel < Song.ChannelCount) State.cursorCh = n.channel;
            State.aiTarget = target.Value;
            State.aiPrompt = n.request ?? "";
            State.Clamp();
            s.NotifyChanged();
            if (!ClaudePrompt.IsNoteTarget(target.Value)) return Request();
            if (!s.CanEdit()) return false;
            if (Drafting) EndDraft(null, false);
            // パターンについての提案なので、トラック全体を頼む
            int ch = State.cursorCh;
            State.cursorRow = 0;
            roll.ClearSelection();
            return RequestNotes(song, target.Value, ch, 0, s.CurrentPattern.TrackLength(ch), State.aiPrompt, null, null);
        }

        // ---- 曲の下書き

        int draftPatternCount;
        bool draftStructured;

        /// <summary>依頼欄の概要から曲の下書きを始める：まず曲構成を頼む。</summary>
        bool StartDraft(Song song)
        {
            string brief = (State.aiPrompt ?? "").Trim();
            if (brief.Length == 0)
            {
                SetStatus("どんな曲にしたいか依頼欄に書いてくださいまし（例：明るいフィールド曲、BPM 150、イントロ・A・B・サビ）", true);
                return false;
            }
            Drafting = true;
            draftSong = song;
            draftBrief = brief;
            draftSteps = null;
            draftStructured = false;
            draftNext = 0;
            draftLead = -1;
            draftPatternCount = song.patterns.Count;
            return RequestStructure(song, brief, DraftWritten);
        }

        void EndDraft(string message, bool error)
        {
            Drafting = false;
            draftSong = null;
            draftSteps = null;
            if (message != null) SetStatus(message, error);
            else s.NotifyChanged();
        }

        /// <summary>下書きの 1 段が書き込めた：次へ進め、おまかせなら続けて頼む。</summary>
        void DraftWritten()
        {
            if (!Drafting) return;
            if (draftSteps == null) draftStructured = true;
            else
            {
                if (draftSteps[draftNext].channel == 0 && draftLead < 0) draftLead = draftSteps[draftNext].pattern;
                draftNext++;
            }
            if (State.aiDraftAuto) DraftNext();
            else s.NotifyChanged();
        }

        /// <summary>
        /// 下書きの次の段を頼む（曲構成の後は、順序表に出るパターンごとに各チャンネル）。
        /// すでに書いてあるパターンのチャンネルと、固定したチャンネルは飛ばす。
        /// </summary>
        public bool DraftNext()
        {
            if (!Drafting || Busy) return false;
            var song = s.Song;
            if (song != draftSong)
            {
                EndDraft("曲が変わったので、下書きを終えましたわ", true);
                return false;
            }
            if (!s.CanEdit()) return false;
            if (draftSteps == null)
            {
                if (!draftStructured) return RequestStructure(song, draftBrief, DraftWritten);
                draftSteps = DraftSteps(song);
            }
            while (draftNext < draftSteps.Count &&
                   (IsChannelLocked(draftSteps[draftNext].channel) || draftSteps[draftNext].pattern >= song.patterns.Count))
                draftNext++;
            if (draftNext >= draftSteps.Count)
            {
                EndDraft("下書きができましたわ。気になるところは「この案を直す」や講評で整えてくださいまし", false);
                return true;
            }
            var st = draftSteps[draftNext];
            s.SelectPattern(st.pattern);
            State.orderIndex = st.order;
            State.cursorCh = st.channel;
            State.cursorRow = 0;
            State.Clamp();
            roll.ClearSelection();
            return RequestNotes(song, st.target, st.channel, 0, song.patterns[st.pattern].TrackLength(st.channel),
                draftBrief, DraftContext(song, st), DraftWritten);
        }

        List<DraftStep> DraftSteps(Song song)
        {
            var steps = new List<DraftStep>();
            var seen = new HashSet<int>();
            for (int oi = 0; oi < song.order.Count; oi++)
            {
                int pi = song.order[oi];
                if (pi < 0 || pi >= song.patterns.Count || song.patterns[pi] == null || !seen.Add(pi)) continue;
                var p = song.patterns[pi];
                for (int ch = 0; ch < Song.ChannelCount; ch++)
                {
                    if (IsChannelLocked(ch)) continue;
                    if (pi < draftPatternCount && RollNotes.Extract(p, ch).Count > 0) continue;
                    steps.Add(new DraftStep
                    {
                        pattern = pi, channel = ch, order = oi, section = ClaudeStructure.PatternName(song, pi),
                        target = ch == 0 && string.IsNullOrWhiteSpace(p.chords) ? AiTarget.MelodyAndChords : AiTarget.Melody,
                    });
                }
            }
            return steps;
        }

        /// <summary>下書きの各段に添える文脈：曲の概要・構成・この段の役目・最初に書いたメロディ（モチーフをそろえる）。</summary>
        string DraftContext(Song song, DraftStep st)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Song draft");
            sb.AppendLine("This is one step of drafting a whole new song section by section. Brief from the composer: " + draftBrief);
            sb.AppendLine("Structure: " + string.Join(" -> ", song.order.Where(i => i >= 0 && i < song.patterns.Count)
                .Select(i => $"{ClaudeStructure.PatternName(song, i)}[{i}]")));
            sb.AppendLine($"Now writing section \"{st.section}\" (pattern {st.pattern}), the {ClaudePrompt.PartNames[st.channel]} part, " +
                          "for the whole section. Give the section its own role in the song (intro, verse, chorus...) while staying in one style.");
            var lead = draftLead >= 0 && draftLead < song.patterns.Count ? song.patterns[draftLead] : null;
            if (st.channel == 0 && lead != null && draftLead != st.pattern)
                sb.AppendLine($"Keep the song's motif: develop the earlier lead melody below (rhythm and contour) instead of inventing an unrelated one. " +
                              $"Earlier lead melody (section \"{ClaudeStructure.PatternName(song, draftLead)}\"): {ClaudePrompt.Part(song, lead, 0)}");
            else if (st.channel != 0)
                sb.AppendLine("Support the lead melody (ch0) and the chords of this section.");
            return sb.ToString();
        }

        bool RequestMix(Song song) =>
            Send(AiTarget.Mix, ClaudeMix.BuildUserMessage(song, State.aiPrompt), "ミックス",
                () => s.Song == song ? null : "返答の間に曲が変わったので、提案は書き込みませんでしたわ",
                () =>
                {
                    lastReq = new ClaudePrompt.Request { target = AiTarget.Mix };
                    lastSong = song;
                    lastPattern = -1;
                    song.Validate();
                    originalMixer = song.mixer.Select(m => m.Clone()).ToList();
                    originalEffects = song.effects.Clone();
                });

        /// <summary>
        /// マスタリング：まず曲をチェーンなしで解析し（バックグラウンド）、結果を添えて送る。
        /// 返ってきた候補はそれぞれ目標ラウドネスにゲインを合わせてから書き込む（音量差でなく音色で聴き比べられるように）。
        /// </summary>
        bool RequestMaster(Song song)
        {
            song.Validate();
            var original = song.master.Clone();
            var dry = original.Clone();
            dry.enabled = false;
            string request = State.aiPrompt;
            int id = ++requestId;
            Busy = true;
            SetStatus("曲を解析しています…（マスタリング）", false);
            Run();
            return true;

            async void Run()
            {
                MasterReport report = null;
                string error = null;
                try { await RunBackground(() => report = MasterAnalysis.Analyze(song, dry)); }
                catch (Exception ex) { error = ex.GetBaseException().Message; }
                if (id != requestId) return;
                Busy = false;
                if (error != null)
                {
                    SetStatus("解析に失敗しましたわ：" + error, true);
                    return;
                }
                if (report.mix.Silent)
                {
                    SetStatus("音が出ていないので、マスタリングできませんわ", true);
                    return;
                }
                if (s.Song != song)
                {
                    SetStatus("解析の間に曲が変わったので、送りませんでしたわ", true);
                    return;
                }
                Send(AiTarget.Master, ClaudeMaster.BuildUserMessage(song, report, request), "マスタリング",
                    () => s.Song == song ? null : "返答の間に曲が変わったので、提案は書き込みませんでしたわ",
                    () =>
                    {
                        lastReq = new ClaudePrompt.Request { target = AiTarget.Master };
                        lastSong = song;
                        lastPattern = -1;
                        originalMaster = original;
                    },
                    list => RunBackground(() => ClaudeMaster.MatchGains(song, original, list)),
                    "3 案のラウドネスをそろえています…");
            }
        }

        /// <summary>
        /// 送って、返答を読み、まだ書き込める状態なら（stale が null を返せば）記録して候補 1 を書き込む。
        /// prepare があれば、書き込む前に候補へ重い下ごしらえをする（その間も Busy）。
        /// </summary>
        bool Send(AiTarget target, string message, string what, Func<string> stale, Action remember,
            Func<List<ClaudeReply.Suggestion>, Task> prepare = null, string preparing = null, Action written = null)
        {
            string model = ClaudePrompt.ModelIds[Math.Max(0, Math.Min(ClaudePrompt.ModelIds.Length - 1, State.aiModel))];
            int id = ++requestId;
            Busy = true;
            SetStatus($"Claude が考えています…（{what}）", false);
            sendingTarget = target;
            cancel = send(model, message, async (json, netError) =>
            {
                if (id != requestId) return;
                Busy = false;
                cancel = null;
                if (netError != null)
                {
                    SetStatus(netError, true);
                    return;
                }
                var list = ClaudeReply.Parse(json, out string error);
                if (list == null)
                {
                    SetStatus(error, true);
                    return;
                }
                string why = stale();
                if (why == null && prepare != null)
                {
                    Busy = true;
                    SetStatus(preparing ?? "仕上げています…", false);
                    string error2 = null;
                    try { await prepare(list); }
                    catch (Exception ex) { error2 = ex.GetBaseException().Message; }
                    if (id != requestId) return;
                    Busy = false;
                    if (error2 != null)
                    {
                        SetStatus(error2, true);
                        return;
                    }
                    why = stale();
                }
                if (why != null)
                {
                    SetStatus(why, true);
                    return;
                }
                if (target == AiTarget.Critique)
                {
                    // 講評は曲を書き換えない（候補にも入れない）
                    var c = list[0];
                    c.points = (c.points ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)).Take(ClaudeCritique.MaxPoints).ToArray();
                    c.next = (c.next ?? new ClaudeReply.NextAction[0]).Where(x => x != null && ClaudeCritique.ParseTarget(x.target) != null)
                        .Take(ClaudeCritique.MaxNext).ToArray();
                    Critique = c;
                    SetStatus($"講評が届きましたわ（指摘 {c.points.Length} 件・次の一手 {c.next.Length} 件）", false);
                    return;
                }
                candidates = list;
                remember();
                Chosen = -1;
                if (Choose(0)) written?.Invoke();
            });
            return true;
        }

        /// <summary>依頼を中止する。下書きの途中なら下書きも止める（ここまで書いた内容は残る）。</summary>
        public void Cancel()
        {
            if (!Busy && !Drafting) return;
            bool wasDrafting = Drafting;
            if (Busy)
            {
                requestId++;
                Busy = false;
                var c = cancel;
                cancel = null;
                c?.Invoke();
            }
            if (wasDrafting) EndDraft("下書きを止めましたわ。ここまでの内容は残っています", false);
            else SetStatus("中止しましたわ", false);
        }

        /// <summary>候補を書き込める状態か（だめなら理由）。</summary>
        string Mismatch()
        {
            if (s.Song != lastSong) return "曲が変わったので、候補は切り替えられませんわ";
            switch (lastReq.target)
            {
                case AiTarget.Structure:
                    return s.Song.patterns.Count < originalPatternCount ? "パターンが減ったので、候補は切り替えられませんわ" : null;
                case AiTarget.Mix:
                case AiTarget.Master:
                    return null;
                case AiTarget.Instrument:
                    return lastInstrument < s.Song.instruments.Count ? null : "楽器が減ったので、候補は切り替えられませんわ";
                default:
                    return State.patternIndex != lastPattern || lastReq.target != AiTarget.Chords && State.cursorCh != lastReq.channel
                        ? "パターンかチャンネルが変わったので、候補は切り替えられませんわ" : null;
            }
        }

        /// <summary>候補 i を書き込む（-1 で依頼前の内容に戻す）。それぞれ 1 つの Undo になる。</summary>
        public bool Choose(int i)
        {
            if (Busy || candidates.Count == 0 || i < -1 || i >= candidates.Count || i == Chosen) return false;
            var req = lastReq;
            string why = Mismatch();
            if (why != null)
            {
                SetStatus(why, true);
                return false;
            }
            if (!s.CanEdit()) return false;
            Chosen = i;
            switch (req.target)
            {
                case AiTarget.Master: ChooseMaster(i); return true;
                case AiTarget.Mix: ChooseMix(i); return true;
                case AiTarget.Structure: ChooseStructure(i); return true;
                case AiTarget.Instrument: ChooseInstrument(i); return true;
            }
            if (i < 0)
            {
                if (req.target != AiTarget.Melody && s.CurrentPattern.chords != originalChords)
                    s.ModifyPattern("AI Original", p => p.chords = originalChords);
                if (originalNotes != null) roll.ReplaceRange("AI Original", req.from, req.to, originalNotes);
                SetStatus("依頼前の内容に戻しましたわ", false);
                return true;
            }
            Apply(candidates[i], req, $"案{i + 1}/{candidates.Count}: ");
            return true;
        }

        /// <summary>曲構成の候補 i（-1 は依頼前）にする：足したパターンを外し順序表を戻してから組み立てる（1 回の Undo）。</summary>
        void ChooseStructure(int i)
        {
            string order = null;
            s.ModifySong(i < 0 ? "AI Original" : "AI Structure", song =>
            {
                song.patterns.RemoveRange(originalPatternCount, song.patterns.Count - originalPatternCount);
                song.order = new List<int>(originalOrder);
                song.orderTranspose = new List<int>(originalTranspose);
                if (i >= 0) order = ClaudeStructure.Build(song, candidates[i].sections, originalPatternCount);
            });
            if (i < 0) SetStatus("依頼前の構成に戻しましたわ", false);
            else if (order == null) SetStatus($"案{i + 1}/{candidates.Count}: 構成が空でしたわ", true);
            else
            {
                var c = candidates[i].comment;
                SetStatus($"案{i + 1}/{candidates.Count}: " + (c.Length > 0 ? c + "  " : "") + $"[{order}]", false);
            }
        }

        /// <summary>音色の候補 i（-1 は依頼前）を楽器に書き込む（1 回の Undo）。</summary>
        void ChooseInstrument(int i)
        {
            var inst = i < 0
                ? originalInstrument.Clone()
                : ClaudeInstrument.Build(originalInstrument, candidates[i].instrument, Song.Kinds[lastReq.channel]);
            s.ModifySong(i < 0 ? "AI Original" : "AI Instrument", song => song.instruments[lastInstrument] = inst);
            if (i < 0) SetStatus("依頼前の音色に戻しましたわ", false);
            else
            {
                var c = candidates[i].comment;
                SetStatus($"案{i + 1}/{candidates.Count}: " + (c.Length > 0 ? c + "  " : "") + $"[#{lastInstrument} {inst.name}]", false);
            }
        }

        /// <summary>マスタリングの候補 i（-1 は依頼前）にする（1 回の Undo）。</summary>
        void ChooseMaster(int i)
        {
            var m = i < 0 ? originalMaster.Clone() : ClaudeMaster.Build(originalMaster, candidates[i].master);
            s.ModifySong(i < 0 ? "AI Original" : "AI Master", song => song.master = m.Clone());
            if (i < 0) SetStatus("依頼前のマスターに戻しましたわ", false);
            else
            {
                var c = candidates[i].comment;
                SetStatus($"案{i + 1}/{candidates.Count}: " + (c.Length > 0 ? c + "  " : "") + $"[{ClaudeMaster.Label(m)}]", false);
            }
        }

        /// <summary>ミックスの候補 i（-1 は依頼前）にする：ミキサーを依頼前に戻してから書く（1 回の Undo）。ミュート / ソロは今のまま。</summary>
        void ChooseMix(int i)
        {
            string label = null;
            s.ModifySong(i < 0 ? "AI Original" : "AI Mix", song =>
            {
                song.Validate();
                for (int c = 0; c < originalMixer.Count && c < song.mixer.Count; c++)
                {
                    var m = originalMixer[c].Clone();
                    m.mute = song.mixer[c].mute;
                    m.solo = song.mixer[c].solo;
                    song.mixer[c] = m;
                }
                song.effects = originalEffects.Clone();
                if (i >= 0) label = ClaudeMix.Apply(song, candidates[i]);
            });
            if (i < 0) SetStatus("依頼前のミックスに戻しましたわ", false);
            else if (label == null) SetStatus($"案{i + 1}/{candidates.Count}: ミックスが空でしたわ", true);
            else
            {
                var c = candidates[i].comment;
                SetStatus($"案{i + 1}/{candidates.Count}: " + (c.Length > 0 ? c + "  " : "") + $"[{label}]", false);
            }
        }

        /// <summary>GM ドラム番号 → ノイズのピッチと楽器。音は次の打点まで伸ばす（減衰を切らない）。</summary>
        IEnumerable<RollNote> DrumNotes(List<MelodyModel.Note> notes, int to)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                var hit = UniDAW.Midi.DrumMap.Map(notes[i].pitch);
                int end = i + 1 < notes.Count ? notes[i + 1].start : to;
                yield return new RollNote(notes[i].start, Math.Max(1, end - notes[i].start), hit.pitch, Cell.NoteCell(hit.pitch, DrumInstrument(hit.instrument)));
            }
        }

        /// <summary>既定の楽器番号を、この曲で同じ名前の楽器に読み替える（無ければ番号のまま、範囲外ならチャンネルの楽器）。</summary>
        int DrumInstrument(int defaultIndex)
        {
            var defaults = Song.DefaultInstruments();
            var list = s.Song.instruments;
            if (defaultIndex >= 0 && defaultIndex < defaults.Count)
            {
                string name = defaults[defaultIndex].name;
                int found = list.FindIndex(i => i != null && string.Equals(i.name, name, StringComparison.OrdinalIgnoreCase));
                if (found >= 0) return found;
            }
            return defaultIndex >= 0 && defaultIndex < list.Count ? defaultIndex : s.ChannelInstrument(Song.ChannelCount - 1);
        }

        void Apply(ClaudeReply.Suggestion sg, ClaudePrompt.Request req, string label)
        {
            string done = "";
            if (req.target != AiTarget.Melody && sg.chords.Trim().Length > 0)
            {
                ChordProgression.Parse(sg.chords, s.Song.Key, out string chordError);
                s.ModifyPattern("AI Chords", p => p.chords = sg.chords.Trim());
                done += $"コード {sg.chords.Trim()}" + (chordError != null ? $"（{chordError}）" : "");
            }
            if (req.target != AiTarget.Chords)
            {
                var notes = ClaudeReply.FitNotes(sg.notes, req.from, req.to, req.minPitch, req.maxPitch);
                int inst = s.ChannelInstrument(req.channel);
                var written = KeepLockedBars((Song.Kinds[req.channel] == ChannelKind.Noise
                    ? DrumNotes(notes, req.to)
                    : notes.Select(n => new RollNote(n.start, n.length, n.pitch, Cell.NoteCell(n.pitch, inst)))).ToList(), req.from, req.to, out int kept);
                roll.ReplaceRange("AI Suggest", req.from, req.to, written);
                done += (done.Length > 0 ? " / " : "") + $"{written.Count - kept} 音（行 {req.from}〜{req.to - 1}）" + (kept > 0 ? $"・固定 {kept} 音" : "");
            }
            SetStatus(label + (sg.comment.Length > 0 ? sg.comment + "  " : "") + $"[{done}]", false);
        }

        /// <summary>
        /// 固定した小節を守る：そこに始まる新しいノートは捨て、固定小節にはみ出すノートはその手前で切り、
        /// 固定小節に始まる今のノートはそのまま残す。kept は残した今のノートの数。
        /// </summary>
        List<RollNote> KeepLockedBars(List<RollNote> added, int from, int to, out int kept)
        {
            kept = 0;
            var bars = LockedBars();
            if (bars.Count == 0) return added;
            int rowsPerBar = RowsPerBar;
            bool Locked(int row) => bars.Contains(row / rowsPerBar);
            var result = new List<RollNote>();
            foreach (var n in added)
            {
                if (Locked(n.start)) continue;
                int len = n.length;
                for (int r = n.start + 1; r < n.start + n.length; r++)
                    if (Locked(r))
                    {
                        len = r - n.start;
                        break;
                    }
                result.Add(new RollNote(n.start, len, n.pitch, n.cell));
            }
            var current = roll.Notes().Where(n => n.start >= from && n.start < to && Locked(n.start)).ToList();
            kept = current.Count;
            result.AddRange(current);
            return result.OrderBy(n => n.start).ToList();
        }
    }
}
