using System;
using NUnit.Framework;
using UniDAW.Composer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>マスターチェーン（EQ・幅・コンプ・リミッター）とラウドネス測定・解析。</summary>
    public class MasteringTests
    {
        const int Rate = 48000;

        static float[] Sine(double hz, double seconds, float ampL, float ampR, int rate = Rate)
        {
            int n = (int)(seconds * rate);
            var buf = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float v = (float)Math.Sin(2 * Math.PI * hz * i / rate);
                buf[i * 2] = v * ampL;
                buf[i * 2 + 1] = v * ampR;
            }
            return buf;
        }

        static Song MakeSong()
        {
            var s = Song.CreateDefault(32);
            s.bpm = 150;
            s.speed = 6;
            for (int r = 0; r < 32; r += 4)
            {
                s.patterns[0].tracks[0].cells[r] = Cell.NoteCell(60 + (r / 4) % 5, Song.InstBass);
                s.patterns[0].tracks[2].cells[r] = Cell.NoteCell(40, Song.InstBass);
            }
            s.order = new System.Collections.Generic.List<int> { 0, 0, 0 };
            s.Validate();
            return s;
        }

        static float MaxAbs(float[] buf)
        {
            float m = 0f;
            foreach (var v in buf) m = Math.Max(m, Math.Abs(v));
            return m;
        }

        [Test]
        public void Loudness_FullScaleSineIsZeroLufs()
        {
            // BS.1770：997 Hz・0 dBFS の正弦波を両チャンネルで 0 LUFS、片チャンネルで -3.01 LUFS
            var both = LoudnessMeter.Measure(Sine(997, 5, 1f, 1f), Rate);
            Assert.AreEqual(0f, both.integratedLufs, 0.3f);
            Assert.AreEqual(0f, both.peakDb, 0.05f);
            Assert.AreEqual(1f, both.correlation, 0.01f);
            var one = LoudnessMeter.Measure(Sine(997, 5, 1f, 0f), Rate);
            Assert.AreEqual(-3.01f, one.integratedLufs, 0.3f);
            var quiet = LoudnessMeter.Measure(Sine(997, 5, 0.1f, 0.1f), Rate);
            Assert.AreEqual(-20f, quiet.integratedLufs, 0.3f);
            Assert.IsTrue(LoudnessMeter.Measure(new float[Rate * 2], Rate).Silent);
        }

        [Test]
        public void Loudness_BandShares()
        {
            var low = LoudnessMeter.Measure(Sine(60, 2, 0.5f, 0.5f), Rate);
            Assert.Greater(low.lowShare, 0.8f);
            var high = LoudnessMeter.Measure(Sine(9000, 2, 0.5f, 0.5f), Rate);
            Assert.Greater(high.highShare, 0.8f);
            var anti = LoudnessMeter.Measure(Sine(440, 2, 0.5f, -0.5f), Rate);
            Assert.Less(anti.correlation, -0.9f);
        }

        [Test]
        public void Master_DisabledIsBypass()
        {
            var s = MakeSong();
            try
            {
                s.master.enabled = false;
                s.master.gainDb = 12f;
                s.master.compRatio = 8f;
                var off = OfflineRenderer.RenderSongStereo(s, Rate);
                var bypass = OfflineRenderer.RenderSongStereo(s, Rate, e => e.BypassMaster = true);
                CollectionAssert.AreEqual(bypass, off);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Master_LimiterRespectsCeiling()
        {
            var s = MakeSong();
            try
            {
                s.master.enabled = true;
                s.master.gainDb = 12f;
                s.master.limiter = true;
                s.master.ceilingDb = -3f;
                var buf = OfflineRenderer.RenderSongStereo(s, Rate);
                Assert.LessOrEqual(MaxAbs(buf), ChannelMix.DbToGain(-3f) + 1e-4f);
                Assert.Greater(MaxAbs(buf), ChannelMix.DbToGain(-4f), "ゲインを上げたので上限近くまで来る");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Master_WidthZeroIsMono()
        {
            var s = MakeSong();
            try
            {
                s.mixer[0].pan = -1f;
                s.master.enabled = true;
                s.master.limiter = false;
                s.master.width = 0f;
                var buf = OfflineRenderer.RenderSongStereo(s, Rate);
                double diff = 0, sum = 0;
                for (int i = 0; i < buf.Length; i += 2)
                {
                    diff += Math.Abs(buf[i] - buf[i + 1]);
                    sum += Math.Abs(buf[i]);
                }
                Assert.Greater(sum, 1.0);
                Assert.Less(diff, sum * 1e-4);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Master_CompressorReducesPeaks()
        {
            var s = MakeSong();
            try
            {
                s.master.enabled = true;
                s.master.limiter = false;
                float before = LoudnessMeter.Measure(OfflineRenderer.RenderSongStereo(s, Rate), Rate).integratedLufs;
                s.master.compThresholdDb = -30f;
                s.master.compRatio = 8f;
                float after = LoudnessMeter.Measure(OfflineRenderer.RenderSongStereo(s, Rate), Rate).integratedLufs;
                Assert.Less(after, before - 3f);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Analysis_MatchLoudnessHitsTarget()
        {
            var s = MakeSong();
            try
            {
                s.master.targetLufs = -16f;
                var m = MasterAnalysis.MatchLoudness(s, s.master, out var st);
                Assert.IsNotNull(m);
                Assert.IsTrue(m.enabled && m.limiter);
                Assert.IsTrue(Math.Abs(st.integratedLufs + 16f) < 1f || m.gainDb >= 12f, $"{st.integratedLufs} LUFS / gain {m.gainDb}");
                Assert.IsFalse(s.master.enabled, "曲の設定は変えない");

                var report = MasterAnalysis.Analyze(s, m);
                Assert.AreEqual(Song.ChannelCount, report.channelLufs.Length);
                Assert.IsTrue(float.IsNegativeInfinity(report.channelLufs[1]), "鳴っていない Pulse2 は無音");
                Assert.Greater(report.hints.Count, 0);
                StringAssert.Contains("LUFS", report.Text());
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Song_ValidateRestoresMaster()
        {
            var s = MakeSong();
            try
            {
                s.master = null;
                s.Validate();
                Assert.IsNotNull(s.master);
                s.master.ceilingDb = 5f;
                s.master.width = -1f;
                s.Validate();
                Assert.AreEqual(0f, s.master.ceilingDb);
                Assert.AreEqual(0f, s.master.width);
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
