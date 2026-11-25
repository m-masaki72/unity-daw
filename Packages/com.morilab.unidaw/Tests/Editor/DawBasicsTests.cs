using System;
using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>メトロノーム・カウントイン・センドエフェクト・アレンジ（順序表の並べ替え）。</summary>
    public class DawBasicsTests
    {
        const int Rate = 48000;

        static Song Blank()
        {
            var s = Song.CreateDefault(16);
            s.bpm = 150;
            for (int c = 0; c < Song.ChannelCount; c++)
                for (int r = 0; r < 16; r++) s.patterns[0].tracks[c].cells[r] = Cell.Blank;
            return s;
        }

        static double Rms(ChipEngine e, int frames)
        {
            var buf = new float[frames * 2];
            e.Render(buf, 0, frames, 2);
            double sum = 0;
            foreach (var v in buf) sum += v * v;
            return Math.Sqrt(sum / buf.Length);
        }

        // ---- メトロノーム ---------------------------------------------------------------

        [Test]
        public void Metronome_ClicksOnlyWhenEnabled()
        {
            var s = Blank();
            var on = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false, Metronome = true };
            on.PlayPattern(0);
            Assert.Greater(Rms(on, Rate), 0.001, "空の曲でもクリックが鳴る");

            var off = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            off.PlayPattern(0);
            Assert.Less(Rms(off, Rate), 1e-6, "メトロノームを切ると無音");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void CountIn_WaitsFourBeatsBeforePlaying()
        {
            var s = Blank();
            var e = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            e.PlayPattern(0, 0, 4);
            Assert.Greater(Rms(e, Rate / 10), 0.001, "カウント中はメトロノームを切っていてもクリックが鳴る");
            Assert.IsTrue(e.CountingIn, "最初はカウント中");
            Assert.AreEqual(-1, e.PlayingPattern, "カウント中はパターンを進めない");

            // 4 拍 = 1.6 秒（150 BPM）。少しずつ回して終わる時刻を測る
            double t = 0.1;
            while (e.CountingIn && t < 5)
            {
                Rms(e, Rate / 100);
                t += 0.01;
            }
            Assert.IsFalse(e.CountingIn);
            Assert.AreEqual(1.6, t, 0.25, "カウントインは 4 拍ぶん");
            Rms(e, Rate / 20);
            Assert.AreEqual(0, e.PlayingPattern, "カウント後は再生が始まる");
            Object.DestroyImmediate(s);
        }

        // ---- センドエフェクト ------------------------------------------------------------

        static double TailAfterStop(Song s)
        {
            var e = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            e.PlayPattern(0);
            Rms(e, Rate / 4);
            e.Stop();
            Rms(e, Rate / 10);
            return Rms(e, Rate / 4);
        }

        [Test]
        public void DelaySend_LeavesTailAfterStop()
        {
            var s = Blank();
            s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstBass);
            double dry = TailAfterStop(s);
            s.Mix(0).SetSend(SendKind.Delay, 1f);
            s.effects.delayFeedback = 0.6f;
            double wet = TailAfterStop(s);
            Assert.Greater(wet, 0.001, "ディレイの残響が残る");
            Assert.Greater(wet, dry * 4 + 1e-5, "送りなしより明らかに大きい");

            s.Mix(0).SetSend(SendKind.Delay, 0f);
            s.Mix(0).SetSend(SendKind.Reverb, 1f);
            Assert.Greater(TailAfterStop(s), dry * 4 + 1e-5, "リバーブも残響が残る");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Effects_ValidateClampsAndRestoresMissing()
        {
            var s = Blank();
            s.mixer[1].delaySend = 3f;
            s.mixer[1].reverbSend = -1f;
            s.effects.delayFeedback = 5f;
            s.effects.reverbSize = -2f;
            s.Validate();
            Assert.AreEqual(1f, s.mixer[1].delaySend);
            Assert.AreEqual(0f, s.mixer[1].reverbSend);
            Assert.LessOrEqual(s.effects.delayFeedback, SongEffects.MaxFeedback);
            Assert.AreEqual(0f, s.effects.reverbSize);

            s.effects = null;
            s.Validate();
            Assert.IsNotNull(s.effects, "古い曲（effects なし）でも既定値が入る");
            Object.DestroyImmediate(s);
        }

        // ---- アレンジ --------------------------------------------------------------------

        [Test]
        public void ArrangeLayout_BlocksAndDropIndex()
        {
            var s = Song.CreateDefault(16);
            s.patterns.Add(s.patterns[0].Clone());
            s.patterns[1].length = 32;
            s.order.Clear();
            s.order.AddRange(new[] { 0, 1, 0 });
            s.Validate();
            var b = ArrangeLayout.Blocks(s);
            Assert.AreEqual(3, b.Count);
            Assert.AreEqual(16, b[1].start);
            Assert.AreEqual(48, b[2].start);
            Assert.AreEqual(64, ArrangeLayout.TotalRows(b));
            Assert.AreEqual(1, ArrangeLayout.BlockAt(b, 20));
            Assert.AreEqual(-1, ArrangeLayout.BlockAt(b, 80));

            Assert.AreEqual(0, ArrangeLayout.DropIndex(b, 2, 3f), "末尾を先頭へ");
            Assert.AreEqual(2, ArrangeLayout.DropIndex(b, 0, 60f), "先頭を末尾へ（取り除いた後の番号）");
            Assert.AreEqual(1, ArrangeLayout.DropIndex(b, 0, 40f), "2 番目の中心より右なら、その後ろ");
            Assert.AreEqual("2.1", ArrangeLayout.BarBeat(16, 4));
            Object.DestroyImmediate(s);
        }

        [Test]
        public void OrderEntries_MoveDuplicateUnique_WithUndo()
        {
            var song = Song.CreateDefault(16);
            song.patterns.Add(song.patterns[0].Clone());
            song.order.Clear();
            song.order.AddRange(new[] { 0, 1 });
            song.Validate();
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                var ses = w.Order;

                ses.Move(0, 1);
                CollectionAssert.AreEqual(new[] { 1, 0 }, song.order);
                Assert.AreEqual(1, w.State.orderIndex, "動かしたエントリが選択される");

                UnityEditor.Undo.IncrementCurrentGroup();
                ses.Duplicate(0);
                CollectionAssert.AreEqual(new[] { 1, 1, 0 }, song.order);

                UnityEditor.Undo.IncrementCurrentGroup();
                int before = song.patterns.Count;
                ses.MakeUnique(1);
                Assert.AreEqual(before + 1, song.patterns.Count);
                Assert.AreEqual(before, song.order[1], "このエントリだけ新しいパターン");
                Assert.AreEqual(1, song.order[0], "他の出現はそのまま");
                StringAssert.EndsWith("'", song.patterns[before].name);

                UnityEditor.Undo.IncrementCurrentGroup();
                ses.Transpose(2, 12);
                Assert.AreEqual(12, song.TransposeAtOrder(2));
                ses.Transpose(2, null);
                Assert.AreEqual(0, song.TransposeAtOrder(2));

                UnityEditor.Undo.IncrementCurrentGroup();
                ses.Remove(0);
                Assert.AreEqual(2, song.order.Count);
                UnityEditor.Undo.PerformUndo();
                Assert.AreEqual(3, song.order.Count, "削除を Undo で戻せる");
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }
    }
}
