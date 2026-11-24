using System;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>リアルタイムを待たずに曲を PCM へ書き出す（WAV 書き出し・CLI・テスト用）。</summary>
    public static class OfflineRenderer
    {
        const int BlockFrames = 512;

        /// <summary>曲を 1 周レンダリング（モノラル。L/R を平均）。</summary>
        public static float[] RenderSong(Song song, int sampleRate, double maxSeconds = 600) =>
            Render(song, sampleRate, 1, maxSeconds);

        /// <summary>曲を 1 周レンダリング（ステレオ interleaved：L, R, L, R, …）。</summary>
        public static float[] RenderSongStereo(Song song, int sampleRate, double maxSeconds = 600) =>
            Render(song, sampleRate, 2, maxSeconds);

        static float[] Render(Song song, int sampleRate, int channels, double maxSeconds)
        {
            var e = new ChipEngine(sampleRate) { Song = song, EmitNoteEvents = false };
            e.Play();
            var buf = new float[BlockFrames * channels];
            var outList = new List<float>(sampleRate * 10 * channels);
            long maxSamples = (long)(maxSeconds * sampleRate) * channels;
            while (outList.Count < maxSamples)
            {
                e.Render(buf, 0, BlockFrames, channels);
                outList.AddRange(buf);
                // 最後の行が終わったところで止める（ブロック単位なので軽く余韻が乗る）
                if (e.SongLoopCount >= 1) break;
            }
            // 短いフェードアウト
            int frames = outList.Count / channels;
            int fade = Math.Min(frames, sampleRate / 50);
            for (int i = 0; i < fade; i++)
                for (int c = 0; c < channels; c++)
                    outList[(frames - 1 - i) * channels + c] *= i / (float)fade;
            return outList.ToArray();
        }
    }
}
