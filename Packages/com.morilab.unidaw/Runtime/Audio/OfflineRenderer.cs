using System;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>リアルタイムを待たずに曲を PCM へ書き出す（WAV 書き出し・CLI・テスト用）。</summary>
    public static class OfflineRenderer
    {
        const int BlockFrames = 512;

        /// <summary>曲を 1 周レンダリング（モノラル）。</summary>
        public static float[] RenderSong(Song song, int sampleRate, double maxSeconds = 600)
        {
            var e = new ChipEngine(sampleRate) { Song = song, EmitNoteEvents = false };
            e.Play();
            var buf = new float[BlockFrames];
            var outList = new List<float>(sampleRate * 10);
            long maxFrames = (long)(maxSeconds * sampleRate);
            while (outList.Count < maxFrames)
            {
                e.Render(buf, 0, BlockFrames, 1);
                outList.AddRange(buf);
                // 最後の行が終わったところで止める（ブロック単位なので軽く余韻が乗る）
                if (e.SongLoopCount >= 1) break;
            }
            // 短いフェードアウト
            int fade = Math.Min(outList.Count, sampleRate / 50);
            for (int i = 0; i < fade; i++)
                outList[outList.Count - 1 - i] *= i / (float)fade;
            return outList.ToArray();
        }
    }
}
