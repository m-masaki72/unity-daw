using System;
using System.Linq;
using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>Claude 提案の文章化・返答の読み取り・書き込み（通信はしない）。</summary>
    public class ClaudeAssistTests
    {
        static string Reply(string innerJson, string stop = "end_turn") =>
            "{\"type\":\"message\",\"stop_reason\":\"" + stop + "\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":" + ClaudeClient.JsonString(innerJson) + "}]}";

        [Test]
        public void Str_EscapesJson()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\nd\\u0001\"", ClaudeClient.JsonString("a\"b\\c\nd\u0001"));
        }

        [Test]
        public void BuildRequest_HasModelSchemaAndContext()
        {
            var song = Song.CreateDefault(32);
            try
            {
                song.keyRoot = 9;
                song.scaleMode = ScaleMode.Minor;
                song.patterns[0].chords = "Am F C G";
                song.patterns[0].tracks[0].cells[0] = Cell.NoteCell(69, 0);
                var req = new ClaudePrompt.Request { target = AiTarget.Melody, channel = 0, from = 8, to = 24, minPitch = 48, maxPitch = 96, text = "切なく" };
                string msg = ClaudePrompt.BuildUserMessage(song, 0, 0, req);
                StringAssert.Contains("A Minor", msg);
                StringAssert.Contains("Am @0", msg);
                StringAssert.Contains("0+", msg);
                StringAssert.Contains("A4(69)", msg);
                StringAssert.Contains("rows 8 to 23", msg);
                StringAssert.Contains("切なく", msg);

                string body = ClaudeClient.BuildApiBody("claude-opus-5-5", ClaudePrompt.SystemPrompt, ClaudePrompt.Schema, msg);
                StringAssert.Contains("\"model\":\"claude-opus-5-5\"", body);
                StringAssert.Contains("\"json_schema\"", body);
                StringAssert.DoesNotContain("x-api-key", body);
                Assert.IsFalse(body.Contains("\n"), "改行はエスケープ済み");
            }
            finally
            {
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ParseResponse_ReadsSuggestionAndErrors()
        {
            var sg = ClaudeReply.Parse(Reply("{\"comment\":\"上行で\",\"chords\":\"\",\"notes\":[{\"start\":8,\"length\":4,\"pitch\":72}]}"), out var err);
            Assert.IsNull(err);
            Assert.AreEqual("上行で", sg.comment);
            Assert.AreEqual(1, sg.notes.Length);
            Assert.AreEqual(72, sg.notes[0].pitch);

            Assert.IsNull(ClaudeReply.Parse("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", out err));
            StringAssert.Contains("authentication_error", err);
            Assert.IsNull(ClaudeReply.Parse(Reply("{}", "refusal"), out err));
            Assert.IsNotNull(err);
            Assert.IsNull(ClaudeReply.Parse(Reply("{}", "max_tokens"), out err));
            Assert.IsNull(ClaudeReply.Parse("not json", out err));
            Assert.IsNotNull(err);
        }

        [Test]
        public void ParseResponse_ReadsClaudeCodeResult()
        {
            string ok = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"stop_reason\":\"tool_use\",\"result\":\"\"," +
                        "\"structured_output\":{\"comment\":\"明るく\",\"chords\":\"C\",\"notes\":[{\"start\":0,\"length\":4,\"pitch\":64}]}}";
            var sg = ClaudeReply.Parse(ok, out var err);
            Assert.IsNull(err);
            Assert.AreEqual("明るく", sg.comment);
            Assert.AreEqual(64, sg.notes[0].pitch);

            // structured_output がなければ result の文字列を JSON として読む
            string text = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"{\\\"comment\\\":\\\"x\\\",\\\"chords\\\":\\\"Am\\\",\\\"notes\\\":[]}\"}";
            sg = ClaudeReply.Parse(text, out err);
            Assert.IsNull(err);
            Assert.AreEqual("Am", sg.chords);

            Assert.IsNull(ClaudeReply.Parse("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"Not logged in\"}", out err));
            StringAssert.Contains("Not logged in", err);
            Assert.IsNull(ClaudeReply.Parse("{\"type\":\"result\",\"subtype\":\"error_max_turns\",\"is_error\":false}", out err));
            StringAssert.Contains("error_max_turns", err);
        }

        [Test]
        public void Backend_AutoPrefersKeyThenClaudeCode()
        {
            Assert.AreEqual(ClaudeBackend.Api, ClaudeClient.Resolve(ClaudeBackend.Auto, true, true));
            Assert.AreEqual(ClaudeBackend.ClaudeCode, ClaudeClient.Resolve(ClaudeBackend.Auto, false, true));
            Assert.AreEqual(ClaudeBackend.Api, ClaudeClient.Resolve(ClaudeBackend.Auto, false, false));
            Assert.AreEqual(ClaudeBackend.ClaudeCode, ClaudeClient.Resolve(ClaudeBackend.ClaudeCode, true, false), "明示した選択は優先");
        }

        [Test]
        public void Quote_FollowsWindowsArgumentRules()
        {
            Assert.AreEqual("\"\"", ClaudeCodeClient.Quote(""));
            Assert.AreEqual("\"a b\"", ClaudeCodeClient.Quote("a b"));
            Assert.AreEqual("\"say \\\"hi\\\"\"", ClaudeCodeClient.Quote("say \"hi\""));
            Assert.AreEqual("\"C:\\dir\\\\\"", ClaudeCodeClient.Quote("C:\\dir\\"), "末尾の \\ は二重に");
            Assert.AreEqual("\"a\\\\\\\"b\"", ClaudeCodeClient.Quote("a\\\"b"));
        }

        [Test]
        public void FitNotes_ClampsRangePitchAndOverlap()
        {
            var raw = new[]
            {
                new ClaudeReply.SuggestedNote { start = 4, length = 2, pitch = 60 },   // 範囲外
                new ClaudeReply.SuggestedNote { start = 10, length = 8, pitch = 40 },  // 音域外 → 52、次で切る
                new ClaudeReply.SuggestedNote { start = 12, length = 20, pitch = 100 }, // 音域外 → 88、範囲末で切る
                new ClaudeReply.SuggestedNote { start = 12, length = 1, pitch = 70 },  // 同じ開始は捨てる
            };
            var fit = ClaudeReply.FitNotes(raw, 8, 16, 48, 96);
            Assert.AreEqual(2, fit.Count);
            Assert.AreEqual((10, 2, 52), (fit[0].start, fit[0].length, fit[0].pitch));
            Assert.AreEqual((12, 4, 88), (fit[1].start, fit[1].length, fit[1].pitch));
        }

        [Test]
        public void ClaudeAssist_WritesSuggestionWithUndo()
        {
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.cursorRow = 8;
                w.State.assistRows = 8;
                w.State.aiTarget = AiTarget.MelodyAndChords;
                w.State.aiPrompt = "サビっぽく";

                Assert.IsTrue(c.Request());
                Assert.IsTrue(c.Busy);
                StringAssert.Contains("サビっぽく", sent);
                Assert.IsFalse(c.Request(), "考え中は重ねて送らない");

                UnityEditor.Undo.IncrementCurrentGroup();
                pending(Reply("{\"comment\":\"跳躍で\",\"chords\":\"C G Am F\",\"notes\":[{\"start\":8,\"length\":2,\"pitch\":72},{\"start\":12,\"length\":4,\"pitch\":79}]}"), null);
                Assert.IsFalse(c.Busy);
                Assert.IsFalse(c.StatusIsError, c.Status);
                var p = song.patterns[0];
                Assert.AreEqual("C G Am F", p.chords);
                Assert.AreEqual(72, p.tracks[0].cells[8].note);
                Assert.AreEqual(79, p.tracks[0].cells[12].note);

                UnityEditor.Undo.PerformUndo();
                Assert.IsFalse(song.patterns[0].tracks[0].cells[8].HasNote, "Undo で消える");

                // 通信エラーと中止
                Assert.IsTrue(c.Request());
                pending(null, "通信に失敗しました");
                Assert.IsTrue(c.StatusIsError);
                Assert.IsTrue(c.Request());
                var late = pending;
                c.Cancel();
                Assert.IsFalse(c.Busy);
                late(Reply("{\"comment\":\"\",\"chords\":\"Dm\",\"notes\":[]}"), null);
                Assert.AreNotEqual("Dm", song.patterns[0].chords, "中止後の返答は無視");

                w.State.cursorCh = 3;
                w.State.aiTarget = AiTarget.Melody;
                Assert.IsFalse(c.Request(), "ノイズではメロディを頼めない");
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }
    }
}
