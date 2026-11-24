using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniDAW.Editor;
using UniDAW.Midi;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>シーケンサー×トラッカーのいいとこどり機能（Gxx / Qxy / Rxy / 順序表トランスポーズ / ピアノロール / ヘルプ）。</summary>
    public class HybridFeatureTests
    {
        const int Rate = 48000;
        const int TickSamples = 800; // 150bpm, rpb4, speed6

        static Song MakeSong(int length = 16)
        {
            var s = Song.CreateDefault(length);
            s.bpm = 150;
            s.rowsPerBeat = 4;
            s.speed = 6;
            return s;
        }

        static void Render(ChipEngine e, int frames) => e.Render(new float[frames], 0, frames, 1);

        static List<NoteEvent> Drain(ChipEngine e)
        {
            var l = new List<NoteEvent>();
            while (e.NoteEvents.TryDequeue(out var n)) l.Add(n);
            return l;
        }

        static Cell NoteWithFx(int note, FxType t, int param, int inst = Song.InstBass)
        {
            var c = Cell.NoteCell(note, inst);
            c.fx1 = new Fx(t, param);
            return c;
        }

        [Test]
        public void FxText_NewEffectsRoundTrip()
        {
            foreach (var s in new[] { "G03", "Q27", "R15" })
            {
                Assert.IsTrue(FxText.TryParse(s, out var fx), s);
                Assert.AreEqual(s, FxText.Format(fx));
            }
            Assert.AreEqual(FxType.NoteDelay, FxText.TypeFromChar('g'));
            StringAssert.Contains("Gxx", FxText.Help(FxType.NoteDelay));
        }

        [Test]
        public void NoteDelay_TriggersAtTick()
        {
            var s = MakeSong(4);
            s.patterns[0].tracks[0].cells[0] = NoteWithFx(60, FxType.NoteDelay, 3);
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, TickSamples * 2 + 400); // tick 0..2
            Assert.AreEqual(0, Drain(e).Count, "tick 3 まで鳴らない");
            Assert.IsFalse(e.IsChannelActive(0));
            Render(e, TickSamples);           // tick 3
            var ev = Drain(e);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(60, ev[0].note);
            Assert.IsTrue(e.IsChannelActive(0));
            Object.DestroyImmediate(s);
        }

        [Test]
        public void NoteDelay_AtOrBeyondSpeed_IsSkipped()
        {
            var s = MakeSong(4);
            s.patterns[0].tracks[0].cells[0] = NoteWithFx(60, FxType.NoteDelay, 6);
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, TickSamples * 6 * 2);
            Assert.AreEqual(0, Drain(e).Count(n => n.channel == 0));
            Object.DestroyImmediate(s);
        }

        [Test]
        public void NoteSlide_ReachesTarget()
        {
            var s = MakeSong(4);
            s.patterns[0].tracks[0].cells[0] = NoteWithFx(60, FxType.NoteSlideUp, 0xF7);
            s.patterns[0].tracks[1].cells[0] = NoteWithFx(60, FxType.NoteSlideDown, 0xF5);
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, TickSamples * 5 + 100);
            Assert.AreEqual(67f, e.GetChannelPitch(0), 0.01f, "Q: 7 半音上で止まる");
            Assert.AreEqual(55f, e.GetChannelPitch(1), 0.01f, "R: 5 半音下で止まる");

            var s2 = MakeSong(4);
            s2.patterns[0].tracks[0].cells[0] = NoteWithFx(60, FxType.NoteSlideUp, 0x07);
            var e2 = new ChipEngine(Rate) { Song = s2 };
            e2.PlayPattern(0);
            Render(e2, TickSamples * 3 + 100);
            float mid = e2.GetChannelPitch(0);
            Assert.Greater(mid, 60f, "速度 0 でも徐々に上がる");
            Assert.Less(mid, 67f);
            Object.DestroyImmediate(s);
            Object.DestroyImmediate(s2);
        }

        [Test]
        public void OrderTranspose_ShiftsTonalChannelsOnly()
        {
            var s = MakeSong(4);
            s.order = new List<int> { 0, 0 };
            s.orderTranspose = new List<int> { 0, 12 };
            s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstLead);
            s.patterns[0].tracks[3].cells[0] = Cell.NoteCell(5, Song.InstHat);
            var e = new ChipEngine(Rate) { Song = s };
            e.Play();
            Render(e, TickSamples * 6 * 8);
            var ev = Drain(e);
            CollectionAssert.AreEqual(new[] { 60, 72 }, ev.Where(n => n.channel == 0).Select(n => n.note).ToArray());
            CollectionAssert.AreEqual(new[] { 5, 5 }, ev.Where(n => n.channel == 3).Select(n => n.note).ToArray(), "ノイズは移調しない");

            // パターンループ再生（エディタのパターン試聴）は移調なし
            e.PlayPattern(0);
            Render(e, TickSamples * 6);
            Assert.AreEqual(60, Drain(e).First(n => n.channel == 0).note);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void OrderTranspose_StaysInSyncWithOrder()
        {
            var s = MakeSong(4);
            s.patterns.Add(new Pattern(4, "B"));
            s.InsertOrder(1, 1, 7);
            s.InsertOrder(2, 0, -3);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, s.order);
            CollectionAssert.AreEqual(new[] { 0, 7, -3 }, s.orderTranspose);
            s.SwapOrder(0, 1);
            CollectionAssert.AreEqual(new[] { 7, 0, -3 }, s.orderTranspose);
            s.RemoveOrder(0);
            CollectionAssert.AreEqual(new[] { 0, -3 }, s.orderTranspose);

            // 旧データ（orderTranspose なし）は Validate で 0 埋め
            s.order = new List<int> { 0, 1, 1 };
            s.orderTranspose = null;
            s.Validate();
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, s.orderTranspose);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void SongToMidi_AppliesTransposeAndDelay()
        {
            var s = MakeSong(4);
            s.order = new List<int> { 0, 0 };
            s.orderTranspose = new List<int> { 0, 5 };
            s.patterns[0].tracks[0].cells[0] = NoteWithFx(60, FxType.NoteDelay, 3, Song.InstLead);
            var data = SmfReader.Read(SongToMidi.Export(s));
            var ns = data.notes.Where(n => n.channel == 0).OrderBy(n => n.startTick).ToList();
            Assert.AreEqual(2, ns.Count);
            Assert.AreEqual(60, ns[0].note);
            Assert.AreEqual(65, ns[1].note);
            int ticksPerRow = data.division / 4;
            Assert.AreEqual(ticksPerRow / 2, ns[0].startTick, "G03 は speed 6 の半分だけ遅れる");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Window_FxKeysAndHelp()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                Assert.IsNull(w.FxHelpAtCursor(), "ノート列ではヘルプなし");
                w.SetCursor(0, 0, 3);
                StringAssert.Contains("0 1 2 3 4 A V F S G Q R", w.FxHelpAtCursor(), "空のエフェクト列では一覧");
                Assert.IsTrue(w.HandleKey(KeyCode.G, false, false));
                Assert.AreEqual(FxType.NoteDelay, song.patterns[0].tracks[0].cells[0].fx1.type);
                w.SetCursor(0, 0, 3);
                StringAssert.Contains("Gxx", w.FxHelpAtCursor());
                w.SetCursor(0, 0, 5);
                Assert.IsTrue(w.HandleKey(KeyCode.Q, false, false));
                Assert.AreEqual(FxType.NoteSlideUp, song.patterns[0].tracks[0].cells[0].fx2.type);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }
    }
}
