using System;
using System.IO;
using System.Text;

namespace UniDAW
{
    public static class WavWriter
    {
        /// <summary>モノラル float を 16bit PCM ステレオ WAV として書き出す（一時ファイル→置換で原子的に）。</summary>
        public static void WriteStereo16(string path, float[] mono, int sampleRate)
        {
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                const int channels = 2;
                int dataBytes = mono.Length * channels * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (var f in mono)
                {
                    short s = (short)Math.Round(Math.Max(-1f, Math.Min(1f, f)) * 32767f);
                    w.Write(s);
                    w.Write(s);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
