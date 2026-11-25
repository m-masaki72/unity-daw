using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>Claude に頼む内容。</summary>
    public enum AiTarget { Melody = 0, Chords = 1, MelodyAndChords = 2 }

    /// <summary>
    /// Claude 提案の純粋なロジック：曲の文脈の文章化、リクエスト JSON の組み立て、返答の読み取りと検証。
    /// 通信も編集もしない（テスト可能）。
    /// </summary>
    public static class ClaudePrompt
    {
        public static readonly string[] TargetNames = { "メロディ", "コード進行", "メロディ＋コード" };
        public static readonly string[] ModelIds = { "claude-opus-5-5", "claude-sonnet-5-5" };
        public static readonly string[] ModelNames = { "Opus 5.5（高品質）", "Sonnet 5.5（速い）" };

        public const int MaxTokens = 32000;

        /// <summary>頼む範囲（対象チャンネルの行 from..to-1）。</summary>
        public struct Request
        {
            public AiTarget target;
            public int channel, from, to, minPitch, maxPitch;
            public string text;
        }

        // ---- 返答の形 ----------------------------------------------------------------

        [Serializable]
        public sealed class SuggestedNote
        {
            public int start, length, pitch;
        }

        [Serializable]
        public sealed class Suggestion
        {
            public string comment;
            public string chords;
            public SuggestedNote[] notes;
        }

        [Serializable]
        sealed class ApiBlock
        {
            public string type;
            public string text;
        }

        [Serializable]
        sealed class ApiError
        {
            public string type;
            public string message;
        }

        [Serializable]
        sealed class ApiResponse
        {
            public string type;
            public ApiBlock[] content;
            public string stop_reason;
            public ApiError error;
        }

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"comment\":{\"type\":\"string\"}," +
            "\"chords\":{\"type\":\"string\"}," +
            "\"notes\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"start\":{\"type\":\"integer\"},\"length\":{\"type\":\"integer\"},\"pitch\":{\"type\":\"integer\"}}," +
            "\"required\":[\"start\",\"length\",\"pitch\"],\"additionalProperties\":false}}}," +
            "\"required\":[\"comment\",\"chords\",\"notes\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are a composition assistant inside UniDAW, a chiptune tracker with 4 channels: " +
            "Pulse 1 (lead melody), Pulse 2 (harmony / chords), Triangle (bass) and Noise (drums). " +
            "Time is a grid of rows; the song context states how many rows make a beat. " +
            "Each channel is monophonic: one note at a time.\n\n" +
            "Reply with JSON only, following the schema:\n" +
            "- comment: one or two short sentences in Japanese explaining your musical idea.\n" +
            "- notes: the new notes for the requested channel and row range. start is an absolute row inside the range, " +
            "length is in rows, pitch is a MIDI note number (60 = C4) inside the allowed range. Notes must not overlap. " +
            "Leave notes empty when only chords were requested.\n" +
            "- chords: a new chord progression for the whole pattern as space-separated chord symbols, spread evenly over the pattern " +
            "(for example \"C G Am F\" or \"Dm7 G7 Cmaj7 Cmaj7\"). Supported qualities: major, m, dim, aug, sus2, sus4, 7, maj7, m7, m7b5, 5, " +
            "and slash chords like C/E. Leave chords empty when only a melody was requested.\n\n" +
            "Follow the song's key and chords unless the request asks to change them. Make the result idiomatic for the request " +
            "and consistent with the surrounding material: motifs, rhythm, register and phrase length. " +
            "Prefer singable lines with clear phrasing and some rests.";

        // ---- 文脈 --------------------------------------------------------------------

        public static string NoteName(int midi) => Scale.RootNames[Scale.Pc(midi)] + (midi / 12 - 1);

        static string Notes(IEnumerable<MelodyModel.Note> notes, bool noise)
        {
            var parts = notes.Select(n => noise ? $"{n.start}" : $"{n.start}+{n.length} {NoteName(n.pitch)}({n.pitch})").ToList();
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
        }

        static string Chords(Pattern p, Scale key)
        {
            var list = ChordProgression.Parse(p.chords ?? "", key, out _);
            if (list.Count == 0) return "(none)";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
                sb.Append(i == 0 ? "" : ", ").Append(list[i].Name).Append(" @").Append(ChordProgression.StartRow(i, list.Count, p.length));
            return sb.ToString();
        }

        /// <summary>曲と頼みごとを Claude 向けの文章にする。</summary>
        public static string BuildUserMessage(Song song, int patternIndex, int orderIndex, Request req)
        {
            var p = song.GetPattern(patternIndex);
            var key = song.Key;
            int rpb = Math.Max(1, song.rowsPerBeat);
            var sb = new StringBuilder();
            sb.AppendLine("## Song");
            sb.AppendLine($"Tempo {song.bpm:0} BPM, {rpb} rows per beat ({rpb * 4} rows per 4/4 bar). Key: {(key.IsChromatic ? "not set" : key.ToString())}.");
            sb.AppendLine($"Current pattern {patternIndex} ({p.length} rows), order position {orderIndex + 1} of {song.order.Count}.");
            sb.AppendLine($"Chords (symbol @start row): {Chords(p, key)}");
            sb.AppendLine();
            sb.AppendLine("## Current pattern (notes as start+length name(MIDI); drums as hit rows)");
            for (int ch = 0; ch < Song.ChannelCount; ch++)
            {
                bool noise = Song.Kinds[ch] == ChannelKind.Noise;
                sb.AppendLine($"ch{ch} {Song.ChannelNames[ch]}: {Notes(MelodyModel.Extract(p, ch), noise)}");
            }

            if (req.target != AiTarget.Chords && orderIndex > 0 && orderIndex - 1 < song.order.Count)
            {
                int prev = song.PatternAtOrder(orderIndex - 1);
                var pp = song.GetPattern(prev);
                if (pp != null && prev != patternIndex)
                {
                    sb.AppendLine();
                    sb.AppendLine($"## Previous pattern in the order (pattern {prev}, {pp.length} rows, chords: {Chords(pp, key)})");
                    sb.AppendLine($"ch{req.channel} {Song.ChannelNames[req.channel]}: {Notes(MelodyModel.Extract(pp, req.channel), false)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Task");
            if (req.target != AiTarget.Chords)
                sb.AppendLine($"Write notes for ch{req.channel} {Song.ChannelNames[req.channel]}, rows {req.from} to {req.to - 1} " +
                              $"(existing notes there will be replaced). Allowed pitch range: {req.minPitch}-{req.maxPitch}.");
            if (req.target != AiTarget.Melody)
                sb.AppendLine($"Write a chord progression for the whole pattern ({p.length} rows).");
            if (req.target == AiTarget.MelodyAndChords)
                sb.AppendLine("The melody must fit the new chords.");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(req.text) ? "(no specific request: continue naturally)" : req.text.Trim());
            return sb.ToString();
        }

        /// <summary>Messages API へのリクエスト本文。</summary>
        public static string BuildRequestBody(string model, string userMessage) =>
            "{\"model\":" + Str(model) +
            ",\"max_tokens\":" + MaxTokens +
            ",\"system\":" + Str(SystemPrompt) +
            ",\"messages\":[{\"role\":\"user\",\"content\":" + Str(userMessage) + "}]" +
            ",\"output_config\":{\"effort\":\"high\",\"format\":{\"type\":\"json_schema\",\"schema\":" + Schema + "}}" +
            ",\"fallbacks\":\"default\"}";

        /// <summary>JSON 文字列リテラル。</summary>
        public static string Str(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---- 返答 --------------------------------------------------------------------

        /// <summary>API の返答を読む。失敗なら null と理由。</summary>
        public static Suggestion ParseResponse(string json, out string error)
        {
            error = null;
            ApiResponse r;
            try { r = JsonUtility.FromJson<ApiResponse>(json); }
            catch (Exception) { r = null; }
            if (r == null)
            {
                error = "返答を読めませんでした";
                return null;
            }
            if (r.type == "error" || r.error != null && !string.IsNullOrEmpty(r.error.message))
            {
                error = $"API エラー: {r.error?.type} {r.error?.message}".TrimEnd();
                return null;
            }
            if (r.stop_reason == "refusal")
            {
                error = "この依頼には応えられないと返されましたわ。言い方を変えてみてくださいまし";
                return null;
            }
            if (r.stop_reason == "max_tokens")
            {
                error = "返答が途中で切れました。範囲を短くしてくださいまし";
                return null;
            }
            var text = r.content?.LastOrDefault(b => b != null && b.type == "text")?.text;
            if (string.IsNullOrEmpty(text))
            {
                error = "返答が空でした";
                return null;
            }
            Suggestion sg;
            try { sg = JsonUtility.FromJson<Suggestion>(text); }
            catch (Exception) { sg = null; }
            if (sg == null)
            {
                error = "提案の形式を読めませんでした";
                return null;
            }
            sg.notes ??= Array.Empty<SuggestedNote>();
            sg.chords ??= "";
            sg.comment ??= "";
            return sg;
        }

        /// <summary>
        /// 提案のノートを範囲・音域・単音に収める：範囲外の開始は捨て、音域外はオクターブで寄せ、
        /// 同じ開始は先勝ち、重なりは次の音の手前で切る。
        /// </summary>
        public static List<MelodyModel.Note> FitNotes(IEnumerable<SuggestedNote> notes, int from, int to, int minPitch, int maxPitch)
        {
            var list = new List<MelodyModel.Note>();
            var seen = new HashSet<int>();
            foreach (var n in notes.Where(n => n != null && n.start >= from && n.start < to).OrderBy(n => n.start))
            {
                if (!seen.Add(n.start)) continue;
                int p = n.pitch;
                while (p < minPitch && p + 12 <= maxPitch) p += 12;
                while (p > maxPitch && p - 12 >= minPitch) p -= 12;
                p = Math.Max(minPitch, Math.Min(maxPitch, p));
                list.Add(new MelodyModel.Note(n.start, Math.Max(1, Math.Min(n.length, to - n.start)), p));
            }
            for (int i = 0; i + 1 < list.Count; i++)
            {
                var a = list[i];
                int gap = list[i + 1].start - a.start;
                if (a.length > gap) list[i] = new MelodyModel.Note(a.start, gap, a.pitch);
            }
            return list;
        }
    }
}
