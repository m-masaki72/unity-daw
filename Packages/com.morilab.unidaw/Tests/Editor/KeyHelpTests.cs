using System.Linq;
using NUnit.Framework;
using UniDAW.Composer;
using UniDAW.Editor;

namespace UniDAW.Tests
{
    /// <summary>キー早見表の並べ替えと検索。</summary>
    public class KeyHelpTests
    {
        [Test]
        public void Arrange_PutsCurrentViewFirst()
        {
            var all = KeyHelp.Sections(DawKeyPreset.Cubase);
            Assert.AreEqual("ピアノロール", KeyHelp.Arrange(all, PatternViewKind.PianoRoll, "")[0].Title);
            Assert.AreEqual("トラッカー / ステップ", KeyHelp.Arrange(all, PatternViewKind.Step, "")[0].Title);
            Assert.AreEqual(all.Length, KeyHelp.Arrange(all, PatternViewKind.Tracker, null).Count);
        }

        [Test]
        public void Arrange_FiltersRowsByAllWords()
        {
            var all = KeyHelp.Sections(DawKeyPreset.Cubase);
            var r = KeyHelp.Arrange(all, PatternViewKind.Tracker, "移調 ctrl");
            Assert.IsTrue(r.SelectMany(s => s.Rows).All(x => (x.keys + x.desc).Contains("移調") && x.keys.ToLower().Contains("ctrl")));
            Assert.AreEqual(2, r.Count, "トラッカーの Ctrl+↑↓ と順序表の Ctrl+Alt+↑↓");
            // 見出しが合えば区画ごと
            var p = KeyHelp.Arrange(all, PatternViewKind.Tracker, "ピアノ");
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual(all.First(s => s.Title == "ピアノロール").Rows.Length, p[0].Rows.Length);
            Assert.AreEqual(0, KeyHelp.Arrange(all, PatternViewKind.Tracker, "存在しない操作").Count);
        }

        [Test]
        public void Sections_FollowPreset()
        {
            string Keys(DawKeyPreset p) => string.Join("|", KeyHelp.Sections(p).First(s => s.Title == "ピアノロール").Rows.Select(x => x.keys));
            StringAssert.Contains("Alt+←→", Keys(DawKeyPreset.StudioOne));
            StringAssert.Contains("Ctrl+←→", Keys(DawKeyPreset.Cubase));
            StringAssert.Contains("Space : 再生 / 停止", ComposerKeymap.Help(DawKeyPreset.Cubase));
        }
    }
}
