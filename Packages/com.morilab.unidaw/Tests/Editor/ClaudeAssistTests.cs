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
            "{\"type\":\"message\",\"stop_reason\":\"" + stop + "\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":" + ClaudePrompt.Str(innerJson) + "}]}";

        [Test]
        public void Str_EscapesJson()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\nd\\u0001\"", ClaudePrompt.Str("a\"b\\c\nd\u0001"));
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

                string body = ClaudePrompt.BuildRequestBody("claude-opus-5-5", msg);
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
            var sg = ClaudePrompt.ParseResponse(Reply("{\"comment\":\"上行で\",\"chords\":\"\",\"notes\":[{\"start\":8,\"length\":4,\"pitch\":72}]}"), out var err);
            Assert.IsNull(err);
            Assert.AreEqual("上行で", sg.comment);
            Assert.AreEqual(1, sg.notes.Length);
            Assert.AreEqual(72, sg.notes[0].pitch);

            Assert.IsNull(ClaudePrompt.ParseResponse("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", out err));
            StringAssert.Contains("authentication_error", err);
            Assert.IsNull(ClaudePrompt.ParseResponse(Reply("{}", "refusal"), out err));
            Assert.IsNotNull(err);
            Assert.IsNull(ClaudePrompt.ParseResponse(Reply("{}", "max_tokens"), out err));
            Assert.IsNull(ClaudePrompt.ParseResponse("not json", out err));
            Assert.IsNotNull(err);
        }

        [Test]
        public void FitNotes_ClampsRangePitchAndOverlap()
        {
            var raw = new[]
            {
                new ClaudePrompt.SuggestedNote { start = 4, length = 2, pitch = 60 },   // 範囲外
                new ClaudePrompt.SuggestedNote { start = 10, length = 8, pitch = 40 },  // 音域外 → 52、次で切る
                new ClaudePrompt.SuggestedNote { start = 12, length = 20, pitch = 100 }, // 音域外 → 88、範囲末で切る
                new ClaudePrompt.SuggestedNote { start = 12, length = 1, pitch = 70 },  // 同じ開始は捨てる
            };
            var fit = ClaudePrompt.FitNotes(raw, 8, 16, 48, 96);
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
                var c = new ClaudeAssist(w.Session, w.Roll, (body, done) => { sent = body; pending = done; return () => { }; });
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
