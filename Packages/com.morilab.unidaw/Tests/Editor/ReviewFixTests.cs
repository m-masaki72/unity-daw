using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniDAW.Composer;
using UniDAW.Editor;
using UniDAW.Midi;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>コードレビュー指摘の回帰テスト。</summary>
    public class ReviewFixTests
    {
        const int Rate = 48000;

        static Song MakeSong(int length = 16)
        {
            var s = Song.CreateDefault(length);
            s.bpm = 150;
            s.rowsPerBeat = 4;
            s.speed = 6;
            return s;
        }

        static float[] Render(ChipEngine e, int frames)
        {
            var buf = new float[frames];
            e.Render(buf, 0, frames, 1);
            return buf;
        }

        static List<NoteEvent> Drain(ChipEngine e)
        {
            var l = new List<NoteEvent>();
            while (e.NoteEvents.TryDequeue(out var n)) l.Add(n);
            return l;
        }

        [Test]
        public void StyleSheet_UsesBundledFonts()
        {
            const string dir = "Packages/com.morilab.unidaw/";
            var deps = UnityEditor.AssetDatabase.GetDependencies(dir + "Composer/UI/UniDAW.uss", false);
            foreach (var f in new[] { "Fonts/BIZUDPGothic-Regular.ttf", "Fonts/JetBrainsMono-Regular.ttf" })
            {
                CollectionAssert.Contains(deps, dir + f, "USS の url が同梱フォントを指す");
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(dir + f));
            }
        }

        [Test]
        public void SnapshotUndo_StoresDeltas_AndRestoresExactly()
        {
            var song = MakeSong(64);
            double t = 0;
            var h = new SnapshotUndo(() => t += 1);
            var states = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                states.Add(JsonUtility.ToJson(song));
                h.Record(song, "edit");
                song.patterns[0].tracks[i % 4].cells[i].note = 40 + i;
            }
            var last = JsonUtility.ToJson(song);
            Assert.Less(h.StoredChars, last.Length * 3L, "古い段は差分だけ持つ");
            for (int i = states.Count - 1; i >= 0; i--)
            {
                Assert.IsTrue(h.Undo());
                Assert.AreEqual(states[i], JsonUtility.ToJson(song), $"{i} 段目に戻る");
            }
            Assert.IsFalse(h.Undo());
            for (int i = 1; i < states.Count; i++)
            {
                Assert.IsTrue(h.Redo());
                Assert.AreEqual(states[i], JsonUtility.ToJson(song));
            }
            Assert.IsTrue(h.Redo());
            Assert.AreEqual(last, JsonUtility.ToJson(song));
            Assert.IsFalse(h.Redo());

            // 途中まで戻して別の編集をすると Redo は消え、上限を超えた古い段は捨てる
            h.Undo();
            h.Record(song, "other");
            Assert.IsFalse(h.CanRedo);
            for (int i = 0; i < SnapshotUndo.MaxUndo + 10; i++)
            {
                h.Record(song, "bpm");
                song.bpm = 100 + i;
            }
            int n = 0;
            while (h.Undo()) n++;
            Assert.AreEqual(SnapshotUndo.MaxUndo, n);
        }

        [Test]
        public void Render_ExceptionBecomesSilence_AndIsReportedOnce()
        {
            var e = new ChipEngine(Rate) { Song = MakeSong() };
            e.Play();
            var data = new float[64];
            for (int i = 0; i < data.Length; i++) data[i] = 1f;
            Assert.DoesNotThrow(() => e.Render(data, 0, 4096, 2), "オーディオスレッドに例外を投げない");
            Assert.IsNotNull(e.LastError);
            Assert.IsTrue(data.All(v => v == 0f), "落ちたブロックは無音");
            var first = e.LastError;
            e.Render(new float[64], 0, 4096, 2);
            Assert.AreSame(first, e.LastError, "取り出すまでは最初の例外を残す");
            e.LastError = null;
            Assert.DoesNotThrow(() => e.Render(new float[512], 0, 256, 2), "次のブロックは普通に鳴る");
            Assert.IsNull(e.LastError);
        }

        [Test]
        public void PatternRemovedWhilePlaying_StopsLikeStop()
        {
            var song = MakeSong();
            var e = new ChipEngine(Rate) { Song = song };
            e.PlayPattern(0);
            var buf = new float[1024];
            e.Render(buf, 0, 512, 2);
            e.Queue(0);
            e.Render(buf, 0, 16, 2);
            Assert.AreEqual(0, e.Queued);
            song.patterns.Clear();
            for (int i = 0; i < 20; i++) e.Render(buf, 0, 512, 2);
            Assert.IsFalse(e.IsPlaying);
            Assert.AreEqual(-1, e.Queued, "予約も消す");
            Assert.AreEqual(-1, e.PlayingPattern);
            Assert.IsNull(e.LastError);
        }

        [Test]
        public void ClaudeCodeFind_UsesNodeForNpmShim()
        {
            if (System.IO.Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows だけ");
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UniDAW-findtest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cliDir = System.IO.Path.Combine(dir, "node_modules", "@anthropic-ai", "claude-code");
                System.IO.Directory.CreateDirectory(cliDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "claude.cmd"), "@echo off");
                var none = ClaudeCodeClient.Find(dir, out var noScript);
                Assert.IsFalse(none != null && none.StartsWith(dir), "cli.js と node が無ければ使わない");
                System.IO.File.WriteAllText(System.IO.Path.Combine(cliDir, "cli.js"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "node.exe"), "");
                var exe = ClaudeCodeClient.Find(dir, out var script);
                Assert.AreEqual(System.IO.Path.Combine(dir, "node.exe"), exe, "cmd.exe を通さず node で起動");
                Assert.AreEqual(System.IO.Path.Combine(cliDir, "cli.js"), script);
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        [Test]
        public void MidiBridge_NoteOffGoesToChannelThatSounded()
        {
            var prevTarget = UniDAWMidiBridge.TargetChannel;
            var prevEngine = UniDAWMidiBridge.Engine;
            try
            {
                UniDAWMidiBridge.Engine = null;
                while (UniDAWMidiBridge.Pending.TryDequeue(out _)) { }
                UniDAWMidiBridge.TargetChannel = 0;
                UniDAWMidiBridge.OnMidi(new MidiMessage { status = 0x90, data1 = 60, data2 = 100 });
                UniDAWMidiBridge.TargetChannel = 1; // 押したままチャンネル移動
                UniDAWMidiBridge.OnMidi(new MidiMessage { status = 0x80, data1 = 60, data2 = 0 });
                var list = new List<LiveNote>();
                while (UniDAWMidiBridge.Pending.TryDequeue(out var n)) list.Add(n);
                Assert.AreEqual(2, list.Count);
                Assert.IsFalse(list[1].on);
                Assert.AreEqual(0, list[1].channel, "ノートオフは発音したチャンネルへ");
            }
            finally
            {
                UniDAWMidiBridge.TargetChannel = prevTarget;
                UniDAWMidiBridge.Engine = prevEngine;
            }
        }

        [Test]
        public void Window_EditOnGhostRow_WritesWrappedCell()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                w.SetTrackLength(0, 4);
                w.SetCursor(8, 0, 0);
                Assert.IsTrue(w.HandleKey(KeyCode.Z, false, false));
                var cells = song.patterns[0].tracks[0].cells;
                Assert.AreEqual(60, cells[0].note, "行 8 はトラック長 4 で折り返してセル 0");
                Assert.IsFalse(cells[8].HasNote);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(w);
                UnityEngine.Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void Portamento_AfterRelease_RetriggersNote()
        {
            var s = MakeSong(8);
            s.instruments[Song.InstBass] = new Instrument("Rel", "15 / 8 4 0");
            var t = s.patterns[0].tracks[0].cells;
            t[0] = Cell.NoteCell(60, Song.InstBass);
            t[4] = Cell.NoteCell(Cell.Off, -1);
            var c = Cell.NoteCell(64, Song.InstBass);
            c.fx1 = new Fx(FxType.Portamento, 0x10);
            t[5] = c;
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, 4800 * 6);
            var row6 = Render(e, 4800);
            Assert.Greater(row6.Max(Math.Abs), 0.01f, "リリース後の 3xx ノートが鳴る");
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Sequence_LoopsAfterRelease()
        {
            var arp = Sequence.Parse("| 0 4 7");
            int pos = 0;
            var got = Enumerable.Range(0, 6).Select(_ => arp.Step(ref pos, true)).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 4, 7, 0, 4, 7 }, got, "リリース指定なしのループはリリース後もループ");

            var vol = Sequence.Parse("15 / 10 | 5 0");
            pos = vol.release;
            got = Enumerable.Range(0, 5).Select(_ => vol.Step(ref pos, true)).ToArray();
            CollectionAssert.AreEqual(new[] { 10, 5, 0, 5, 0 }, got, "リリース部のループ");

            var hold = Sequence.Parse("| 15 12 / 6 3");
            pos = hold.release;
            got = Enumerable.Range(0, 4).Select(_ => hold.Step(ref pos, true)).ToArray();
            CollectionAssert.AreEqual(new[] { 6, 3, 3, 3 }, got, "サステイン部のループはリリース後に保持");
        }

        [Test]
        public void SongMode_ConditionAdvancesPerOccurrence()
        {
            var s = MakeSong(4);
            s.order = new List<int> { 0, 0, 0, 0 };
            var c = Cell.NoteCell(60, Song.InstLead);
            c.condA = 2;
            c.condB = 2;
            s.patterns[0].tracks[0].cells[0] = c;
            var e = new ChipEngine(Rate) { Song = s };
            e.Play();
            Render(e, 4800 * 4 * 4);
            Assert.AreEqual(2, Drain(e).Count(n => n.channel == 0), "順序表 4 回のうち 2, 4 回目で鳴る");
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void SongMode_ProbabilityVariesPerOccurrence()
        {
            var s = MakeSong(16);
            s.order = new List<int> { 0, 0 };
            for (int r = 0; r < 16; r++)
            {
                var c = Cell.NoteCell(60, Song.InstLead);
                c.probability = 50;
                s.patterns[0].tracks[0].cells[r] = c;
            }
            var e = new ChipEngine(Rate) { Song = s };
            e.Play();
            Render(e, 4800 * 16 * 2);
            var ev = Drain(e).Where(n => n.channel == 0).ToList();
            // 1 回目と 2 回目の出現で鳴った行の並びが異なる（行番号が戻った所が 2 回目の始まり）
            int split = -1;
            for (int i = 1; i < ev.Count && split < 0; i++)
                if (ev[i].row <= ev[i - 1].row) split = i;
            Assert.Greater(split, 0);
            var a = ev.Take(split).Select(n => n.row).ToArray();
            var b = ev.Skip(split).Select(n => n.row).ToArray();
            CollectionAssert.AreNotEqual(a, b);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void SongToMidi_NonDivisibleRowsPerBeat_KeepsTempo()
        {
            var s = MakeSong(16);
            s.rowsPerBeat = 7;
            s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstLead);
            s.patterns[0].tracks[0].cells[7] = Cell.NoteCell(62, Song.InstLead);
            var data = SmfReader.Read(SongToMidi.Export(s));
            var ns = data.notes.Where(n => n.channel == 0).OrderBy(n => n.startTick).ToList();
            Assert.AreEqual(0, data.division % 7);
            Assert.AreEqual(data.division, ns[1].startTick, "7 行 = ちょうど 1 拍");
            UnityEngine.Object.DestroyImmediate(s);
        }
    }
}
