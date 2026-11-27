using System;
using System.Linq;
using System.Threading.Tasks;
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
            var sg = ClaudeReply.Parse(Reply("{\"comment\":\"上行で\",\"chords\":\"\",\"notes\":[{\"start\":8,\"length\":4,\"pitch\":72}]}"), out var err)[0];
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
            var sg = ClaudeReply.Parse(ok, out var err)[0];
            Assert.IsNull(err);
            Assert.AreEqual("明るく", sg.comment);
            Assert.AreEqual(64, sg.notes[0].pitch);

            // structured_output がなければ result の文字列を JSON として読む
            string text = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"{\\\"comment\\\":\\\"x\\\",\\\"chords\\\":\\\"Am\\\",\\\"notes\\\":[]}\"}";
            sg = ClaudeReply.Parse(text, out err)[0];
            Assert.IsNull(err);
            Assert.AreEqual("Am", sg.chords);

            Assert.IsNull(ClaudeReply.Parse("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"Not logged in\"}", out err));
            StringAssert.Contains("Not logged in", err);
            Assert.IsNull(ClaudeReply.Parse("{\"type\":\"result\",\"subtype\":\"error_max_turns\",\"is_error\":false}", out err));
            StringAssert.Contains("error_max_turns", err);
        }

        static string Cand(string comment, string chords, int start, int pitch) =>
            "{\"comment\":\"" + comment + "\",\"chords\":\"" + chords + "\",\"notes\":[{\"start\":" + start + ",\"length\":2,\"pitch\":" + pitch + "}]}";

        [Test]
        public void ParseResponse_ReadsCandidates()
        {
            var list = ClaudeReply.Parse(Reply("{\"candidates\":[" + Cand("a", "C", 8, 72) + "," + Cand("b", "", 9, 74) + "]}"), out var err);
            Assert.IsNull(err);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("b", list[1].comment);
            Assert.AreEqual(74, list[1].notes[0].pitch);
            StringAssert.Contains("\"candidates\"", ClaudePrompt.Schema);

            string cc = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"\",\"structured_output\":{\"candidates\":[" + Cand("x", "Am", 0, 64) + "]}}";
            list = ClaudeReply.Parse(cc, out err);
            Assert.IsNull(err);
            Assert.AreEqual("Am", list[0].chords);
        }

        [Test]
        public void ClaudeAssist_WritesDrumsAndBriefsBass()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                var p = song.patterns[0];
                p.tracks[3].cells[0] = Cell.NoteCell(3, Song.InstKick);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.cursorCh = 3;
                w.State.cursorRow = 0;
                w.State.assistRows = 16;
                w.State.aiTarget = AiTarget.Melody;
                Assert.AreEqual("ドラム", ClaudePrompt.TargetLabel(w.State.aiTarget, 3));
                Assert.IsTrue(c.Request());
                StringAssert.Contains("General MIDI drum key", sent);
                StringAssert.Contains("0 Kick", sent, "既存の打点は楽器名で伝える");
                pending(Reply("{\"candidates\":[{\"comment\":\"d\",\"chords\":\"\",\"notes\":[" +
                              "{\"start\":0,\"length\":1,\"pitch\":36},{\"start\":4,\"length\":1,\"pitch\":38},{\"start\":6,\"length\":1,\"pitch\":42}]}]}"), null);
                var cells = p.tracks[3].cells;
                Assert.AreEqual((3, Song.InstKick), (cells[0].note, cells[0].instrument));
                Assert.AreEqual((9, Song.InstSnare), (cells[4].note, cells[4].instrument));
                Assert.AreEqual((14, Song.InstHat), (cells[6].note, cells[6].instrument));
                Assert.AreNotEqual(Cell.Off, cells[1].note, "打点の間で音を切らない");

                w.State.cursorCh = 2;
                Assert.IsTrue(c.Request());
                StringAssert.Contains("bass line", sent);
                Assert.AreEqual("ベース＋コード", ClaudePrompt.TargetLabel(AiTarget.MelodyAndChords, 2));
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        static string Sec(string name, int source, int bars, string chords, int transpose = 0) =>
            $"{{\"name\":\"{name}\",\"source\":{source},\"bars\":{bars},\"chords\":\"{chords}\",\"transpose\":{transpose}}}";

        [Test]
        public void ClaudeStructure_BuildsSectionsAndOrder()
        {
            var song = Song.CreateDefault(16);
            try
            {
                song.patterns[0].chords = "C G";
                song.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60);
                var secs = new[]
                {
                    new ClaudeReply.Section { name = "Intro", source = -1, bars = 2 },
                    new ClaudeReply.Section { name = "A", source = 0, bars = 2, chords = "Am F" }, // 既存の名前 → そのまま使う
                    new ClaudeReply.Section { name = "サビ", source = 0, bars = 2, chords = "F G C C" },
                    new ClaudeReply.Section { name = "A" },
                    new ClaudeReply.Section { name = "サビ", transpose = 1 },
                };
                string order = ClaudeStructure.Build(song, secs, song.patterns.Count);
                Assert.AreEqual("Intro → A → サビ → A → サビ(+1)", order);
                Assert.AreEqual(3, song.patterns.Count);
                CollectionAssert.AreEqual(new[] { 1, 0, 2, 0, 2 }, song.order);
                CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 1 }, song.orderTranspose);
                Assert.AreEqual("C G", song.patterns[0].chords, "既存のパターンは変えない");
                var intro = song.patterns[1];
                Assert.AreEqual((32, "Intro", false), (intro.length, intro.name, intro.tracks[0].cells[0].HasNote));
                var sabi = song.patterns[2];
                Assert.AreEqual((32, "F G C C", 60), (sabi.length, sabi.chords, sabi.tracks[0].cells[0].note), "元パターンを複製して伸ばす");
                Assert.IsNull(ClaudeStructure.Build(song, new ClaudeReply.Section[0], 1));
            }
            finally
            {
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeStructure_ReusesBlankPlaceholderAsSlot()
        {
            var song = Song.CreateDefault(32);
            try
            {
                StringAssert.Contains("blank placeholder", ClaudeStructure.BuildUserMessage(song, ""));
                var secs = new[]
                {
                    new ClaudeReply.Section { name = "A", source = -1, bars = 8, chords = "C G Am F C G F C" },
                    new ClaudeReply.Section { name = "サビ", source = -1, bars = 4 },
                };
                ClaudeStructure.Build(song, secs, song.patterns.Count);
                CollectionAssert.AreEqual(new[] { 0, 1 }, song.order);
                Assert.AreEqual((128, "C G Am F C G F C"), (song.patterns[0].length, song.patterns[0].chords), "空の仮置きは依頼の長さに合わせる");
            }
            finally
            {
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeAssist_ProposesStructureAndRestoresIt()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.aiTarget = AiTarget.Structure;
                Assert.AreEqual("曲構成", ClaudePrompt.TargetLabel(AiTarget.Structure, 2));
                Assert.IsTrue(c.Request());
                StringAssert.Contains("## Existing patterns", sent);
                StringAssert.Contains("0: A, 16 rows", sent);
                pending(Reply("{\"candidates\":[" +
                              "{\"comment\":\"a\",\"sections\":[" + Sec("Intro", -1, 1, "") + "," + Sec("A", 0, 1, "") + "," + Sec("サビ", 0, 2, "F G") + "]}," +
                              "{\"comment\":\"b\",\"sections\":[" + Sec("A", 0, 1, "") + "," + Sec("A", 0, 1, "", 2) + "]}]}"), null);
                Assert.AreEqual(2, c.CandidateCount);
                CollectionAssert.AreEqual(new[] { 1, 0, 2 }, song.order);
                Assert.AreEqual(3, song.patterns.Count);

                Assert.IsTrue(c.Choose(1));
                Assert.AreEqual(1, song.patterns.Count, "前の案で足したパターンは外す");
                CollectionAssert.AreEqual(new[] { 0, 0 }, song.order);
                CollectionAssert.AreEqual(new[] { 0, 2 }, song.orderTranspose);

                Assert.IsTrue(c.Choose(-1));
                CollectionAssert.AreEqual(new[] { 0 }, song.order);
                CollectionAssert.AreEqual(new[] { 0 }, song.orderTranspose);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        static string Ch(int ch, float db, float pan, float lo, float duck) =>
            $"{{\"channel\":{ch},\"volumeDb\":{db},\"pan\":{pan},\"eqLow\":{lo},\"eqMid\":0,\"eqHigh\":-2,\"duck\":{duck},\"delaySend\":0.2,\"reverbSend\":0.3}}";

        [Test]
        public void ClaudeAssist_ProposesMixAndRestoresIt()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                song.mixer[1].mute = true;
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.aiTarget = AiTarget.Mix;
                Assert.AreEqual("ミックス", ClaudePrompt.TargetLabel(AiTarget.Mix, 0));
                Assert.IsTrue(c.Request());
                StringAssert.Contains("## Channels", sent);
                StringAssert.Contains("2 Triangle", sent);
                pending(Reply("{\"candidates\":[" +
                              "{\"comment\":\"a\",\"mix\":[" + Ch(0, -3, -0.5f, 0, 0.5f) + "," + Ch(2, -60, 0, 30, 0) + "],\"sidechainSource\":2,\"duckRelease\":0.8}," +
                              "{\"comment\":\"b\",\"mix\":[" + Ch(1, -6, 0.4f, -4, 0) + "],\"sidechainSource\":-1,\"duckRelease\":-1}]}"), null);
                Assert.AreEqual(2, c.CandidateCount);
                Assert.AreEqual((-3f, -0.5f, 0.5f), (song.mixer[0].volumeDb, song.mixer[0].pan, song.mixer[0].duck));
                Assert.AreEqual((ChannelMix.MinDb, ChannelMix.MaxEqDb), (song.mixer[2].volumeDb, song.mixer[2].eqLowDb), "範囲外は丸める");
                Assert.AreEqual((0.2f, 0.3f), (song.mixer[0].delaySend, song.mixer[0].reverbSend));
                Assert.AreEqual((2, 0.8f), (song.effects.sidechainSource, song.effects.duckRelease));
                Assert.IsTrue(song.mixer[1].mute, "ミュートはそのまま");

                Assert.IsTrue(c.Choose(1));
                Assert.AreEqual((0f, 0f), (song.mixer[0].volumeDb, song.mixer[0].duck), "前の案は外す");
                Assert.AreEqual((-6f, -4f), (song.mixer[1].volumeDb, song.mixer[1].eqLowDb));
                Assert.AreEqual(3, song.effects.sidechainSource);

                song.mixer[1].mute = false;
                Assert.IsTrue(c.Choose(-1));
                Assert.AreEqual((0f, 0f), (song.mixer[1].volumeDb, song.mixer[1].eqLowDb));
                Assert.IsFalse(song.mixer[1].mute, "切り替えの間に変えたミュートは残す");
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        static string Master(float lo, float width, float ratio, string limiter = "true") =>
            $"{{\"eqLow\":{lo},\"eqMid\":0,\"eqHigh\":-1.5,\"width\":{width},\"threshold\":-18,\"ratio\":{ratio},\"ceiling\":-1,\"limiter\":{limiter}}}";

        [Test]
        public void ClaudeAssist_ProposesMasterMatchedToTarget()
        {
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                for (int r = 0; r < 32; r += 4)
                {
                    song.patterns[0].tracks[0].cells[r] = Cell.NoteCell(60 + (r / 4) % 5, Song.InstBass);
                    song.patterns[0].tracks[2].cells[r] = Cell.NoteCell(40, Song.InstBass);
                }
                song.master.targetLufs = -16f;
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                c.RunBackground = a => { a(); return Task.CompletedTask; };
                w.State.aiTarget = AiTarget.Master;
                Assert.AreEqual("マスタリング", ClaudePrompt.TargetLabel(AiTarget.Master, 0));
                Assert.IsTrue(c.Request());
                Assert.IsNotNull(sent, c.Status);
                StringAssert.Contains("## Analysis of the mix", sent);
                StringAssert.Contains("Target loudness: -16 LUFS", sent);
                StringAssert.Contains("0 Pulse 1", sent);

                pending(Reply("{\"candidates\":[" +
                              "{\"comment\":\"a\",\"master\":" + Master(40, 1.2f, 2) + "}," +
                              "{\"comment\":\"b\",\"master\":" + Master(-2, 0.5f, 1, "false") + "}]}"), null);
                Assert.IsFalse(c.Busy);
                Assert.IsFalse(c.StatusIsError, c.Status);
                Assert.AreEqual(2, c.CandidateCount);
                var m = song.master;
                Assert.IsTrue(m.enabled && m.limiter);
                Assert.AreEqual((12f, 1.2f, 2f, -16f), (m.eqLowDb, m.width, m.compRatio, m.targetLufs), "範囲外は丸め、目標はそのまま");
                float lufs = MasterAnalysis.Measure(song, m).integratedLufs;
                Assert.IsTrue(Math.Abs(lufs + 16f) < 1f || m.gainDb >= 12f, $"ゲインを目標に合わせる：{lufs} LUFS / {m.gainDb} dB");

                Assert.IsTrue(c.Choose(1));
                Assert.AreEqual((-2f, 0.5f, false), (song.master.eqLowDb, song.master.width, song.master.limiter));
                Assert.IsTrue(c.Choose(-1));
                Assert.IsFalse(song.master.enabled, "依頼前に戻す");
                Assert.AreEqual(0f, song.master.eqLowDb);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeAssist_ChoosesBetweenCandidates()
        {
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                var p = song.patterns[0];
                p.tracks[0].cells[8] = Cell.NoteCell(60, 0);
                p.chords = "F";
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { pending = done; return () => { }; });
                w.State.cursorRow = 8;
                w.State.assistRows = 8;
                w.State.aiTarget = AiTarget.MelodyAndChords;
                Assert.IsTrue(c.Request());
                pending(Reply("{\"candidates\":[" + Cand("a", "C", 8, 72) + "," + Cand("b", "G", 10, 74) + "]}"), null);
                Assert.AreEqual(2, c.CandidateCount);
                Assert.AreEqual(0, c.Chosen);
                Assert.AreEqual(72, p.tracks[0].cells[8].note);
                Assert.AreEqual("C", p.chords);
                Assert.AreEqual(8, c.AuditionRow);

                Assert.IsTrue(c.Choose(1));
                Assert.IsFalse(p.tracks[0].cells[8].HasNote, "案1 の音は消える");
                Assert.AreEqual(74, p.tracks[0].cells[10].note);
                Assert.AreEqual("G", p.chords);
                StringAssert.StartsWith("案2/2", c.Status);

                Assert.IsTrue(c.Choose(-1));
                Assert.AreEqual(60, p.tracks[0].cells[8].note, "元の音に戻る");
                Assert.IsFalse(p.tracks[0].cells[10].HasNote);
                Assert.AreEqual("F", p.chords);
                Assert.IsFalse(c.Choose(5), "範囲外");

                w.State.cursorCh = 1;
                Assert.IsFalse(c.Choose(0), "チャンネルが変わったら切り替えない");
                Assert.IsTrue(c.StatusIsError);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
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
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        static string Note(int start, int length, int pitch) => $"{{\"start\":{start},\"length\":{length},\"pitch\":{pitch}}}";

        static string Cands(string chords, params string[] notes) =>
            "{\"candidates\":[{\"comment\":\"x\",\"chords\":\"" + chords + "\",\"notes\":[" + string.Join(",", notes) + "]}]}";

        [Test]
        public void ClaudeAssist_DraftsSongStepByStep()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                var sent = new System.Collections.Generic.List<string>();
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent.Add(msg); pending = done; return () => { }; });
                w.State.aiTarget = AiTarget.Draft;
                w.State.aiPrompt = "";
                Assert.IsFalse(c.Request(), "概要が空なら始めない");
                w.State.aiPrompt = "明るいフィールド曲";
                w.State.aiDraftAuto = false;
                c.SetChannelLock(3, true);
                Assert.IsTrue(c.Request());
                Assert.IsTrue(c.Drafting);
                StringAssert.Contains("## Existing patterns", sent[0]);
                pending(Reply("{\"candidates\":[{\"comment\":\"a\",\"sections\":[" + Sec("Intro", -1, 1, "") + "," + Sec("A", 0, 1, "") + "]}]}"), null);
                CollectionAssert.AreEqual(new[] { 1, 0 }, song.order);
                Assert.AreEqual(1, sent.Count, "選んで進めるモードでは次へを待つ");

                Assert.IsTrue(c.DraftNext());
                w.State.aiDraftAuto = true;
                // おまかせ：残りは書き込むたびに自動で次を頼む
                for (int guard = 0; guard < 20 && pending != null; guard++)
                {
                    var p = pending;
                    pending = null;
                    p(Reply(Cands("C", Note(0, 4, 64))), null);
                }
                Assert.IsFalse(c.Drafting);
                StringAssert.Contains("下書きができました", c.Status);
                Assert.AreEqual(1 + 6, sent.Count, "2 セクション × 3 チャンネル（ノイズは固定）");
                StringAssert.Contains("Write a chord progression", sent[1], "コードの無い Intro はメロディ＋コード");
                StringAssert.Contains("明るいフィールド曲", sent[1]);
                StringAssert.Contains("section \"Intro\"", sent[1]);
                StringAssert.Contains("Earlier lead melody (section \"Intro\")", sent[4], "A のメロディは Intro のモチーフを引き継ぐ");
                Assert.AreEqual("C", song.patterns[1].chords);
                for (int ch = 0; ch < 3; ch++)
                {
                    Assert.IsTrue(song.patterns[1].tracks[ch].cells[0].HasNote, $"Intro ch{ch}");
                    Assert.IsTrue(song.patterns[0].tracks[ch].cells[0].HasNote, $"A ch{ch}");
                }
                Assert.IsFalse(song.patterns[1].tracks[3].cells.Any(x => x.HasNote), "固定したノイズは書かない");
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeAssist_KeepsLockedBarsAndRefines()
        {
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                var p = song.patterns[0];
                p.tracks[0].cells[20] = Cell.NoteCell(60, 0);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.cursorRow = 0;
                w.State.assistRows = 32;
                w.State.aiTarget = AiTarget.Melody;
                c.SetLockBars("2");
                Assert.IsTrue(c.Request());
                StringAssert.Contains("bar 2 = rows 16-31", sent);
                pending(Reply(Cands("", Note(0, 2, 72), Note(14, 8, 74), Note(20, 2, 76))), null);
                var notes = w.Roll.Notes();
                Assert.AreEqual(72, p.tracks[0].cells[0].note);
                Assert.AreEqual(2, notes.First(n => n.start == 14).length, "固定小節の手前で切る");
                Assert.AreEqual(60, p.tracks[0].cells[20].note, "固定小節の音は残す");

                Assert.IsTrue(c.CanRefine);
                w.State.aiPrompt = "";
                Assert.IsFalse(c.Refine(), "直す内容が空");
                w.State.aiPrompt = "もっと高く";
                Assert.IsTrue(c.Refine());
                StringAssert.Contains("## Revision", sent);
                StringAssert.Contains("もっと高く", sent);
                StringAssert.Contains("Locked bars", sent);
                pending(Reply(Cands("", Note(0, 2, 79))), null);
                Assert.AreEqual(79, p.tracks[0].cells[0].note);
                Assert.IsFalse(p.tracks[0].cells[14].HasNote);

                Assert.IsTrue(c.Choose(-1));
                Assert.IsFalse(p.tracks[0].cells[0].HasNote, "元は最初の依頼の前");
                Assert.AreEqual(60, p.tracks[0].cells[20].note);

                c.SetChannelLock(0, true);
                w.State.aiPrompt = "";
                Assert.IsFalse(c.Request(), "固定したチャンネルには頼まない");
                Assert.IsTrue(c.StatusIsError);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeAssist_ProposesInstrumentAndRestoresIt()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                int idx = w.Session.ChannelInstrument(0);
                string oldName = song.instruments[idx].name;
                w.State.aiTarget = AiTarget.Instrument;
                w.State.aiPrompt = "柔らかく";
                Assert.IsTrue(c.Request());
                StringAssert.Contains("## Current instrument", sent);
                StringAssert.Contains("ticks per second", sent);
                pending(Reply("{\"candidates\":[" +
                              "{\"comment\":\"a\",\"instrument\":{\"name\":\"Soft Lead\",\"volume\":\"15 99 | 10 / 6 0\",\"arpeggio\":\"\",\"duty\":\"2\",\"pitch\":\"\"}}," +
                              "{\"comment\":\"b\",\"instrument\":{\"name\":\"\",\"volume\":\"8\",\"arpeggio\":\"0 12\",\"duty\":\"9\",\"pitch\":\"\"}}]}"), null);
                var inst = song.instruments[idx];
                Assert.AreEqual("Soft Lead", inst.name);
                CollectionAssert.AreEqual(new[] { 15, 15, 10, 6, 0 }, inst.volume.values, "範囲に収める");
                Assert.AreEqual(2, inst.volume.loop);
                Assert.AreEqual(3, inst.volume.release);

                Assert.IsTrue(c.Choose(1));
                Assert.AreEqual(oldName, song.instruments[idx].name, "名前が空なら元の名前");
                CollectionAssert.AreEqual(new[] { 3 }, song.instruments[idx].duty.values);

                Assert.IsTrue(c.CanRefine);
                Assert.IsTrue(c.Choose(-1));
                Assert.AreEqual(oldName, song.instruments[idx].name);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void ClaudeAssist_CritiqueRunsNextAction()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                string sent = null;
                Action<string, string> pending = null;
                var c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) => { sent = msg; pending = done; return () => { }; });
                w.State.aiTarget = AiTarget.Critique;
                Assert.IsTrue(c.Request());
                StringAssert.Contains("## Patterns in the order", sent);
                pending(Reply("{\"candidates\":[{\"comment\":\"素朴\",\"points\":[\"良い: x\",\"改善: y\"],\"next\":[" +
                              "{\"label\":\"ハモり\",\"target\":\"melody\",\"channel\":1,\"pattern\":0,\"request\":\"3度上でハモって\"}," +
                              "{\"label\":\"?\",\"target\":\"zzz\",\"channel\":0,\"pattern\":-1,\"request\":\"\"}]}]}"), null);
                Assert.IsNotNull(c.Critique);
                Assert.AreEqual(2, c.Critique.points.Length);
                Assert.AreEqual(1, c.Critique.next.Length, "知らない種類は外す");
                Assert.AreEqual(0, c.CandidateCount, "講評は曲を書き換えない");

                Assert.IsTrue(c.RunNext(0));
                Assert.AreEqual(1, w.State.cursorCh);
                Assert.AreEqual(AiTarget.Melody, w.State.aiTarget);
                StringAssert.Contains("3度上でハモって", sent);
                StringAssert.Contains("Write notes for ch1", sent);
                pending(Reply(Cands("", Note(0, 4, 67))), null);
                Assert.AreEqual(67, song.patterns[0].tracks[1].cells[0].note);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }
    }
}
