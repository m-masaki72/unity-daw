using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// Claude の返答の読み取りと検証：API（Messages）と Claude Code（claude -p の結果 JSON）の両方を読み、
    /// 提案のノートを範囲・音域・単音に収める。通信も編集もしない（テスト可能）。
    /// </summary>
    public static class ClaudeReply
    {
        [Serializable]
        public sealed class SuggestedNote
        {
            public int start, length, pitch;
        }

        /// <summary>曲構成の 1 セクション（<see cref="ClaudeStructure"/>）。</summary>
        [Serializable]
        public sealed class Section
        {
            public string name, chords;
            public int source = -1, bars, transpose;
        }

        [Serializable]
        public sealed class Suggestion
        {
            public string comment;
            public string chords;
            public SuggestedNote[] notes;
            /// <summary>曲構成の依頼のときだけ入る。</summary>
            public Section[] sections;
            /// <summary>ミックスの依頼のときだけ入る（-1 は変えない）。</summary>
            public ChannelSetting[] mix;
            public int sidechainSource = -1;
            public float duckRelease = -1f;
            /// <summary>マスタリングの依頼のときだけ使う。</summary>
            public MasterSetting master;
            /// <summary>音色の依頼のときだけ入る（<see cref="ClaudeInstrument"/>）。</summary>
            public InstrumentSetting instrument;
            /// <summary>講評のときだけ入る：指摘と、次にやること（<see cref="ClaudeCritique"/>）。</summary>
            public string[] points;
            public NextAction[] next;
        }

        /// <summary>音色の提案：マクロを UniDAW の表記（"15 12 | 10 / 6 0"）で。</summary>
        [Serializable]
        public sealed class InstrumentSetting
        {
            public string name, volume, arpeggio, duty, pitch;
        }

        /// <summary>講評が勧める次の依頼（ボタン 1 つで頼める）。target は melody / chords / melodyAndChords / structure / mix / master / instrument。</summary>
        [Serializable]
        public sealed class NextAction
        {
            public string label, target, request;
            public int channel, pattern = -1;
        }

        /// <summary>マスタリングの提案（マスターチェーンの設定）。gain は返答には含まれず、ラウドネス合わせで決める。</summary>
        [Serializable]
        public sealed class MasterSetting
        {
            public float eqLow, eqMid, eqHigh, width = 1f, threshold = -12f, ratio = 1f, ceiling = -1f, gain;
            public bool limiter = true;
        }

        /// <summary>ミックスの提案の 1 チャンネル分。</summary>
        [Serializable]
        public sealed class ChannelSetting
        {
            public int channel = -1;
            public float volumeDb, pan, eqLow, eqMid, eqHigh, duck, delaySend, reverbSend;
        }

        /// <summary>返答の本体：候補の配列（candidates）か、以前の 1 案だけの形（comment / chords / notes）のどちらも読む。</summary>
        [Serializable]
        sealed class Body
        {
            public Suggestion[] candidates;
            public string comment;
            public string chords;
            public SuggestedNote[] notes;

            public bool HasContent =>
                candidates is { Length: > 0 } || notes is { Length: > 0 } || !string.IsNullOrEmpty(comment) || !string.IsNullOrEmpty(chords);

            public List<Suggestion> ToList()
            {
                var list = (candidates ?? Array.Empty<Suggestion>()).Where(c => c != null).ToList();
                if (list.Count == 0) list.Add(new Suggestion { comment = comment, chords = chords, notes = notes });
                foreach (var sg in list)
                {
                    sg.notes ??= Array.Empty<SuggestedNote>();
                    sg.sections ??= Array.Empty<Section>();
                    sg.chords ??= "";
                    sg.comment ??= "";
                    sg.points ??= Array.Empty<string>();
                    sg.next ??= Array.Empty<NextAction>();
                }
                return list;
            }
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
            // Claude Code（claude -p --output-format json）の結果
            public string subtype, result;
            public bool is_error;
            public Body structured_output;
        }

        /// <summary>API（Messages）か Claude Code（claude -p の結果 JSON）の返答を読み、候補（1 つ以上）を返す。失敗なら null と理由。</summary>
        public static List<Suggestion> Parse(string json, out string error)
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
            Body sg = null;
            if (r.type == "result")
            {
                if (r.is_error || r.subtype != "success")
                {
                    error = "Claude Code: " + (string.IsNullOrEmpty(r.result) ? r.subtype : r.result);
                    return null;
                }
                var so = r.structured_output;
                if (so != null && so.HasContent) sg = so;
            }
            if (sg == null)
            {
                var text = r.type == "result" ? r.result : r.content?.LastOrDefault(b => b != null && b.type == "text")?.text;
                if (string.IsNullOrEmpty(text))
                {
                    error = "返答が空でした";
                    return null;
                }
                try { sg = JsonUtility.FromJson<Body>(text); }
                catch (Exception) { sg = null; }
            }
            if (sg == null || !sg.HasContent)
            {
                error = "提案の形式を読めませんでした";
                return null;
            }
            return sg.ToList();
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
