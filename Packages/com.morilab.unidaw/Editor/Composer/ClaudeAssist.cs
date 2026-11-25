using System;
using System.Collections.Generic;
using System.Linq;

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
            send = sender ?? ((model, msg, done) => ClaudeClient.Send(model, ClaudePrompt.SystemPrompt, ClaudePrompt.Schema, msg, done));
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
            if (!s.CanEdit()) return false;

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
            to = Math.Min(to, tl);
            if (target != AiTarget.Chords && to <= from)
            {
                SetStatus("トラックの終わりですわ。カーソルを手前に置いてくださいまし", true);
                return false;
            }

            var (min, max, _) = Song.Kinds[ch] == ChannelKind.Noise
                ? (ClaudePrompt.DrumMinKey, ClaudePrompt.DrumMaxKey, 0)
                : AssistCommands.PitchRange(ch);
            var req = new ClaudePrompt.Request { target = target, channel = ch, from = from, to = to, minPitch = min, maxPitch = max, text = State.aiPrompt };
            string model = ClaudePrompt.ModelIds[Math.Max(0, Math.Min(ClaudePrompt.ModelIds.Length - 1, State.aiModel))];
            string message = ClaudePrompt.BuildUserMessage(song, State.patternIndex, State.orderIndex, req);

            int id = ++requestId;
            int pi = State.patternIndex;
            Busy = true;
            SetStatus($"Claude が考えています…（{ClaudePrompt.TargetLabel(target, ch)}、行 {from}〜{to - 1}）", false);
            cancel = send(model, message, (json, netError) =>
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
                if (s.Song != song || State.patternIndex != pi || (target != AiTarget.Chords && State.cursorCh != ch))
                {
                    SetStatus("返答の間にパターンかチャンネルが変わったので、提案は書き込みませんでしたわ", true);
                    return;
                }
                candidates = list;
                lastReq = req;
                lastSong = song;
                lastPattern = pi;
                originalNotes = target != AiTarget.Chords ? roll.Notes().Where(n => n.start >= req.from && n.start < req.to).ToList() : null;
                originalChords = s.CurrentPattern.chords ?? "";
                Chosen = -1;
                Choose(0);
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

        /// <summary>候補 i を書き込む（-1 で依頼前の内容に戻す）。それぞれ 1 つの Undo になる。</summary>
        public bool Choose(int i)
        {
            if (Busy || candidates.Count == 0 || i < -1 || i >= candidates.Count || i == Chosen) return false;
            var req = lastReq;
            if (s.Song != lastSong || State.patternIndex != lastPattern || (req.target != AiTarget.Chords && State.cursorCh != req.channel))
            {
                SetStatus("パターンかチャンネルが変わったので、候補は切り替えられませんわ", true);
                return false;
            }
            if (!s.CanEdit()) return false;
            Chosen = i;
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
                roll.ReplaceRange("AI Suggest", req.from, req.to, Song.Kinds[req.channel] == ChannelKind.Noise
                    ? DrumNotes(notes, req.to)
                    : notes.Select(n => new RollNote(n.start, n.length, n.pitch, Cell.NoteCell(n.pitch, inst))));
                done += (done.Length > 0 ? " / " : "") + $"{notes.Count} 音（行 {req.from}〜{req.to - 1}）";
            }
            SetStatus(label + (sg.comment.Length > 0 ? sg.comment + "  " : "") + $"[{done}]", false);
        }
    }
}
