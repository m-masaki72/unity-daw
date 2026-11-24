using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
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
