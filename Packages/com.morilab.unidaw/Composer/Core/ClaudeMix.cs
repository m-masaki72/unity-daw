using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UniDAW.Composer
{
    /// <summary>
    /// ミックス（音量・パン・EQ・サイドチェイン・送り）の依頼文と、返ってきた設定を曲のミキサーに書く処理。
    /// ミュート / ソロとセンドエフェクト本体（ディレイタイム・リバーブの大きさ）は変えない。通信はしない（テスト可能）。
    /// </summary>
    public static class ClaudeMix
    {
        const string ChannelSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"channel\":{\"type\":\"integer\"},\"volumeDb\":{\"type\":\"number\"},\"pan\":{\"type\":\"number\"}," +
            "\"eqLow\":{\"type\":\"number\"},\"eqMid\":{\"type\":\"number\"},\"eqHigh\":{\"type\":\"number\"}," +
            "\"duck\":{\"type\":\"number\"},\"delaySend\":{\"type\":\"number\"},\"reverbSend\":{\"type\":\"number\"}}," +
            "\"required\":[\"channel\",\"volumeDb\",\"pan\",\"eqLow\",\"eqMid\",\"eqHigh\",\"duck\",\"delaySend\",\"reverbSend\"]," +
            "\"additionalProperties\":false}";

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{\"comment\":{\"type\":\"string\"}," +
            "\"mix\":{\"type\":\"array\",\"items\":" + ChannelSchema + "}," +
            "\"sidechainSource\":{\"type\":\"integer\"},\"duckRelease\":{\"type\":\"number\"}}," +
            "\"required\":[\"comment\",\"mix\",\"sidechainSource\",\"duckRelease\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are a mixing engineer inside UniDAW, a chiptune tracker with four channels: 0 Pulse 1 (usually the lead), " +
            "1 Pulse 2 (harmony / counter-melody / arpeggios), 2 Triangle (bass), 3 Noise (drums: kick, snare, hats). " +
            "Every channel strip has a fader, pan, a 3-band EQ, a sidechain duck amount and sends to a tempo-synced delay and a reverb.\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 3 clearly different mixes " +
            "(for example clean and punchy, wide and spacious, pumping dance mix), best first. Each candidate has:\n" +
            "- comment: one or two short sentences in Japanese describing the sound of the mix.\n" +
            "- mix: one entry per channel 0-3 with:\n" +
            "  - channel: the channel index.\n" +
            "  - volumeDb: fader in dB, -48 to +6 (usually -12 to 0; leave headroom, the lead sits on top).\n" +
            "  - pan: -1 (left) to 1 (right), 0 = center. Keep bass and kick centered.\n" +
            "  - eqLow, eqMid, eqHigh: gain in dB from -12 to +12 for a 120 Hz low shelf, a 1 kHz peak and a 6 kHz high shelf. " +
            "Chip waves are bright; small cuts usually work better than big boosts.\n" +
            "  - duck: 0 to 1, how deeply this channel dips when the sidechain source hits (0 = none; 0.3-0.6 for a pumping feel). " +
            "The source channel itself is never ducked.\n" +
            "  - delaySend, reverbSend: 0 to 1 send amounts (usually 0 to 0.4; keep the bass dry).\n" +
            "- sidechainSource: the channel index whose sound triggers the ducking (usually 3, the drums; it reacts mostly to kicks).\n" +
            "- duckRelease: 0 to 1, how quickly ducked channels come back (0 = 30 ms, 1 = 600 ms; around 0.3 follows a beat at 120-160 BPM).\n\n" +
            "Base the mix on what each channel actually plays and on the request.";

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>曲全体（順序表のパターン）でチャンネルが鳴らす内容の要約。</summary>
        internal static string ChannelContent(Song song, int ch)
        {
            var used = song.order.Where(i => i >= 0 && i < song.patterns.Count).Distinct().Select(i => song.patterns[i]).Where(p => p != null).ToList();
            var notes = used.SelectMany(p => MelodyModel.Extract(p, ch)).ToList();
            if (notes.Count == 0) return "silent";
            var insts = used.SelectMany(p => p.tracks[ch].cells.Take(p.length)).Where(c => c.HasNote && c.instrument >= 0 && c.instrument < song.instruments.Count)
                .Select(c => song.instruments[c.instrument]?.name).Where(n => !string.IsNullOrEmpty(n)).Distinct().Take(6);
            string range = Song.Kinds[ch] == ChannelKind.Noise ? $"{notes.Count} hits"
                : $"{notes.Count} notes {ClaudePrompt.NoteName(notes.Min(n => n.pitch))}-{ClaudePrompt.NoteName(notes.Max(n => n.pitch))}";
            return $"{range}, instruments {string.Join(", ", insts.DefaultIfEmpty("(default)"))}";
        }

        public static string BuildUserMessage(Song song, string request)
        {
            var sb = new StringBuilder();
            var key = song.Key;
            sb.AppendLine("## Song");
            sb.AppendLine($"Tempo {song.bpm:0} BPM. Key: {(key.IsChromatic ? "not set" : key.ToString())}. {song.order.Count} sections in the order.");
            sb.AppendLine();
            sb.AppendLine("## Channels (content over the whole song; current mixer settings)");
            for (int ch = 0; ch < Song.ChannelCount; ch++)
            {
                var m = song.Mix(ch);
                sb.AppendLine($"{ch} {Song.ChannelNames[ch]}: {ChannelContent(song, ch)}; volumeDb {F(m.volumeDb)}, pan {F(m.pan)}, " +
                              $"eq {F(m.eqLowDb)}/{F(m.eqMidDb)}/{F(m.eqHighDb)}, duck {F(m.duck)}, delaySend {F(m.delaySend)}, reverbSend {F(m.reverbSend)}" +
                              (m.mute ? ", muted" : ""));
            }
            var e = song.effects;
            sb.AppendLine($"Delay {SongEffects.DelayBeatNames[e.DelayChoice]} note, feedback {F(e.delayFeedback)}; reverb size {F(e.reverbSize)}; " +
                          $"sidechain source {e.sidechainSource}, duckRelease {F(e.duckRelease)}.");
            sb.AppendLine();
            sb.AppendLine("## Task");
            sb.AppendLine("Propose the mixer settings for all four channels.");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(request) ? "(no specific request: a balanced, polished mix)" : request.Trim());
            return sb.ToString();
        }

        /// <summary>提案をミキサーに書く（範囲外は丸める）。返り値は表示用の要約、書くものがなければ null（曲は変えない）。</summary>
        public static string Apply(Song song, ClaudeReply.Suggestion sg)
        {
            var list = (sg?.mix ?? Array.Empty<ClaudeReply.ChannelSetting>()).Where(x => x != null && x.channel >= 0 && x.channel < Song.ChannelCount).ToList();
            if (list.Count == 0) return null;
            var labels = new List<string>();
            foreach (var c in list)
            {
                var m = song.Mix(c.channel);
                m.volumeDb = ChannelMix.ClampDb(float.IsNaN(c.volumeDb) ? 0f : c.volumeDb);
                m.pan = ChannelMix.ClampPan(float.IsNaN(c.pan) ? 0f : c.pan);
                m.Set(MixParam.EqLow, c.eqLow);
                m.Set(MixParam.EqMid, c.eqMid);
                m.Set(MixParam.EqHigh, c.eqHigh);
                m.Set(MixParam.Duck, c.duck);
                m.SetSend(SendKind.Delay, c.delaySend);
                m.SetSend(SendKind.Reverb, c.reverbSend);
                labels.Add($"{Song.ChannelNames[c.channel]} {ChannelMix.FormatDb(m.volumeDb)}dB {ChannelMix.FormatPan(m.pan)}");
            }
            if (sg.sidechainSource >= 0 && sg.sidechainSource < Song.ChannelCount) song.effects.sidechainSource = sg.sidechainSource;
            if (sg.duckRelease >= 0f) song.effects.Set(FxParam.DuckRelease, sg.duckRelease);
            return string.Join(" / ", labels);
        }
    }
}
