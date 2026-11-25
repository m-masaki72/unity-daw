using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>制作まわり：ステム書き出しなど。</summary>
    public class ProductionTests
    {
        const int Rate = 48000;

        static float Peak(float[] a)
        {
            float m = 0;
            foreach (var x in a) m = Mathf.Max(m, Mathf.Abs(x));
            return m;
        }

        [Test]
        public void Stems_RenderOneChannelEach()
        {
            var s = Song.CreateDefault(16);
            try
            {
                s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstBass);
                s.patterns[0].tracks[2].cells[0] = Cell.NoteCell(48, Song.InstBass);
                s.mixer[2].solo = true; // 曲のソロがあってもステムは各チャンネルを鳴らす
                var ch0 = OfflineRenderer.RenderChannelStereo(s, Rate, 0, 2);
                var ch1 = OfflineRenderer.RenderChannelStereo(s, Rate, 1, 2);
                var ch2 = OfflineRenderer.RenderChannelStereo(s, Rate, 2, 2);
                Assert.Greater(Peak(ch0), 0.01f, "Pulse 1 が鳴る");
                Assert.Less(Peak(ch1), 1e-4f, "空のチャンネルは無音");
                Assert.Greater(Peak(ch2), 0.01f, "Triangle が鳴る");
                Assert.AreEqual(ch0.Length, ch2.Length, "同じ長さ");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        static ChipEngine Engine(Song s) => new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };

        /// <summary>行を 1 つずつ進める（speed tick ぶんのサンプル）。</summary>
        static void Rows(ChipEngine e, Song s, int rows)
        {
            int frames = (int)System.Math.Ceiling(Rate * 60.0 / (s.bpm * s.rowsPerBeat)) * rows;
            var buf = new float[frames * 2];
            e.Render(buf, 0, frames, 2);
        }

        [Test]
        public void CellLane_ReadsAndWritesTheSameColumnsAsTheTracker()
        {
            var c = Cell.NoteCell(60);
            Assert.AreEqual(-1, CellLane.Get(c, LaneKind.Volume, FxType.None));
            CellLane.Set(ref c, LaneKind.Volume, FxType.None, 20);
            Assert.AreEqual(15, c.volume);

            c.fx1 = new Fx(FxType.Vibrato, 0x24);
            CellLane.Set(ref c, LaneKind.Effect, FxType.Duty, 9);
            Assert.AreEqual((FxType.Vibrato, FxType.Duty, 3), (c.fx1.type, c.fx2.type, (int)c.fx2.param), "空いている列へ、上限で丸める");
            Assert.AreEqual(3, CellLane.Get(c, LaneKind.Effect, FxType.Duty));
            CellLane.Set(ref c, LaneKind.Effect, FxType.Vibrato, 0x46);
            Assert.AreEqual(0x46, c.fx1.param, "同じ種類の列を上書き");
            CellLane.Set(ref c, LaneKind.Effect, FxType.Duty, -1);
            Assert.IsTrue(c.fx2.IsEmpty);
            CellLane.Set(ref c, LaneKind.Effect, FxType.NoteCut, -1);
            Assert.AreEqual(FxType.Vibrato, c.fx1.type, "無い種類を消しても他は残る");
        }

        [Test]
        public void InstrumentPresets_SaveLoadAndDelete()
        {
            string old = InstrumentPresets.FilePath;
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unidaw-preset-test-" + System.Guid.NewGuid().ToString("N"));
            InstrumentPresets.FilePath = System.IO.Path.Combine(dir, "p.json");
            try
            {
                int builtIn = InstrumentLibrary.BuiltIn.Count;
                Assert.Greater(builtIn, 10);
                Assert.AreEqual(builtIn, InstrumentPresets.All().Count);

                var inst = new Instrument("Mine", "15 10 | 8", "| 0 12", "1", "1 -1");
                InstrumentPresets.Save(inst);
                inst.volume = Sequence.Parse("3");
                InstrumentPresets.Save(inst); // 同じ名前は上書き
                var user = InstrumentPresets.LoadUser();
                Assert.AreEqual(1, user.Count);
                Assert.IsTrue(user[0].user);
                Assert.AreEqual("3", user[0].instrument.volume.ToString());
                Assert.AreEqual("| 0 12", user[0].instrument.arpeggio.ToString());
                Assert.AreEqual(builtIn + 1, InstrumentPresets.All().Count);

                InstrumentPresets.Delete("Mine");
                Assert.AreEqual(0, InstrumentPresets.LoadUser().Count);
            }
            finally
            {
                InstrumentPresets.FilePath = old;
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }

        [Test]
        public void PreviewSlot_PlaysAnInstrumentOutsideTheSong()
        {
            var s = Song.CreateDefault(16);
            try
            {
                s.instruments.Clear(); // 曲には楽器が無い
                var e = Engine(s);
                e.PreviewInstrument = new Instrument("P", "15", "", "2");
                e.NoteOn(0, 60, 15, ChipEngine.PreviewSlot);
                var buf = new float[4800 * 2];
                e.Render(buf, 0, 4800, 2);
                float peak = 0;
                foreach (var v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.Greater(peak, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void BeatEvents_CountBeatsBarsAndSections()
        {
            var s = Song.CreateDefault(16); // 4 拍 = 1 小節
            try
            {
                s.rowsPerBeat = 4;
                s.order.Add(0);
                var e = Engine(s);
                e.Play();
                Rows(e, s, 33);
                var list = new System.Collections.Generic.List<BeatEvent>();
                while (e.BeatEvents.TryDequeue(out var b)) list.Add(b);
                Assert.GreaterOrEqual(list.Count, 8);
                Assert.AreEqual((0, 0, 0, true), (list[0].beat, list[0].bar, list[0].beatInBar, list[0].patternStart));
                Assert.AreEqual((1, 0, 1, false), (list[1].beat, list[1].bar, list[1].beatInBar, list[1].patternStart));
                Assert.AreEqual((4, 1, 0, true, 1), (list[4].beat, list[4].bar, list[4].beatInBar, list[4].patternStart, list[4].order));
                Assert.IsTrue(list[4].BarStart);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void Queue_JumpsAtNextBarOrPatternEnd()
        {
            var s = Song.CreateDefault(32); // 2 小節のパターン
            try
            {
                s.rowsPerBeat = 4;
                s.patterns.Add(s.patterns[0].Clone());
                s.order.Clear();
                s.order.Add(0);
                s.order.Add(0);
                s.order.Add(1);
                var e = Engine(s);
                e.Play();
                Rows(e, s, 3);
                e.Queue(2);
                Rows(e, s, 15); // 行 16（次の小節の頭）を越える
                Assert.AreEqual(2, e.PlayingOrder, "次の小節の頭で移る");
                Assert.AreEqual(-1, e.Queued);
                Assert.Less(e.PlayingRow, 4, "移った先は頭から");

                e.Play();
                Rows(e, s, 3);
                e.Queue(2, atNextBar: false);
                Rows(e, s, 15);
                Assert.AreEqual(0, e.PlayingOrder, "パターンの終わりまでは移らない");
                Rows(e, s, 16);
                Assert.AreEqual(2, e.PlayingOrder);

                e.Play();
                Rows(e, s, 2);
                e.Queue(2);
                e.Queue(-1);
                Rows(e, s, 32);
                Assert.AreEqual(1, e.PlayingOrder, "取り消せば順序表どおり");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
