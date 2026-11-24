using System.IO;

namespace UniDAW
{
    /// <summary>一時ファイルに書いてから置き換える原子的なファイル書き込み。</summary>
    public static class AtomicFile
    {
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            Commit(tmp, path);
        }

        /// <summary>書き終えた一時ファイル tmp で path を置き換える（既存なら File.Replace で原子的に入れ替え）。</summary>
        public static void Commit(string tmp, string path)
        {
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw;
            }
        }
    }
}
