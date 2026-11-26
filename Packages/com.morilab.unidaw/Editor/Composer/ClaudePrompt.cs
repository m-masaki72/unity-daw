using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UniDAW.Editor
{
    /// <summary>Claude に頼む内容。</summary>
    public enum AiTarget { Melody = 0, Chords = 1, MelodyAndChords = 2, Structure = 3, Mix = 4, Master = 5 }

    /// <summary>
    /// Claude への依頼文：曲の文脈の文章化と、返答の形（スキーマ）・システムプロンプト。返答の読み取りは <see cref="ClaudeReply"/>。
    /// 通信も編集もしない（テスト可能）。
    /// </summary>
    public static class ClaudePrompt
    {
        public static readonly string[] TargetNames = { "メロディ", "コード進行", "メロディ＋コード", "曲構成", "ミックス", "マスタリング" };
        /// <summary>チャンネルごとの役割（Assist の表示用）。</summary>
        public static readonly string[] PartNames = { "メロディ", "ハーモニー", "ベース", "ドラム" };

        /// <summary>ドラムは GM ドラム番号で頼み、<see cref="UniDAW.Midi.DrumMap"/> でノイズの音に置き換える。</summary>
        public const int DrumMinKey = 35, DrumMaxKey = 59;

        /// <summary>頼む内容の表示名（メロディの部分はカーソルのチャンネルの役割に置き換える）。</summary>
        public static string TargetLabel(AiTarget t, int ch)
        {
            string part = ch >= 0 && ch < PartNames.Length ? PartNames[ch] : TargetNames[0];
            return t == AiTarget.Melody ? part : t == AiTarget.MelodyAndChords ? part + "＋コード" : TargetNames[(int)t];
        }

        public static string[] TargetLabels(int ch) =>
            ((AiTarget[])Enum.GetValues(typeof(AiTarget))).Select(t => TargetLabel(t, ch)).ToArray();
        public static readonly string[] ModelIds = { "claude-opus-5-5", "claude-sonnet-5-5" };
        public static readonly string[] ModelNames = { "Opus 5.5（高品質）", "Sonnet 5.5（速い）" };


        /// <summary>頼む範囲（対象チャンネルの行 from..to-1）。</summary>
        public struct Request
        {
            public AiTarget target;
            public int channel, from, to, minPitch, maxPitch;
            public string text;
        }

        /// <summary>頼む候補の数。</summary>
        public const int CandidateCount = 3;

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"comment\":{\"type\":\"string\"}," +
            "\"chords\":{\"type\":\"string\"}," +
            "\"notes\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"start\":{\"type\":\"integer\"},\"length\":{\"type\":\"integer\"},\"pitch\":{\"type\":\"integer\"}}," +
            "\"required\":[\"start\",\"length\",\"pitch\"],\"additionalProperties\":false}}}," +
            "\"required\":[\"comment\",\"chords\",\"notes\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are a composition assistant inside UniDAW, a chiptune tracker with 4 channels: " +
            "Pulse 1 (lead melody), Pulse 2 (harmony / chords), Triangle (bass) and Noise (drums). " +
            "Time is a grid of rows; the song context states how many rows make a beat. " +
            "Each channel is monophonic: one note at a time.\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 3 clearly different ideas " +
            "(for example different contour, rhythm or harmonic colour), best first. Each candidate has:\n" +
            "- comment: one or two short sentences in Japanese explaining your musical idea.\n" +
            "- notes: the new notes for the requested channel and row range. start is an absolute row inside the range, " +
            "length is in rows, pitch is a MIDI note number (60 = C4) inside the allowed range. Notes must not overlap. " +
            "Leave notes empty when only chords were requested.\n" +
            "- chords: a new chord progression for the whole pattern as space-separated chord symbols, spread evenly over the pattern " +
            "(for example \"C G Am F\" or \"Dm7 G7 Cmaj7 Cmaj7\"). Supported qualities: major, m, dim, aug, sus2, sus4, 7, maj7, m7, m7b5, 5, " +
            "and slash chords like C/E. Leave chords empty when only a melody was requested.\n\n" +
            "Follow the song's key and chords unless the request asks to change them. Make the result idiomatic for the request " +
            "and consistent with the surrounding material: motifs, rhythm, register and phrase length. " +
            "Prefer singable lines with clear phrasing and some rests. " +
            "When the task is a drum part for the Noise channel, pitch is a General MIDI drum key instead of a note (the task lists them).";

        // ---- 文脈 --------------------------------------------------------------------

        public static string NoteName(int midi) => Scale.RootNames[Scale.Pc(midi)] + (midi / 12 - 1);

        static string Notes(IEnumerable<MelodyModel.Note> notes, bool noise)
        {
            var parts = notes.Select(n => noise ? $"{n.start}" : $"{n.start}+{n.length} {NoteName(n.pitch)}({n.pitch})").ToList();
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
        }

        /// <summary>ドラムの打点を「行 楽器名」で並べる（ノイズのピッチより楽器名のほうが伝わる）。</summary>
        static string DrumHits(Song song, Pattern p, int ch)
        {
            var parts = new List<string>();
            int tl = p.TrackLength(ch), inst = -1;
            for (int r = 0; r < tl; r++)
            {
                var c = p.tracks[ch].cells[r];
                if (c.instrument >= 0) inst = c.instrument;
                if (c.HasNote) parts.Add($"{r} {song.GetInstrument(inst)?.name ?? "hit"}");
            }
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
        }

        static string Part(Song song, Pattern p, int ch) =>
            Song.Kinds[ch] == ChannelKind.Noise ? DrumHits(song, p, ch) : Notes(MelodyModel.Extract(p, ch), false);

        /// <summary>チャンネルの役割に応じた書き方の注文。</summary>
        static string RoleHint(int ch)
        {
            switch (Song.Kinds[ch])
            {
                case ChannelKind.Noise:
                    return "This is the drum part. For each hit, pitch is a General MIDI drum key: 36 kick, 38 snare, 42 closed hi-hat, " +
                           "46 open hi-hat, 49 crash, 45 / 48 / 50 toms. One hit per row (the channel is monophonic); length is ignored, use 1. " +
                           "Lock the kick with the bass rhythm and keep a clear backbeat unless the request says otherwise; use fills and crashes " +
                           "to mark phrase ends.";
                case ChannelKind.Triangle:
                    return "This is the bass line (triangle wave, no volume control). Ground the harmony: chord roots on strong beats, " +
                           "with fifths, octaves and passing tones between them, and lock the rhythm with the kick drum hits listed for ch3.";
                default:
                    return ch == 1
                        ? "This channel is the harmony / counter-line: complement ch0 (answer its phrases, sustain chord tones) rather than doubling it."
                        : "This is the lead melody.";
            }
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
                sb.AppendLine($"ch{ch} {Song.ChannelNames[ch]}: {Part(song, p, ch)}");
            }

            if (req.target != AiTarget.Chords && orderIndex > 0 && orderIndex - 1 < song.order.Count)
            {
                int prev = song.PatternAtOrder(orderIndex - 1);
                var pp = song.GetPattern(prev);
                if (pp != null && prev != patternIndex)
                {
                    sb.AppendLine();
                    sb.AppendLine($"## Previous pattern in the order (pattern {prev}, {pp.length} rows, chords: {Chords(pp, key)})");
                    sb.AppendLine($"ch{req.channel} {Song.ChannelNames[req.channel]}: {Part(song, pp, req.channel)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Task");
            if (req.target != AiTarget.Chords)
            {
                sb.AppendLine($"Write notes for ch{req.channel} {Song.ChannelNames[req.channel]}, rows {req.from} to {req.to - 1} " +
                              $"(existing notes there will be replaced). Allowed pitch range: {req.minPitch}-{req.maxPitch}.");
                sb.AppendLine(RoleHint(req.channel));
            }
            if (req.target != AiTarget.Melody)
                sb.AppendLine($"Write a chord progression for the whole pattern ({p.length} rows).");
            if (req.target == AiTarget.MelodyAndChords)
                sb.AppendLine("The melody must fit the new chords.");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(req.text) ? "(no specific request: continue naturally)" : req.text.Trim());
            return sb.ToString();
        }
    }
}
