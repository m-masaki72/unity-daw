using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniDAW.Composer
{
    /// <summary>
    /// 曲構成（Intro / A メロ / B メロ / サビ…）の依頼文と、返ってきたセクション列を曲に組み立てる処理。
    /// 新しいセクションは元のパターンを複製して名前とコードを付け、同じ名前は同じパターンを使い回し、順序表を並べ直す。
    /// 既存のパターンの中身は変えない。通信はしない（テスト可能）。
    /// </summary>
    public static class ClaudeStructure
    {
        public const int MaxSections = 32, MaxNameLength = 16;

        const string SectionSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\"},\"source\":{\"type\":\"integer\"},\"bars\":{\"type\":\"integer\"}," +
            "\"chords\":{\"type\":\"string\"},\"transpose\":{\"type\":\"integer\"}}," +
            "\"required\":[\"name\",\"source\",\"bars\",\"chords\",\"transpose\"],\"additionalProperties\":false}";

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{\"comment\":{\"type\":\"string\"}," +
            "\"sections\":{\"type\":\"array\",\"items\":" + SectionSchema + "}}," +
            "\"required\":[\"comment\",\"sections\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are an arrangement assistant inside UniDAW, a chiptune tracker. A song is a list of patterns (sections) and an order " +
            "that plays them in sequence. You plan the song structure: which sections exist (intro, verse = Aメロ, pre-chorus = Bメロ, " +
            "chorus = サビ, interlude = 間奏, bridge = Cメロ, outro...) and in what order they play.\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 3 clearly different structures " +
            "(for example a short loop for a game, a full pop form, a build-up with a key change), best first. Each candidate has:\n" +
            "- comment: one or two short sentences in Japanese explaining the plan and how the energy develops.\n" +
            "- sections: the order, first to last. Repeat a section by repeating its exact name; every entry with the same name plays the " +
            "same pattern. Each entry has:\n" +
            "  - name: a short section name (at most 16 characters) such as Intro, Aメロ, Bメロ, サビ, 間奏, Outro. " +
            "To play an existing pattern unchanged, use its exact listed name.\n" +
            "  - source: for a new section, the index of the existing pattern to copy as its starting material (pick the closest in " +
            "role), or -1 to start empty. Ignored when the name is an existing pattern.\n" +
            "  - bars: length of a new section in 4/4 bars (usually 4 or 8). Ignored for existing patterns.\n" +
            "  - chords: the chord progression of a new section as space-separated chord symbols spread evenly over it " +
            "(qualities: major, m, dim, aug, sus2, sus4, 7, maj7, m7, m7b5, 5, slash chords like C/E). Empty keeps the copied chords. " +
            "Ignored for existing patterns.\n" +
            "  - transpose: semitones for this entry only (for example +1 or +2 on the last chorus), usually 0.\n\n" +
            "Stay in the song's key unless the request asks otherwise; make sections contrast (the chorus should lift: brighter chords, " +
            "a strong tonic arrival) and keep the whole song musically coherent.";

        /// <summary>音もコードも無いパターン（新しい曲の仮置きなど）。</summary>
        static bool IsBlank(Pattern p) =>
            string.IsNullOrWhiteSpace(p.chords) && p.tracks.All(t => t.cells.All(c => !c.HasNote));

        static string ChannelSummary(Pattern p, int ch)
        {
            var notes = MelodyModel.Extract(p, ch).ToList();
            if (notes.Count == 0) return "-";
            if (Song.Kinds[ch] == ChannelKind.Noise) return $"{notes.Count} hits";
            return $"{notes.Count} notes {ClaudePrompt.NoteName(notes.Min(n => n.pitch))}-{ClaudePrompt.NoteName(notes.Max(n => n.pitch))}";
        }

        /// <summary>パターンの表示名（無名は P0, P1…）。</summary>
        public static string PatternName(Song song, int i)
        {
            string n = song.patterns[i]?.name?.Trim();
            return string.IsNullOrEmpty(n) ? "P" + i : n;
        }

        public static string BuildUserMessage(Song song, string request)
        {
            var key = song.Key;
            int rpb = Math.Max(1, song.rowsPerBeat);
            var sb = new StringBuilder();
            sb.AppendLine("## Song");
            sb.AppendLine($"Tempo {song.bpm:0} BPM, {rpb} rows per beat ({rpb * 4} rows per 4/4 bar). Key: {(key.IsChromatic ? "not set" : key.ToString())}.");
            sb.AppendLine();
            sb.AppendLine("## Existing patterns (index: name, length, chords, channel contents)");
            for (int i = 0; i < song.patterns.Count; i++)
            {
                var p = song.patterns[i];
                if (p == null) continue;
                var parts = Enumerable.Range(0, Song.ChannelCount).Select(ch => $"{Song.ChannelNames[ch]} {ChannelSummary(p, ch)}");
                string chords = string.IsNullOrWhiteSpace(p.chords) ? "(none)" : p.chords.Trim();
                string blank = IsBlank(p) ? " (blank placeholder: not material; its name and length need not be kept)" : "";
                sb.AppendLine($"{i}: {PatternName(song, i)}, {p.length} rows ({p.length / (double)(rpb * 4):0.#} bars), chords {chords}; {string.Join("; ", parts)}{blank}");
            }
            sb.AppendLine();
            sb.AppendLine("## Current order");
            sb.AppendLine(string.Join(" -> ", song.order.Select((pi, i) =>
                (pi >= 0 && pi < song.patterns.Count ? PatternName(song, pi) : "?") + (song.TransposeAtOrder(i) != 0 ? $"({song.TransposeAtOrder(i):+0;-0})" : ""))));
            sb.AppendLine();
            sb.AppendLine("## Task");
            sb.AppendLine("Plan the structure of the whole song. Reuse the existing patterns where they fit (they are the material written so far) " +
                          "and add new sections where the song needs them; new sections will be filled in later.");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(request) ? "(no specific request: a complete, natural song form)" : request.Trim());
            return sb.ToString();
        }

        /// <summary>
        /// セクション列を曲に組み立てる（パターンの追加と順序表の置き換え）。source は sourceCount 未満の既存パターンだけ有効。
        /// 返り値は順序の表示（例 "Intro → Aメロ → サビ(+1)"）、組み立てられなければ null（曲は変えない）。
        /// </summary>
        public static string Build(Song song, IEnumerable<ClaudeReply.Section> sections, int sourceCount)
        {
            var list = (sections ?? Enumerable.Empty<ClaudeReply.Section>()).Where(x => x != null).Take(MaxSections).ToList();
            if (list.Count == 0 || song.patterns.Count == 0) return null;
            int rowsPerBar = Math.Max(1, song.rowsPerBeat) * 4;
            sourceCount = Math.Min(sourceCount, song.patterns.Count);
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < song.patterns.Count; i++)
            {
                byName.TryAdd(PatternName(song, i), i);
                byName.TryAdd("P" + i, i);
            }
            var order = new List<int>();
            var transpose = new List<int>();
            var labels = new List<string>();
            foreach (var sec in list)
            {
                string name = (sec.name ?? "").Trim();
                if (name.Length == 0) name = "Section";
                if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
                if (!byName.TryGetValue(name, out int pi))
                {
                    var src = sec.source >= 0 && sec.source < sourceCount ? song.patterns[sec.source] : null;
                    int len = sec.bars > 0 ? Math.Min(Pattern.MaxRows, Math.Min(sec.bars, Pattern.MaxRows) * rowsPerBar) : src?.length ?? song.patterns[0].length;
                    var p = src != null ? src.Clone() : new Pattern(len);
                    if (p.length != len) p.Resize(len);
                    p.name = name;
                    if (!string.IsNullOrWhiteSpace(sec.chords)) p.chords = sec.chords.Trim();
                    song.patterns.Add(p);
                    pi = song.patterns.Count - 1;
                    byName[name] = pi;
                }
                else if (!order.Contains(pi) && IsBlank(song.patterns[pi]))
                {
                    // 空の仮置きパターンは枠として使い、長さとコードは依頼に合わせる
                    var p = song.patterns[pi];
                    if (sec.bars > 0) p.Resize(Math.Min(Pattern.MaxRows, Math.Min(sec.bars, Pattern.MaxRows) * rowsPerBar));
                    if (!string.IsNullOrWhiteSpace(sec.chords)) p.chords = sec.chords.Trim();
                }
                int t = Math.Max(-12, Math.Min(12, sec.transpose));
                order.Add(pi);
                transpose.Add(t);
                labels.Add(name + (t != 0 ? $"({t:+0;-0})" : ""));
            }
            song.order = order;
            song.orderTranspose = transpose;
            return string.Join(" → ", labels);
        }
    }
}
