using System.IO;
using System.Reflection;
using NUnit.Framework;
using UniDAW.Composer;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>ゲーム内 DAW の殻：スナップショット Undo・JSON 保存・共通ルート要素。</summary>
    public class RuntimeHostTests
    {
        string folder;
        IDawPlatform previous;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "UniDAWRuntimeTest_" + System.Guid.NewGuid().ToString("N"));
            previous = DawPlatform.Current;
        }

        [TearDown]
        public void TearDown()
        {
            DawPlatform.Current = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        [Test]
        public void Undo_RestoresSnapshot_AndRedoReapplies()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var song = Song.CreateDefault(16);
            int raised = 0;
            p.UndoRedoPerformed += () => raised++;

            p.RecordUndo(song, "BPM");
            song.bpm = 200;
            Assert.IsTrue(p.CanUndo);
            Assert.IsTrue(p.Undo());
            Assert.AreEqual(150f, song.bpm);
            Assert.IsTrue(p.Redo());
            Assert.AreEqual(200f, song.bpm);
            Assert.AreEqual(2, raised);
            Object.DestroyImmediate(song);
        }

        [Test]
        public void Undo_MergesRapidRecordsWithSameName()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var song = Song.CreateDefault(16);
            p.RecordUndo(song, "Volume");
            song.bpm = 160;
            p.RecordUndo(song, "Volume");
            song.bpm = 170;
            Assert.IsTrue(p.Undo());
            Assert.AreEqual(150f, song.bpm, "ドラッグ中の連続記録は 1 段");
            Assert.IsFalse(p.Undo());
            Object.DestroyImmediate(song);
        }

        [Test]
        public void SongFiles_NewSong_SavesJsonAndReloads()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var state = new ComposerState();
            var session = new ComposerSession(state, new Transport(state, null, p), p);
            var files = new RuntimeSongFiles(session, p, folder);

            files.NewSong();
            Assert.IsNotNull(session.Song);
            var id = files.IdOf(session.Song);
            Assert.IsTrue(File.Exists(id));
            Assert.AreEqual(1, files.ListSongs().Count);

            session.Song.bpm = 123;
            session.MarkModified();
            files.SaveAll();
            var back = RuntimeSongFiles.Read(id);
            Assert.AreEqual(123f, back.bpm);
            Assert.AreEqual(session.Song.patterns.Count, back.patterns.Count);
            Object.DestroyImmediate(back);

            files.MakeEditableCopy();
            Assert.AreEqual(2, files.ListSongs().Count);
            Assert.AreNotEqual(id, files.IdOf(session.Song));

            // 片付けたら変更を拾わない
            files.Dispose();
            var copyId = files.IdOf(session.Song);
            session.Song.bpm = 99;
            session.MarkModified();
            files.SaveAll();
            var copy = RuntimeSongFiles.Read(copyId);
            Assert.AreNotEqual(99f, copy.bpm);
            Object.DestroyImmediate(copy);
        }

        [Test]
        public void ComposerRoot_BuildsAndEditsWithoutEditorWindow()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var root = new ComposerRoot(new ComposerState(), s => new RuntimeSongFiles(s, p, folder), p);
            try
            {
                root.Files.NewSong();
                Assert.IsNotNull(root.Session.Song);
                root.Session.SetCursor(0, 0, 0);
                Assert.IsTrue(root.HandleKey(KeyCode.Z, false, false), "ピアノキーで音符を置ける");
                Assert.IsTrue(root.State.CurrentPattern.tracks[0].cells[0].HasNote);
                Assert.IsTrue(p.Undo());
                Assert.IsFalse(root.State.CurrentPattern.tracks[0].cells[0].HasNote);
            }
            finally
            {
                root.Dispose();
            }
        }

        [Test]
        public void InGameComposer_BuildsIntoUIDocument_AndRestoresPlatform()
        {
            var go = new GameObject("UniDAW Composer Test");
            try
            {
                var host = go.AddComponent<UniDAWComposer>();
                host.dataFolder = folder;
                host.openLastSong = false;
                Assert.IsNotNull(host.styleSheet, "Reset で UniDAW.uss が入る");
                Assert.IsNotNull(host.theme, "Reset でランタイムテーマが入る");
                typeof(UniDAWComposer).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);

                Assert.AreSame(previous, DawPlatform.Current, "エディタでは共通のホストを差し替えない");
                Assert.AreSame(host.Platform, host.Composer.Platform);
                Assert.AreSame(host.Platform, host.Composer.Session.Platform);
                var doc = go.GetComponent<UnityEngine.UIElements.UIDocument>();
                Assert.IsNotNull(doc.panelSettings);
                Assert.AreSame(doc.rootVisualElement, host.Composer.parent);
                host.Files.NewSong();
                Assert.IsNotNull(host.Composer.Session.Song);
                Assert.DoesNotThrow(() => host.Files.OpenSong(), "曲の一覧メニューを出せる");
                // 編集モードでは OnDestroy が呼ばれないので明示的に呼ぶ
                var composer = host.Composer;
                typeof(UniDAWComposer).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                Assert.IsNull(composer.parent, "画面を外す");
                Assert.IsNull(host.Composer, "破棄の後は作り直さない");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Assert.AreSame(previous, DawPlatform.Current, "破棄でもとのホストに戻る");
        }
        [Test]
        public void InGameComposer_OpenSongCopiesAsset_AndKeepsEdits()
        {
            var go = new GameObject("UniDAW Composer Api Test");
            var asset = Song.CreateDefault(16);
            asset.title = "Field Theme";
            try
            {
                var host = go.AddComponent<UniDAWComposer>();
                host.dataFolder = folder;
                host.openLastSong = false;
                host.touchRightClick = UniDAWComposer.TouchButtonMode.Always;
                var opened = host.OpenSong(asset);
                Assert.IsNotNull(opened, "Start より前でも開ける");
                Assert.AreNotSame(asset, opened, "アセットは複製して開く");
                Assert.AreSame(opened, host.CurrentSong);
                Assert.IsNotNull(host.Touch, "タッチの右クリックボタン");
                Modified seen = new Modified();
                host.SongModified += seen.On;
                host.Composer.Session.ModifySong("BPM", s => s.bpm = 140);
                Assert.AreEqual(1, seen.count);
                Assert.AreNotEqual(140f, asset.bpm, "元のアセットは書き換えない");
                host.Save();

                var again = host.OpenSong(asset);
                Assert.AreEqual(140f, again.bpm, "同じ名前なら前の編集を残す");
                var fresh = host.OpenSong(asset, keepEdits: false);
                Assert.AreNotEqual(140f, fresh.bpm);
                Assert.AreEqual(2, host.Files.ListSongs().Count);
                Assert.IsTrue(host.OpenFile(host.Files.IdOf(again)));
                Assert.AreSame(again, host.CurrentSong);

                host.Visible = false;
                Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.None, host.Composer.style.display.value, "隠せる");
                typeof(UniDAWComposer).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(asset);
            }
        }

        sealed class Modified
        {
            public int count;
            public void On(Song _) => count++;
        }

        [Test]
        public void RuntimeExport_UsesSongNameWithoutScreen_AndPromptReturnsName()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var state = new ComposerState();
            var session = new ComposerSession(state, new Transport(state, null, p), p);
            var files = new RuntimeSongFiles(session, p, folder);
            try
            {
                files.NewSong();
                session.Song.title = "a/b";
                files.ExportMidi();
                Assert.IsTrue(File.Exists(Path.Combine(files.ExportFolder, "a_b.mid")), "画面が無ければ曲名で書き出す（区切りは使わない）");

                var root = new UnityEngine.UIElements.VisualElement();
                string got = null;
                var prompt = NamePrompt.Show(root, "Export", "name", "x", n => got = n);
                prompt.Value = "   ";
                prompt.Submit();
                Assert.IsNull(got, "空の名前では決めない");
                prompt.Value = " mysong ";
                prompt.Submit();
                Assert.AreEqual("mysong", got);
                Assert.IsNull(prompt.parent, "決めたら閉じる");
            }
            finally
            {
                files.Dispose();
            }
        }

        [Test]
        public void TwoComposers_KeepSeparateHostsAndUndo()
        {
            var pa = new RuntimeDawPlatform(null, folder);
            var pb = new RuntimeDawPlatform(null, folder);
            var a = new ComposerRoot(new ComposerState(), s => new RuntimeSongFiles(s, pa, Path.Combine(folder, "a")), pa);
            var b = new ComposerRoot(new ComposerState(), s => new RuntimeSongFiles(s, pb, Path.Combine(folder, "b")), pb);
            try
            {
                a.Files.NewSong();
                b.Files.NewSong();
                a.Session.SetCursor(0, 0, 0);
                Assert.IsTrue(a.HandleKey(KeyCode.Z, false, false));
                Assert.IsTrue(pa.CanUndo, "A の編集は A のホストに記録");
                Assert.IsFalse(pb.CanUndo, "B のホストには残らない");
                Assert.IsFalse(b.State.CurrentPattern.tracks[0].cells[0].HasNote);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [Test]
        public void MidiInput_OnlyOwnerSyncs_AndReleaseHandsOver()
        {
            var p = new RuntimeDawPlatform(null, folder);
            var sa = new ComposerSession(new ComposerState(), null, p);
            var sb = new ComposerSession(new ComposerState(), null, p);
            var prevOwner = UniDAWMidiBridge.Owner;
            var prevInst = UniDAWMidiBridge.Instruments;
            try
            {
                sa.ClaimMidiInput();
                Assert.IsTrue(sa.OwnsMidiInput);
                Assert.IsFalse(sb.OwnsMidiInput, "後から聞いた側は受け取らない");
                var aInst = UniDAWMidiBridge.Instruments;
                sb.SyncMidiInput();
                Assert.AreSame(aInst, UniDAWMidiBridge.Instruments, "持ち主でないと上書きしない");
                sa.ReleaseMidiInput();
                Assert.IsTrue(sb.OwnsMidiInput, "手放したら次に聞いた側が受け取る");
            }
            finally
            {
                UniDAWMidiBridge.Owner = prevOwner;
                UniDAWMidiBridge.Instruments = prevInst;
            }
        }
    }
}
