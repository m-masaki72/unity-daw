using System;
using System.Linq;
using System.Text;

namespace UniDAW.Composer
{
    /// <summary>
    /// 音色（楽器のマクロ）の依頼文と、返ってきたマクロを楽器に組み立てる処理。
    /// マクロは UniDAW の表記（"15 12 | 10 / 6 0"、| の次がループ開始、/ の次がリリース）でやりとりする。通信はしない（テスト可能）。
    /// </summary>
    public static class ClaudeInstrument
    {
        public const int MaxMacroLength = 64, MaxNameLength = 16;

        const string InstrumentSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"name\":{\"type\":\"string\"},\"volume\":{\"type\":\"string\"},\"arpeggio\":{\"type\":\"string\"}," +
            "\"duty\":{\"type\":\"string\"},\"pitch\":{\"type\":\"string\"}}," +
            "\"required\":[\"name\",\"volume\",\"arpeggio\",\"duty\",\"pitch\"],\"additionalProperties\":false}";

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{\"comment\":{\"type\":\"string\"},\"instrument\":" + InstrumentSchema + "}," +
            "\"required\":[\"comment\",\"instrument\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are a chiptune sound designer inside UniDAW, a tracker modelled on the NES sound chip: two pulse channels, a triangle " +
            "and a noise channel. An instrument is four macros that step once per engine tick while a note plays:\n" +
            "- volume: 0-15 per tick (the envelope). Triangle has no volume control (only 0 = silent / other = on).\n" +
            "- arpeggio: semitone offsets per tick (chords, octave blips, drum-like pitch drops). Empty = none.\n" +
            "- duty: pulse width per tick, 0 = 12.5% (thin), 1 = 25%, 2 = 50% (hollow square), 3 = 75%. On noise, 1 = short (metallic) mode. Ignored on triangle.\n" +
            "- pitch: fine pitch per tick in 1/16 semitone, accumulated (relative); a repeating -1 1 1 -1 is a vibrato, a run of negatives a falling bend. Empty = none.\n" +
            "Macro text: space-separated integers; put \"|\" before the step where the loop starts and \"/\" before the release part " +
            "that plays after note off. Without a loop the last value is held. Example: \"15 13 11 | 10 9 10 / 6 3 0\". At most 64 steps.\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 3 clearly different sounds " +
            "for the request, best first. Each candidate has:\n" +
            "- comment: one or two short sentences in Japanese describing the sound and where it fits.\n" +
            "- instrument: name (short, at most 16 characters, English) and the four macros as text (empty string for an unused macro).\n\n" +
            "Design for the channel the instrument is used on and the tick rate given in the context (a tick is short at fast tempos).";

        /// <summary>楽器の 4 マクロを 1 行で。</summary>
        public static string Describe(Instrument inst) =>
            $"volume \"{inst.volume}\", arpeggio \"{inst.arpeggio}\", duty \"{inst.duty}\", pitch \"{inst.pitch}\"";

        public static string BuildUserMessage(Song song, int ch, int instrumentIndex, string request)
        {
            var inst = song.GetInstrument(instrumentIndex);
            int rpb = Math.Max(1, song.rowsPerBeat), speed = Math.Max(1, song.speed);
            double ticksPerSecond = song.bpm * rpb * speed / 60.0;
            var sb = new StringBuilder();
            sb.AppendLine("## Song");
            sb.AppendLine($"Tempo {song.bpm:0} BPM, {rpb} rows per beat, {speed} ticks per row: about {ticksPerSecond:0} ticks per second.");
            sb.AppendLine();
            sb.AppendLine("## Channel");
            sb.AppendLine($"ch{ch} {Song.ChannelNames[ch]} ({Song.Kinds[ch]}): {ClaudeMix.ChannelContent(song, ch)}. Role: {ClaudePrompt.PartNames[ch]}.");
            sb.AppendLine();
            sb.AppendLine("## Current instrument");
            sb.AppendLine(inst == null ? "(none)" : $"#{instrumentIndex} {inst.name}: {Describe(inst)}");
            var others = song.instruments.Select((x, i) => (x, i)).Where(t => t.x != null && t.i != instrumentIndex).Take(16).ToList();
            if (others.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Other instruments in the song");
                foreach (var (x, i) in others) sb.AppendLine($"#{i} {x.name}: {Describe(x)}");
            }
            sb.AppendLine();
            sb.AppendLine("## Task");
            sb.AppendLine($"Design a new sound for the current instrument (it replaces its macros; every note that uses #{instrumentIndex} will change).");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(request) ? "(no specific request: a polished sound that suits the channel's part)" : request.Trim());
            return sb.ToString();
        }

        /// <summary>マクロの文字列を読み、値を範囲に収め、長さを切る。</summary>
        static Sequence Macro(string text, MacroKind kind, ChannelKind channel)
        {
            var seq = Sequence.Parse(text ?? "");
            Instrument.MacroRange(kind, out int min, out int max);
            if (kind == MacroKind.Duty && channel == ChannelKind.Noise) max = 1;
            var values = seq.values.Take(MaxMacroLength).Select(v => Math.Max(min, Math.Min(max, v))).ToArray();
            return new Sequence
            {
                values = values,
                loop = seq.loop >= 0 && seq.loop < values.Length ? seq.loop : -1,
                release = seq.release >= 0 && seq.release < values.Length ? seq.release : -1,
            };
        }

        /// <summary>提案から楽器を作る（名前が空なら元の名前）。提案が無ければ元の複製。</summary>
        public static Instrument Build(Instrument original, ClaudeReply.InstrumentSetting set, ChannelKind channel)
        {
            var inst = original?.Clone() ?? new Instrument();
            if (set == null) return inst;
            string name = (set.name ?? "").Trim();
            if (name.Length > 0) inst.name = name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
            inst.volume = Macro(set.volume, MacroKind.Volume, channel);
            inst.arpeggio = Macro(set.arpeggio, MacroKind.Arpeggio, channel);
            inst.duty = Macro(set.duty, MacroKind.Duty, channel);
            inst.pitch = Macro(set.pitch, MacroKind.Pitch, channel);
            return inst;
        }
    }
}
