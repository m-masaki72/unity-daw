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
            DawPlatform.Current = p;
            var state = new ComposerState();
            var session = new ComposerSession(state, new Transport(state));
            var files = new RuntimeSongFiles(session, p, folder);
            p.Dirtied += files.MarkDirty;

            files.NewSong();
            Assert.IsNotNull(session.Song);
            var id = files.IdOf(session.Song);
            Assert.IsTrue(File.Exists(id));
            Assert.AreEqual(1, files.ListSongs().Count);

            session.Song.bpm = 123;
            p.MarkDirty(session.Song);
            files.SaveAll();
            var back = RuntimeSongFiles.Read(id);
            Assert.AreEqual(123f, back.bpm);
            Assert.AreEqual(session.Song.patterns.Count, back.patterns.Count);
            Object.DestroyImmediate(back);

            files.MakeEditableCopy();
            Assert.AreEqual(2, files.ListSongs().Count);
            Assert.AreNotEqual(id, files.IdOf(session.Song));
        }

        [Test]
        public void ComposerRoot_BuildsAndEditsWithoutEditorWindow()
        {
            var p = new RuntimeDawPlatform(null, folder);
            DawPlatform.Current = p;
            var root = new ComposerRoot(new ComposerState(), s => new RuntimeSongFiles(s, p, folder));
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

                Assert.AreSame(host.Platform, DawPlatform.Current);
                var doc = go.GetComponent<UnityEngine.UIElements.UIDocument>();
                Assert.IsNotNull(doc.panelSettings);
                Assert.AreSame(doc.rootVisualElement, host.Composer.parent);
                host.Files.NewSong();
                Assert.IsNotNull(host.Composer.Session.Song);
                Assert.DoesNotThrow(() => host.Files.OpenSong(), "曲の一覧メニューを出せる");
                // 編集モードでは OnDestroy が呼ばれないので明示的に呼ぶ
                typeof(UniDAWComposer).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                Assert.IsNull(host.Composer.parent, "画面を外す");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Assert.AreSame(previous, DawPlatform.Current, "破棄でもとのホストに戻る");
        }
    }
}
