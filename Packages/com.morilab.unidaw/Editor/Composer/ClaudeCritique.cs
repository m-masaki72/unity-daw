using System;
using System.Linq;
using System.Text;

namespace UniDAW.Editor
{
    /// <summary>
    /// 講評：曲全体を見てもらい、良い点・直す点と「次にやること」（そのまま頼める依頼）を受け取る。
    /// 曲は書き換えない。通信はしない（テスト可能）。
    /// </summary>
    public static class ClaudeCritique
    {
        public const int MaxPoints = 8, MaxNext = 4;

        /// <summary>次の依頼の target の語と、<see cref="AiTarget"/> の対応。</summary>
        static readonly (string word, AiTarget target)[] Targets =
        {
            ("melody", AiTarget.Melody), ("chords", AiTarget.Chords), ("melodyAndChords", AiTarget.MelodyAndChords),
            ("structure", AiTarget.Structure), ("mix", AiTarget.Mix), ("master", AiTarget.Master), ("instrument", AiTarget.Instrument),
        };

        const string NextSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"label\":{\"type\":\"string\"},\"target\":{\"type\":\"string\",\"enum\":[\"melody\",\"chords\",\"melodyAndChords\",\"structure\",\"mix\",\"master\",\"instrument\"]}," +
            "\"channel\":{\"type\":\"integer\"},\"pattern\":{\"type\":\"integer\"},\"request\":{\"type\":\"string\"}}," +
            "\"required\":[\"label\",\"target\",\"channel\",\"pattern\",\"request\"],\"additionalProperties\":false}";

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{\"comment\":{\"type\":\"string\"}," +
            "\"points\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
            "\"next\":{\"type\":\"array\",\"items\":" + NextSchema + "}}," +
            "\"required\":[\"comment\",\"points\",\"next\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are an experienced chiptune composer and producer giving feedback inside UniDAW, a tracker with 4 channels: " +
            "Pulse 1 (lead), Pulse 2 (harmony), Triangle (bass) and Noise (drums). You review the whole song as it is now and say " +
            "what works, what to improve first, and which concrete requests the composer should run next with the built-in assistant.\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 1 review with:\n" +
            "- comment: two or three sentences in Japanese summing up the song and the single most important next step.\n" +
            "- points: up to 8 short observations in Japanese, most important first; start each with 「良い:」 or 「改善:」 and be specific " +
            "(name the section, channel and bars).\n" +
            "- next: up to 4 follow-up requests, most useful first. Each has:\n" +
            "  - label: a short Japanese button caption (at most 20 characters).\n" +
            "  - target: melody (notes for one channel; on ch3 this is the drum part), chords, melodyAndChords, structure, mix, master or instrument " +
            "(the sound of the instrument used on the channel).\n" +
            "  - channel: 0-3 (the channel the request is about; any value for structure / mix / master).\n" +
            "  - pattern: the pattern index to work on (-1 when it is not about one pattern).\n" +
            "  - request: the request text in Japanese, written as the composer would type it to the assistant.\n\n" +
            "Judge it as music for its purpose (melody, harmony, rhythm, arrangement, contrast between sections, sound and mix), not as code.";

        /// <summary>target の語を <see cref="AiTarget"/> にする（知らない語は null）。</summary>
        public static AiTarget? ParseTarget(string word)
        {
            foreach (var (w, t) in Targets)
                if (string.Equals(w, word?.Trim(), StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        public static string BuildUserMessage(Song song, string request)
        {
            var key = song.Key;
            int rpb = Math.Max(1, song.rowsPerBeat);
            var sb = new StringBuilder();
            sb.AppendLine("## Song");
            sb.AppendLine($"Title \"{song.title}\". Tempo {song.bpm:0} BPM, {rpb} rows per beat ({rpb * 4} rows per 4/4 bar). " +
                          $"Key: {(key.IsChromatic ? "not set" : key.ToString())}.");
            sb.AppendLine("Order: " + string.Join(" -> ", song.order.Select((pi, i) =>
                (pi >= 0 && pi < song.patterns.Count ? $"{ClaudeStructure.PatternName(song, pi)}[{pi}]" : "?") +
                (song.TransposeAtOrder(i) != 0 ? $"({song.TransposeAtOrder(i):+0;-0})" : ""))));
            sb.AppendLine();
            sb.AppendLine("## Patterns in the order (notes as start+length name(MIDI); drums as hit rows)");
            foreach (int pi in song.order.Where(i => i >= 0 && i < song.patterns.Count).Distinct())
            {
                var p = song.patterns[pi];
                if (p == null) continue;
                sb.AppendLine($"### Pattern {pi} {ClaudeStructure.PatternName(song, pi)} ({p.length} rows), chords: {ClaudePrompt.Chords(p, key)}");
                for (int ch = 0; ch < Song.ChannelCount; ch++)
                    sb.AppendLine($"ch{ch} {Song.ChannelNames[ch]}: {ClaudePrompt.Part(song, p, ch)}");
            }
            sb.AppendLine();
            sb.AppendLine("## Sound");
            for (int ch = 0; ch < Song.ChannelCount; ch++)
            {
                var m = song.Mix(ch);
                sb.AppendLine($"ch{ch}: {ClaudeMix.ChannelContent(song, ch)}; volumeDb {m.volumeDb:0.#}, pan {m.pan:0.##}" + (m.mute ? ", muted" : ""));
            }
            sb.AppendLine("Master chain " + (song.master != null && song.master.enabled ? "on" : "off") + ".");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(request) ? "(no specific focus: review the whole song)" : request.Trim());
            return sb.ToString();
        }
    }
}
