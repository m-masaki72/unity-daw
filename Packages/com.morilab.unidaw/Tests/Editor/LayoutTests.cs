using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>UI 刷新の下準備：パネルの自動折りたたみと、順序表・Claude 候補のキー操作。</summary>
    public class LayoutTests
    {
        static readonly PanelVisibility All = new PanelVisibility(true, true, true, true);

        static PanelVisibility Fit(float w, float h, PatternViewKind view = PatternViewKind.Tracker) =>
            PanelLayout.Fit(w, h, All, 150, 270, 80, 230, view);

        [Test]
        public void Fit_WideWindow_KeepsEverything()
        {
            Assert.AreEqual(All.ToString(), Fit(1400, 900).ToString());
        }

        [Test]
        public void Fit_Narrow_CollapsesRightThenLeft()
        {
            Assert.AreEqual("L1 R0 B1 Z1", Fit(900, 900).ToString(), "トラッカー 700 + 左 150 は入る");
            Assert.AreEqual("L0 R0 B1 Z1", Fit(800, 900).ToString());
            Assert.AreEqual("L1 R1 B1 Z1", Fit(800, 900, PatternViewKind.PianoRoll).ToString(), "ピアノロールは 360 で足りる");
        }

        [Test]
        public void Fit_Short_CollapsesBandThenLower()
        {
            Assert.AreEqual("L1 R1 B0 Z1", Fit(1400, 420).ToString());
            Assert.AreEqual("L1 R1 B0 Z0", Fit(1400, 300).ToString());
        }

        [Test]
        public void Fit_BeforeLayout_ReturnsWanted()
        {
            Assert.AreEqual(All.ToString(), Fit(float.NaN, float.NaN).ToString());
            Assert.AreEqual(All.ToString(), Fit(0, 0).ToString());
            Assert.AreEqual("L1 R1 B1 Z1", PanelLayout.Fit(1400, 900, All, float.NaN, 270, 80, 230, PatternViewKind.Tracker).ToString());
        }

        static void WithWindow(System.Action<UniDAWWindow, Song> body)
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                body(w, song);
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void OrderKeys_WorkInEveryView()
        {
            foreach (PatternViewKind view in System.Enum.GetValues(typeof(PatternViewKind)))
            {
                WithWindow((w, song) =>
                {
                    w.State.view = view;
                    int n = song.order.Count;
                    Assert.IsTrue(w.HandleKey(KeyCode.Insert, false, false, true), view + ": Alt+Ins");
                    Assert.AreEqual(n + 1, song.order.Count);
                    Assert.AreEqual(1, w.State.orderIndex, "追加したエントリを選択");

                    Assert.IsTrue(w.HandleKey(KeyCode.UpArrow, true, false, true), view + ": Ctrl+Alt+↑");
                    Assert.AreEqual(1, song.TransposeAtOrder(1));
                    Assert.IsTrue(w.HandleKey(KeyCode.DownArrow, true, true, true));
                    Assert.AreEqual(-11, song.TransposeAtOrder(1), "Shift で 1 オクターブ");

                    Assert.IsTrue(w.HandleKey(KeyCode.PageUp, false, false, true), view + ": Alt+PgUp");
                    Assert.AreEqual(0, w.State.orderIndex);
                    Assert.AreEqual(-11, song.TransposeAtOrder(0), "移調も一緒に動く");

                    int octave = w.State.octave;
                    Assert.IsTrue(w.HandleKey(KeyCode.RightBracket, false, false, true), view + ": Alt+]");
                    Assert.AreEqual(octave, w.State.octave, "Alt+] はオクターブを変えない");

                    Assert.IsTrue(w.HandleKey(KeyCode.Delete, false, false, true), view + ": Alt+Del");
                    Assert.AreEqual(n, song.order.Count);
                });
            }
        }

        [Test]
        public void PlainKeys_StillEditCells()
        {
            WithWindow((w, song) =>
            {
                int n = song.order.Count, octave = w.State.octave;
                Assert.IsTrue(w.HandleKey(KeyCode.RightBracket, false, false));
                Assert.AreEqual(octave + 1, w.State.octave, "修飾キーなしの ] はオクターブ");
                Assert.IsTrue(w.HandleKey(KeyCode.Delete, false, false));
                Assert.AreEqual(n, song.order.Count, "修飾キーなしの Del は順序表に触らない");
            });
        }
    }
}
