using System;
using System.Linq;

namespace UniDAW.Editor
{
    /// <summary>
    /// Claude への依頼（メロディ・コード進行）。範囲を決めて送り、返ってきた提案を Undo 付きで書き込む。
    /// 文章化と検証は <see cref="ClaudePrompt"/>、通信は差し替え可能な送信関数（既定は <see cref="ClaudeClient.Send"/>）。
    /// </summary>
    public sealed class ClaudeAssist
    {
        /// <summary>本文を送り、done(返答, 通信エラー) を呼ぶ。戻り値は中止。</summary>
        public delegate Action Sender(string body, Action<string, string> done);

        readonly ComposerSession s;
        readonly RollEditor roll;
        readonly Sender send;
        Action cancel;
        int requestId;

        public bool Busy { get; private set; }
        /// <summary>直近の状態（表示用）。</summary>
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public ClaudeAssist(ComposerSession session, RollEditor roll, Sender sender = null)
        {
            s = session;
            this.roll = roll;
            send = sender ?? ClaudeClient.Send;
        }

        ComposerState State => s.State;

        public void SetTarget(AiTarget t) => SetOption(() => State.aiTarget = t);
        public void SetModel(int i) => SetOption(() => State.aiModel = Math.Max(0, Math.Min(ClaudePrompt.ModelIds.Length - 1, i)));
        public void SetPrompt(string text) => State.aiPrompt = text ?? "";

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
            if (target != AiTarget.Chords && Song.Kinds[ch] == ChannelKind.Noise)
            {
                SetStatus("メロディは音程のあるチャンネル（Pulse / Triangle）で頼んでくださいまし", true);
                return false;
            }
            if (!s.CanEdit()) return false;

            int tl = p.TrackLength(ch), from, to;
            var sel = roll.Selection;
            if (sel.Count > 0)
            {
                var picked = roll.Notes().Where(n => sel.Contains(n.start)).ToList();
                from = picked.Min(n => n.start);
                to = picked.Max(n => n.End);
            }
            else
            {
                from = State.cursorRow % Math.Max(1, tl);
                to = from + State.assistRows;
            }
            to = Math.Min(to, tl);
            if (target != AiTarget.Chords && to <= from)
            {
                SetStatus("トラックの終わりですわ。カーソルを手前に置いてくださいまし", true);
                return false;
            }

            var (min, max, _) = AssistCommands.PitchRange(ch);
            var req = new ClaudePrompt.Request { target = target, channel = ch, from = from, to = to, minPitch = min, maxPitch = max, text = State.aiPrompt };
            string model = ClaudePrompt.ModelIds[Math.Max(0, Math.Min(ClaudePrompt.ModelIds.Length - 1, State.aiModel))];
            string body = ClaudePrompt.BuildRequestBody(model, ClaudePrompt.BuildUserMessage(song, State.patternIndex, State.orderIndex, req));

            int id = ++requestId;
            int pi = State.patternIndex;
            Busy = true;
            SetStatus($"Claude が考えています…（{ClaudePrompt.TargetNames[(int)target]}、行 {from}〜{to - 1}）", false);
            cancel = send(body, (json, netError) =>
            {
                if (id != requestId) return;
                Busy = false;
                cancel = null;
                if (netError != null)
                {
                    SetStatus(netError, true);
                    return;
                }
                var sg = ClaudePrompt.ParseResponse(json, out string error);
                if (sg == null)
                {
                    SetStatus(error, true);
                    return;
                }
                if (s.Song != song || State.patternIndex != pi || (target != AiTarget.Chords && State.cursorCh != ch))
                {
                    SetStatus("返答の間にパターンかチャンネルが変わったので、提案は書き込みませんでしたわ", true);
                    return;
                }
                Apply(sg, req);
            });
            return true;
        }

        public void Cancel()
        {
            if (!Busy) return;
            requestId++;
            Busy = false;
            var c = cancel;
            cancel = null;
            c?.Invoke();
            SetStatus("中止しましたわ", false);
        }

        void Apply(ClaudePrompt.Suggestion sg, ClaudePrompt.Request req)
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
                var notes = ClaudePrompt.FitNotes(sg.notes, req.from, req.to, req.minPitch, req.maxPitch);
                int inst = s.ChannelInstrument(req.channel);
                roll.ReplaceRange("AI Suggest", req.from, req.to,
                    notes.Select(n => new RollNote(n.start, n.length, n.pitch, Cell.NoteCell(n.pitch, inst))));
                done += (done.Length > 0 ? " / " : "") + $"{notes.Count} 音（行 {req.from}〜{req.to - 1}）";
            }
            SetStatus((sg.comment.Length > 0 ? sg.comment + "  " : "") + $"[{done}]", false);
        }
    }
}
