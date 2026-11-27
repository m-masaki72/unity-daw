using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniDAW.Composer;
using UniDAW.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>入力支援：スケール・コード解析・伴奏生成・メロディ提案と、その Undo。</summary>
    public class AssistTests
    {
        static readonly Scale CMajor = new Scale(0, ScaleMode.Major);

        // ---- スケール ------------------------------------------------------------------

        [Test]
        public void Scale_ContainsAndSnap()
        {
            Assert.IsTrue(CMajor.Contains(60));
            Assert.IsTrue(CMajor.Contains(71));
            Assert.IsFalse(CMajor.Contains(61));
            Assert.AreEqual(60, CMajor.Snap(61), "C# は等距離なので下へ");
            Assert.AreEqual(62, CMajor.Snap(61, 1));
            Assert.AreEqual(64, CMajor.Snap(64));
            var aMinor = new Scale(9, ScaleMode.Minor);
            Assert.IsTrue(aMinor.Contains(60) && !aMinor.Contains(68));
            var penta = new Scale(0, ScaleMode.MajorPentatonic);
            Assert.AreEqual(64, penta.Snap(65, 1), "ペンタに F はないので近い E へ");
            Assert.AreEqual(64, penta.Step(60, 2));
            Assert.AreEqual(57, penta.Step(60, -1));
            Assert.IsTrue(new Scale(0, ScaleMode.Chromatic).Contains(61));
        }

        // ---- コード --------------------------------------------------------------------

        [Test]
        public void Chord_ParseLettersRomanAndSlash()
        {
            Assert.IsTrue(Chord.TryParse("Am7", CMajor, out var c));
            Assert.AreEqual(9, c.root);
            Assert.AreEqual(ChordQuality.Minor7, c.quality);
            Assert.IsTrue(Chord.TryParse("F#dim", CMajor, out c));
            Assert.AreEqual(6, c.root);
            Assert.AreEqual(ChordQuality.Diminished, c.quality);
            Assert.IsTrue(Chord.TryParse("C/E", CMajor, out c));
            Assert.AreEqual(4, c.BassPc);
            Assert.AreEqual("C/E", c.Name);
            Assert.IsTrue(Chord.TryParse("Bb", CMajor, out c));
            Assert.AreEqual(10, c.root);

            var g = new Scale(7, ScaleMode.Major);
            Assert.IsTrue(Chord.TryParse("vi", g, out c));
            Assert.AreEqual(4, c.root, "G の vi = Em");
            Assert.AreEqual(ChordQuality.Minor, c.quality);
            Assert.IsTrue(Chord.TryParse("V7", g, out c));
            Assert.AreEqual(2, c.root);
            Assert.AreEqual(ChordQuality.Dominant7, c.quality);
            Assert.IsTrue(Chord.TryParse("bVII", CMajor, out c));
            Assert.AreEqual(10, c.root);
            Assert.IsFalse(Chord.TryParse("Xyz", CMajor, out _));
        }

        [Test]
        public void ChordProgression_ParseAndLayout()
        {
            var list = ChordProgression.Parse("C | G, Am % F zz", CMajor, out var error);
            Assert.AreEqual(5, list.Count);
            Assert.AreEqual("Am", list[3].Name, "% は直前の繰り返し");
            StringAssert.Contains("zz", error);

            Assert.AreEqual(0, ChordProgression.StartRow(0, 3, 16));
            Assert.AreEqual(6, ChordProgression.StartRow(1, 3, 16));
            Assert.AreEqual(16, ChordProgression.StartRow(3, 3, 16));
            for (int row = 0; row < 16; row++)
            {
                int i = ChordProgression.IndexAt(row, 3, 16);
                Assert.That(row >= ChordProgression.StartRow(i, 3, 16) && row < ChordProgression.StartRow(i + 1, 3, 16), $"row {row}");
            }
            Assert.IsNull(ChordProgression.At(new List<Chord>(), 0, 16));
        }

        // ---- 伴奏 ----------------------------------------------------------------------

        static List<Chord> Prog(string text) => ChordProgression.Parse(text, CMajor, out _);

        [Test]
        public void Accompaniment_BassChordsDrums()
        {
            var chords = Prog("C G Am F");
            foreach (BassStyle style in Enum.GetValues(typeof(BassStyle)))
            {
                var bass = Accompaniment.Bass(chords, 32, 4, style);
                Assert.AreEqual(32, bass.Length);
                if (style == BassStyle.None) { Assert.IsFalse(bass.Any(x => x.HasNote)); continue; }
                Assert.IsTrue(bass[0].HasNote, style.ToString());
                Assert.AreEqual(0, Scale.Pc(bass[0].note), "最初は C");
                Assert.AreEqual(7, Scale.Pc(bass[8].note), $"{style}: 2 つ目のコードの頭は G");
                Assert.IsTrue(bass.Where(x => x.HasNote).All(x => x.note >= 28 && x.note <= 72));
            }

            var arp = Accompaniment.Chords(chords, 32, 4, ChordStyle.Arpeggio);
            Assert.AreEqual(FxType.Arpeggio, arp[0].fx1.type);
            Assert.AreEqual(0x47, arp[0].fx1.param, "C = 0 4 7");
            Assert.AreEqual(0x37, arp[16].fx1.param, "Am = 0 3 7");
            var up = Accompaniment.Chords(chords, 32, 4, ChordStyle.ArpUp);
            Assert.IsTrue(up.Take(8).Where(x => x.HasNote).All(x => new Chord(0, ChordQuality.Major).ContainsPc(x.note)));

            var drums = Accompaniment.Drums(32, 4, DrumStyle.Basic, true);
            Assert.AreEqual(Accompaniment.KickPitch, drums[0].note);
            Assert.AreEqual(Accompaniment.SnarePitch, drums[4].note);
            Assert.AreEqual(Accompaniment.HatPitch, drums[2].note);
            Assert.IsTrue(drums.Skip(28).All(x => x.note == Accompaniment.SnarePitch), "最後の 1 拍はフィル");
            Assert.IsTrue(drums.Where(x => x.HasNote).All(x => x.note >= 0 && x.note <= 15));
            Assert.IsFalse(Accompaniment.Drums(32, 4, DrumStyle.Basic, false).Skip(28).All(x => x.note == Accompaniment.SnarePitch));
        }

        // ---- メロディ ------------------------------------------------------------------

        static MelodyModel.Options Opts(int seed) => new MelodyModel.Options
        {
            key = CMajor,
            chordAt = row => ChordProgression.At(Prog("C F G C"), row, 32),
            rowsPerBeat = 4,
            seed = seed,
            minPitch = 55,
            maxPitch = 84,
            center = 67,
        };

        static MelodyModel Trained()
        {
            var m = new MelodyModel();
            var phrase = new List<MelodyModel.Note>();
            int[] tune = { 60, 62, 64, 65, 67, 65, 64, 62, 60, 64, 67, 72, 71, 67, 65, 64 };
            for (int i = 0; i < tune.Length; i++) phrase.Add(new MelodyModel.Note(i * 2, 2, tune[i]));
            m.Train(phrase);
            return m;
        }

        [Test]
        public void Melody_DeterministicInScaleAndInRange()
        {
            var m = Trained();
            Assert.Greater(m.TrainedSteps, 0);
            var context = new List<MelodyModel.Note> { new MelodyModel.Note(0, 2, 60), new MelodyModel.Note(2, 2, 64) };
            var a = m.Continue(context, 4, 28, Opts(5));
            var b = m.Continue(context, 4, 28, Opts(5));
            Assert.Greater(a.Count, 2);
            CollectionAssert.AreEqual(a.Select(n => (n.start, n.length, n.pitch)).ToList(), b.Select(n => (n.start, n.length, n.pitch)).ToList(), "同じ seed なら同じ結果");
            foreach (var n in a)
            {
                Assert.IsTrue(CMajor.Contains(n.pitch), $"{n.pitch} はスケール内");
                Assert.That(n.pitch >= 55 && n.pitch <= 84);
                Assert.That(n.start >= 4 && n.start + n.length <= 32);
            }
            for (int i = 1; i < a.Count; i++) Assert.Greater(a[i].start, a[i - 1].start);
            bool differs = Enumerable.Range(6, 6).Any(seed =>
                !m.Continue(context, 4, 28, Opts(seed)).Select(n => (n.start, n.pitch)).SequenceEqual(a.Select(n => (n.start, n.pitch))));
            Assert.IsTrue(differs, "seed を変えると別の案");
            Assert.AreEqual(0, Scale.Pc(a[a.Count - 1].pitch), "最後は主音に解決");
        }

        [Test]
        public void Melody_WorksWithoutTraining()
        {
            var notes = new MelodyModel().Continue(new List<MelodyModel.Note>(), 0, 16, Opts(1));
            Assert.Greater(notes.Count, 0);
            Assert.IsTrue(notes.All(n => CMajor.Contains(n.pitch)));
        }

        // ---- ウィンドウ経由 ------------------------------------------------------------

        static void WithWindow(Action<UniDAWWindow, Song> body)
        {
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                body(w, song);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        static void Undo()
        {
            UnityEditor.Undo.PerformUndo();
        }

        [Test]
        public void AssistCommands_ConformToScaleWithUndo()
        {
            WithWindow((w, song) =>
            {
                var a = w.Assist;
                var cells = song.patterns[0].tracks[0].cells;
                cells[0] = Cell.NoteCell(61, 0);
                cells[4] = Cell.NoteCell(66, 0);
                cells[8] = Cell.NoteCell(64, 0);
                song.patterns[0].tracks[2].cells[0] = Cell.NoteCell(49, 2);

                Assert.AreEqual(0, a.ConformToScale(false), "キーなしでは何もしない");
                UnityEditor.Undo.IncrementCurrentGroup();
                a.SetKey(0, ScaleMode.Major);
                Assert.AreEqual(ScaleMode.Major, song.scaleMode);
                UnityEditor.Undo.IncrementCurrentGroup();
                Assert.AreEqual(2, a.ConformToScale(false), "カーソルのチャンネルだけ");
                Assert.AreEqual(60, cells[0].note);
                Assert.AreEqual(65, cells[4].note);
                Assert.AreEqual(64, cells[8].note);
                Assert.AreEqual(49, song.patterns[0].tracks[2].cells[0].note);
                UnityEditor.Undo.IncrementCurrentGroup();
                Assert.AreEqual(1, a.ConformToScale(true));
                Assert.AreEqual(48, song.patterns[0].tracks[2].cells[0].note);

                Undo();
                Undo();
                Assert.AreEqual(61, song.patterns[0].tracks[0].cells[0].note, "Undo で戻る");
                Assert.AreEqual(49, song.patterns[0].tracks[2].cells[0].note);
            });
        }

        [Test]
        public void AssistCommands_GenerateAccompanimentWithUndo()
        {
            WithWindow((w, song) =>
            {
                var a = w.Assist;
                song.patterns[0].tracks[2].cells[3] = Cell.NoteCell(30, 2);
                song.patterns[0].tracks[2].length = 8;
                Assert.IsFalse(a.GenerateAccompaniment(), "コードなし");

                UnityEditor.Undo.IncrementCurrentGroup();
                a.SetChords("  I V vi IV ");
                Assert.AreEqual("I V vi IV", song.patterns[0].chords);
                Assert.AreEqual(4, a.Chords(out _).Count);
                Assert.AreEqual("G", a.ChordAt(8)?.Name);

                UnityEditor.Undo.IncrementCurrentGroup();
                Assert.IsTrue(a.GenerateAccompaniment());
                var p = song.patterns[0];
                Assert.AreEqual(0, p.tracks[2].length, "ポリメーターは解除");
                Assert.AreEqual(0, Scale.Pc(p.tracks[2].cells[0].note));
                Assert.AreEqual(Song.InstBass, p.tracks[2].cells[0].instrument);
                Assert.IsTrue(p.tracks[1].cells.Any(c => c.HasNote));
                Assert.AreEqual(Accompaniment.KickPitch, p.tracks[3].cells[0].note);

                Undo();
                Assert.AreEqual(30, song.patterns[0].tracks[2].cells[3].note);
                Assert.AreEqual(8, song.patterns[0].tracks[2].length);
                Assert.AreEqual("I V vi IV", song.patterns[0].chords, "コードの入力は別の Undo");
            });
        }

        [Test]
        public void AssistCommands_ContinueMelodyAndReroll()
        {
            WithWindow((w, song) =>
            {
                var a = w.Assist;
                a.SetKey(0, ScaleMode.Major);
                var cells = song.patterns[0].tracks[0].cells;
                int[] tune = { 60, 62, 64, 67 };
                for (int i = 0; i < tune.Length; i++) cells[i * 2] = Cell.NoteCell(tune[i], 0);
                w.State.cursorRow = 8;
                w.State.assistRows = 16;

                UnityEditor.Undo.IncrementCurrentGroup();
                int n = a.ContinueMelody(false);
                Assert.Greater(n, 0);
                var p = song.patterns[0];
                Assert.AreEqual(60, p.tracks[0].cells[0].note, "前の音はそのまま");
                var added = Enumerable.Range(8, 16).Where(r => p.tracks[0].cells[r].HasNote).ToList();
                Assert.AreEqual(n, added.Count);
                Assert.IsTrue(added.All(r => CMajor.Contains(p.tracks[0].cells[r].note)));
                Assert.IsFalse(Enumerable.Range(24, 8).Any(r => p.tracks[0].cells[r].HasNote), "範囲の外には書かない");
                Assert.IsTrue(a.CanReroll);

                UnityEditor.Undo.IncrementCurrentGroup();
                Assert.Greater(a.ContinueMelody(true), 0);
                Assert.IsFalse(Enumerable.Range(24, 8).Any(r => p.tracks[0].cells[r].HasNote));

                Undo();
                Undo();
                Assert.IsFalse(Enumerable.Range(8, 16).Any(r => song.patterns[0].tracks[0].cells[r].HasNote), "Undo で消える");

                w.State.cursorCh = 3;
                Assert.AreEqual(0, a.ContinueMelody(false), "ノイズでは使えない");
            });
        }

        [Test]
        public void Song_ValidateNormalizesKey()
        {
            var s = Song.CreateDefault(16);
            Assert.IsTrue(s.Key.IsChromatic, "既定はキーなし");
            s.keyRoot = 14;
            s.scaleMode = (ScaleMode)99;
            s.Validate();
            Assert.AreEqual(2, s.keyRoot);
            Assert.AreEqual(ScaleMode.Chromatic, s.scaleMode);
            s.patterns[0].chords = "C G";
            Assert.AreEqual("C G", s.patterns[0].Clone().chords);
            Object.DestroyImmediate(s);
        }
    }
}
