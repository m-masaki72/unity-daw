using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UniDAW.Editor
{
    /// <summary>
    /// マスタリング（マスターチェーンの EQ・幅・コンプ・リミッター）の依頼文と、返ってきた設定からチェーンを組む処理。
    /// 依頼文には <see cref="MasterAnalysis"/> の解析結果を添える。ゲインは返答のあとで目標ラウドネスに合わせる（<see cref="MatchGains"/>）。
    /// 通信はしない（テスト可能）。
    /// </summary>
    public static class ClaudeMaster
    {
        const string SettingSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"eqLow\":{\"type\":\"number\"},\"eqMid\":{\"type\":\"number\"},\"eqHigh\":{\"type\":\"number\"},\"width\":{\"type\":\"number\"}," +
            "\"threshold\":{\"type\":\"number\"},\"ratio\":{\"type\":\"number\"},\"ceiling\":{\"type\":\"number\"},\"limiter\":{\"type\":\"boolean\"}}," +
            "\"required\":[\"eqLow\",\"eqMid\",\"eqHigh\",\"width\",\"threshold\",\"ratio\",\"ceiling\",\"limiter\"],\"additionalProperties\":false}";

        const string CandidateSchema =
            "{\"type\":\"object\",\"properties\":{\"comment\":{\"type\":\"string\"},\"master\":" + SettingSchema + "}," +
            "\"required\":[\"comment\",\"master\"],\"additionalProperties\":false}";

        public const string Schema =
            "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"items\":" + CandidateSchema + "}}," +
            "\"required\":[\"candidates\"],\"additionalProperties\":false}";

        public const string SystemPrompt =
            "You are a mastering engineer inside UniDAW, a chiptune tracker with four channels: 0 Pulse 1 (usually the lead), " +
            "1 Pulse 2 (harmony / arpeggios), 2 Triangle (bass), 3 Noise (drums). The mix is already done; you set the master chain " +
            "that processes the stereo mix in this order: 3-band EQ -> stereo width (mid/side) -> compressor (L/R linked, 10 ms attack, 150 ms release) " +
            "-> gain -> look-ahead brickwall limiter.\n\n" +
            "You receive a loudness analysis of the mix with the chain bypassed (integrated loudness in LUFS, sample peak, PLR = peak minus loudness, " +
            "short-term maximum, energy shares below 200 Hz and above 4 kHz, left/right correlation, per-channel loudness, and rule-based notes).\n\n" +
            "Reply with JSON only, following the schema: an object whose candidates array holds exactly 3 clearly different masters " +
            "(for example transparent and clean, warm and punchy, bright and wide), best first. Each candidate has:\n" +
            "- comment: one or two short sentences in Japanese describing the sound and why it suits this song (mention what in the analysis you addressed).\n" +
            "- master:\n" +
            "  - eqLow, eqMid, eqHigh: gain in dB from -12 to +12 for a 120 Hz low shelf, a 1 kHz peak and a 6 kHz high shelf. " +
            "Mastering moves are gentle: usually within +-3 dB. Square waves are already bright and buzzy.\n" +
            "  - width: 0 (mono) to 2; 1 leaves the image unchanged. Values up to about 1.4 widen a narrow mix; " +
            "lower it if the correlation is low. A mono mix (correlation near 1) cannot be widened.\n" +
            "  - threshold: compressor threshold in dBFS, -30 to 0 (applied to the mix whose peak level is given).\n" +
            "  - ratio: 1 (off) to 10. Chiptune is already dense; 1.5 to 3 is typical, keep PLR above about 6 dB.\n" +
            "  - ceiling: limiter ceiling in dBFS, -6 to 0 (usually -1 for streaming, -0.3 for louder masters).\n" +
            "  - limiter: whether the limiter is on (normally true).\n\n" +
            "Do not worry about loudness: after your reply the gain of every candidate is set automatically so that it reaches the target loudness. " +
            "Focus on tone, balance, density and width, and follow the request.";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("0.##", Inv);
        static string Db(float v) => float.IsNegativeInfinity(v) ? "silent" : v.ToString("0.0", Inv);

        /// <summary>依頼文：曲の概要・チャンネルの中身と音量・解析結果・今のチェーン・目標ラウドネス・要望。</summary>
        public static string BuildUserMessage(Song song, MasterReport report, string request)
        {
            var sb = new StringBuilder();
            var key = song.Key;
            sb.AppendLine("## Song");
            sb.AppendLine($"Tempo {song.bpm:0} BPM. Key: {(key.IsChromatic ? "not set" : key.ToString())}. {song.order.Count} sections in the order.");
            sb.AppendLine();
            sb.AppendLine("## Channels (content over the whole song; mixer)");
            for (int ch = 0; ch < Song.ChannelCount; ch++)
            {
                var m = song.Mix(ch);
                string lufs = report?.channelLufs != null && ch < report.channelLufs.Length ? $", loudness {Db(report.channelLufs[ch])} LUFS" : "";
                sb.AppendLine($"{ch} {Song.ChannelNames[ch]}: {ClaudeMix.ChannelContent(song, ch)}; volumeDb {F(m.volumeDb)}, pan {F(m.pan)}{lufs}" +
                              (m.mute ? ", muted" : ""));
            }
            sb.AppendLine();
            if (report != null)
            {
                var s = report.mix;
                sb.AppendLine("## Analysis of the mix (master chain bypassed)");
                sb.AppendLine($"Integrated loudness {Db(s.integratedLufs)} LUFS, sample peak {Db(s.peakDb)} dBFS, PLR {(s.Silent ? "-" : s.Plr.ToString("0.0", Inv))} dB, " +
                              $"short-term max {Db(s.shortTermMaxLufs)} LUFS, momentary max {Db(s.momentaryMaxLufs)} LUFS, length {s.seconds:0} s.");
                sb.AppendLine($"Energy share: low (<200 Hz) {s.lowShare:P0}, mid {s.midShare:P0}, high (>4 kHz) {s.highShare:P0}. " +
                              $"Left/right correlation {s.correlation.ToString("0.00", Inv)}.");
                if (report.hints.Count > 0)
                {
                    sb.AppendLine("Notes:");
                    foreach (var h in report.hints) sb.AppendLine("- " + h);
                }
                sb.AppendLine();
            }
            var cur = song.master ?? new SongMaster();
            sb.AppendLine("## Current master chain");
            sb.AppendLine(cur.enabled
                ? $"On: eq {F(cur.eqLowDb)}/{F(cur.eqMidDb)}/{F(cur.eqHighDb)}, width {F(cur.width)}, threshold {F(cur.compThresholdDb)}, ratio {F(cur.compRatio)}, " +
                  $"gain {F(cur.gainDb)}, limiter {(cur.limiter ? "on" : "off")}, ceiling {F(cur.ceilingDb)}"
                : "Off (bypassed).");
            sb.AppendLine($"Target loudness: {F(cur.targetLufs)} LUFS.");
            sb.AppendLine();
            sb.AppendLine("## Task");
            sb.AppendLine("Propose 3 master chain settings.");
            sb.AppendLine();
            sb.AppendLine("## Request");
            sb.AppendLine(string.IsNullOrWhiteSpace(request) ? "(no specific request: a polished, release-ready master)" : request.Trim());
            return sb.ToString();
        }

        /// <summary>提案からチェーンを組む（範囲外は丸める。目標ラウドネスは <paramref name="original"/> のまま、マスターは ON）。</summary>
        public static SongMaster Build(SongMaster original, ClaudeReply.MasterSetting st)
        {
            var m = (original ?? new SongMaster()).Clone();
            st ??= new ClaudeReply.MasterSetting();
            m.enabled = true;
            m.Set(MasterParam.EqLow, st.eqLow);
            m.Set(MasterParam.EqMid, st.eqMid);
            m.Set(MasterParam.EqHigh, st.eqHigh);
            m.Set(MasterParam.Width, st.width);
            m.Set(MasterParam.Threshold, st.threshold);
            m.Set(MasterParam.Ratio, st.ratio);
            m.Set(MasterParam.Ceiling, st.ceiling);
            m.Set(MasterParam.Gain, st.gain);
            m.limiter = st.limiter;
            return m;
        }

        /// <summary>候補ごとにゲインを目標ラウドネスへ合わせる（重い：バックグラウンドで呼ぶ。候補は並列に測る）。</summary>
        public static void MatchGains(Song song, SongMaster original, IList<ClaudeReply.Suggestion> list)
        {
            System.Threading.Tasks.Parallel.For(0, list.Count, i =>
            {
                var st = list[i].master;
                if (st == null) return;
                var matched = MasterAnalysis.MatchLoudness(song, Build(original, st), out _);
                if (matched != null) st.gain = matched.gainDb;
            });
        }

        /// <summary>表示用の要約。</summary>
        public static string Label(SongMaster m) =>
            $"EQ {SongMaster.FormatParam(MasterParam.EqLow, m.eqLowDb)}/{SongMaster.FormatParam(MasterParam.EqMid, m.eqMidDb)}/{SongMaster.FormatParam(MasterParam.EqHigh, m.eqHighDb)}" +
            $" 幅 {SongMaster.FormatParam(MasterParam.Width, m.width)}" +
            $" コンプ {SongMaster.FormatParam(MasterParam.Threshold, m.compThresholdDb)} {SongMaster.FormatParam(MasterParam.Ratio, m.compRatio)}" +
            $" ゲイン {SongMaster.FormatParam(MasterParam.Gain, m.gainDb)}" +
            (m.limiter ? $" CEIL {SongMaster.FormatParam(MasterParam.Ceiling, m.ceilingDb)}" : " リミッター off");
    }
}
