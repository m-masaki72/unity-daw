using NUnit.Framework;
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
    }
}
