using System.IO;
using NUnit.Framework;
using UniDAW.Editor;

namespace UniDAW.Tests
{
    /// <summary>デモの取り込み（Package Manager を通さないコピー）。</summary>
    public class DemoImportTests
    {
        [Test]
        public void CopyMissing_AddsOnlyNewFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "unidaw-demo-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var src = Path.Combine(root, "src");
                var dst = Path.Combine(root, "dst");
                Directory.CreateDirectory(Path.Combine(src, "sub"));
                File.WriteAllText(Path.Combine(src, "a.asset"), "new-a");
                File.WriteAllText(Path.Combine(src, "sub", "b.asset"), "b");
                Directory.CreateDirectory(dst);
                File.WriteAllText(Path.Combine(dst, "a.asset"), "edited-a");

                Assert.AreEqual(1, SongFileCommands.CopyMissing(src, dst));
                Assert.AreEqual("edited-a", File.ReadAllText(Path.Combine(dst, "a.asset")), "取り込み済みは上書きしない");
                Assert.AreEqual("b", File.ReadAllText(Path.Combine(dst, "sub", "b.asset")));
                Assert.AreEqual(0, SongFileCommands.CopyMissing(src, dst));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PackageHasDemoSamples()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SongFileCommands).Assembly);
            Assert.IsNotNull(info);
            var demo = Path.Combine(info.resolvedPath, "Samples~", "Demo");
            foreach (var f in new[] { "DemoSong.asset", "SunsetLoFi.asset", "IronFortress.asset", "UniDAWDemo.unity" })
                Assert.IsTrue(File.Exists(Path.Combine(demo, f)), f);
        }
    }
}
