using System;
using NUnit.Framework;
using Object = UnityEngine.Object;
using UniDAW.Composer;

namespace UniDAW.Tests
{
    /// <summary>再生中のラウドネスメーターと、音量をそろえた A/B 試聴。</summary>
    public class LiveLoudnessTests
    {
        const int Rate = 48000;

        [Test]
        public void LiveLoudness_MatchesOfflineMeter()
        {
            int frames = Rate * 5;
            var st = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                float a = 0.5f * (float)Math.Sin(2 * Math.PI * 997 * i / Rate) * (i < frames / 2 ? 1f : 0.25f);
                st[i * 2] = a;
                st[i * 2 + 1] = a;
            }
            var live = new LiveLoudness(Rate);
            for (int i = 0; i < frames; i++) live.Feed(st[i * 2], st[i * 2 + 1], st[i * 2] * 2f, st[i * 2 + 1] * 2f);
            Assert.IsTrue(live.Poll());
            var off = LoudnessMeter.Measure(st, Rate);
            Assert.AreEqual(off.integratedLufs, live.Pre.IntegratedLufs, 0.1f);
            Assert.AreEqual(off.peakDb, live.Pre.PeakDb, 0.01f);
            Assert.AreEqual(5f, live.Pre.Seconds, 0.01f);
            Assert.AreEqual(50, live.Post.History.Count);
            // 後半は 12 dB 小さい
            Assert.AreEqual(live.Pre.History[20] - 12.04f, live.Pre.MomentaryLufs, 0.1f);
            Assert.AreEqual(6.02f, live.ChainGainDb.Value, 0.05f, "後 − 前");

            // 再生し直したら測り直す
            live.Restart();
            for (int i = 0; i < Rate; i++) live.Feed(0.1f, 0.1f, 0.1f, 0.1f);
            live.Poll();
            Assert.AreEqual(1f, live.Post.Seconds, 0.01f);
        }

        static Song GainSong()
        {
            var s = Song.CreateDefault(16);
            s.bpm = 150;
            for (int r = 0; r < 16; r += 4)
            {
                s.patterns[0].tracks[0].cells[r] = Cell.NoteCell(60 + r / 4, Song.InstBass);
                s.patterns[0].tracks[2].cells[r] = Cell.NoteCell(40, Song.InstBass);
            }
            s.master.enabled = true;
            s.master.limiter = false;
            s.master.gainDb = 6f;
            return s;
        }

        static float[] Render(Song s, Action<ChipEngine> setup, out ChipEngine engine)
        {
            var e = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            setup(e);
            e.PlayPattern(0);
            const int block = 1024;
            var all = new float[block * 2 * 150];
            var buf = new float[block * 2];
            for (int b = 0; b < 150; b++)
            {
                e.Render(buf, 0, block, 2);
                Array.Copy(buf, 0, all, b * block * 2, block * 2);
            }
            engine = e;
            return all;
        }

        [Test]
        public void Audition_PlaysMixWithoutChainAtMatchedLevel()
        {
            var s = GainSong();
            try
            {
                var wet = Render(s, e => e.Loudness = new LiveLoudness(Rate), out var e1);
                e1.Loudness.Poll();
                Assert.Greater(e1.Loudness.Post.Seconds, 3f);
                Assert.AreEqual(6f, e1.Loudness.ChainGainDb.Value, 0.2f, "チェーンのゲインぶん大きい");
                Assert.IsTrue(Math.Abs(e1.Loudness.Post.IntegratedLufs - e1.Loudness.Pre.IntegratedLufs - 6f) < 0.2f);

                // A/B：チェーンの前の音を +6 dB で鳴らす = チェーン（ゲインだけ）を通した音と同じ
                var ab = Render(s, e =>
                {
                    e.AuditionBypass = true;
                    e.AuditionGainDb = 6f;
                }, out _);
                double diff = 0, sum = 0;
                for (int i = 0; i < wet.Length; i++)
                {
                    diff = Math.Max(diff, Math.Abs(wet[i] - ab[i]));
                    sum += wet[i] * wet[i];
                }
                Assert.Greater(sum, 1.0);
                Assert.Less(diff, 1e-3, "音量をそろえた A/B");

                // 補正なしなら 6 dB 小さい
                var raw = Render(s, e => e.AuditionBypass = true, out _);
                double sumRaw = 0;
                foreach (var v in raw) sumRaw += v * v;
                Assert.AreEqual(-6f, 10f * (float)Math.Log10(sumRaw / sum), 0.2f);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
