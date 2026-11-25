using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniDAW.Editor
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
            // Claude Code（claude -p --output-format json）の結果
            public string subtype, result;
            public bool is_error;
            public Suggestion structured_output;
        }

        /// <summary>API（Messages）か Claude Code（claude -p の結果 JSON）の返答を読む。失敗なら null と理由。</summary>
        public static Suggestion Parse(string json, out string error)
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
            Suggestion sg = null;
            if (r.type == "result")
            {
                if (r.is_error || r.subtype != "success")
                {
                    error = "Claude Code: " + (string.IsNullOrEmpty(r.result) ? r.subtype : r.result);
                    return null;
                }
                var so = r.structured_output;
                if (so != null && (so.notes != null || !string.IsNullOrEmpty(so.comment) || !string.IsNullOrEmpty(so.chords))) sg = so;
            }
            if (sg == null)
            {
                var text = r.type == "result" ? r.result : r.content?.LastOrDefault(b => b != null && b.type == "text")?.text;
                if (string.IsNullOrEmpty(text))
                {
                    error = "返答が空でした";
                    return null;
                }
                try { sg = JsonUtility.FromJson<Suggestion>(text); }
                catch (Exception) { sg = null; }
            }
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
