using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>FamiTracker テキストの取り込み。</summary>
    public class FamiTrackerImportTests
    {
        const string Text = @"# FamiTracker text export 0.4.2

# Song information
TITLE           ""Test Tune""
AUTHOR          """"

# Global settings
EXPANSION       0

# Macros
MACRO       0   0   1   2   0 : 15 12 10 8
MACRO       2   0  -1  -1   0 : 2 -1
MACRO       4   3  -1  -1   0 : 1

# Instruments
INST2A03   2     0  -1   0  -1   3 ""Lead""

# Tracks

TRACK   8   3 150 ""Main""
COLUMNS : 2 1 1 1 1

ORDER 00 : 00 00 00 00 00
ORDER 01 : 00 01 00 00 00
ORDER 02 : 00 00 00 00 00

PATTERN 00
ROW 00 : C-4 02 F 400 B01 : ... .. . ... : A-2 .. . ... : 1-# .. 8 ... : ... .. . ...
ROW 01 : --- .. . ... ... : ... .. . ... : === .. . ... : ... .. . ... : ... .. . ...

PATTERN 01
ROW 02 : ... .. . ... ... : D#5 02 . A0F : ... .. . ... : ... .. . ... : ... .. . ...
";

        [Test]
        public void Import_ConvertsTrackOrderAndCells()
        {
            var song = FamiTrackerImport.Import(Text, "fallback", out var report);
            try
            {
                Assert.AreEqual("Test Tune", song.title);
                Assert.AreEqual(3, song.speed);
                Assert.AreEqual(300f, song.bpm, 0.01f, "tempo 150 / speed 3 → 1 拍 4 行で 300");
                Assert.AreEqual(2, song.patterns.Count, "同じ組の順序はパターンを使い回す");
                CollectionAssert.AreEqual(new[] { 0, 1, 0 }, song.order);
                Assert.AreEqual(8, song.patterns[0].length);

                var c = song.patterns[0].tracks[0].cells[0];
                Assert.AreEqual(60, c.note);
                Assert.AreEqual(0, c.instrument, "楽器番号 02 → Song の 0 番");
                Assert.AreEqual(15, c.volume);
                Assert.AreEqual(FxType.Vibrato, c.fx1.type);
                Assert.AreEqual(FxType.None, c.fx2.type, "Bxx（ジャンプ）は落とす");
                Assert.AreEqual(2, report.skippedFx, "作ったパターンごとに数える（ch0 の 00 は 2 パターンで使う）");
                Assert.AreEqual(Cell.Off, song.patterns[0].tracks[0].cells[1].note);
                Assert.AreEqual(Cell.Off, song.patterns[0].tracks[2].cells[1].note);
                Assert.AreEqual(1, song.patterns[0].tracks[3].cells[0].note, "ノイズは 16 進のピッチ");
                Assert.AreEqual(8, song.patterns[0].tracks[3].cells[0].volume);
                var d = song.patterns[1].tracks[1].cells[2];
                Assert.AreEqual(75, d.note);
                Assert.AreEqual(FxType.VolumeSlide, d.fx1.type);
                Assert.AreEqual(0x0F, d.fx1.param);

                Assert.AreEqual(1, song.instruments.Count);
                var inst = song.instruments[0];
                Assert.AreEqual("Lead", inst.name);
                CollectionAssert.AreEqual(new[] { 15, 12, 10, 8 }, inst.volume.values);
                Assert.AreEqual(1, inst.volume.loop);
                Assert.AreEqual(3, inst.volume.release, "FamiTracker のリリース点 2 は UniDAW の 3");
                CollectionAssert.AreEqual(new[] { -2, 1 }, inst.pitch.values, "ピッチは符号を反転");
                CollectionAssert.AreEqual(new[] { 1 }, inst.duty.values);
            }
            finally
            {
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void Import_RejectsNonFamiTrackerText()
        {
            Assert.Throws<System.FormatException>(() => FamiTrackerImport.Import("hello", "x", out _));
        }
    }
}
