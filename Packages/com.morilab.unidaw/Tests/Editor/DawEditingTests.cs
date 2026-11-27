using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniDAW.Composer;
using UniDAW.Editor;
using UnityEngine;

namespace UniDAW.Tests
{
    /// <summary>DAW 準拠の操作：ピアノロールのノート編集（RollNotes / RollEditor）とトランスポートのキー。</summary>
    public class DawEditingTests
    {
        static Pattern MakePattern(params (int row, int note)[] cells)
        {
            var p = new Pattern(16, "T");
            foreach (var (row, note) in cells) p.tracks[0].cells[row] = Cell.NoteCell(note, 1);
            return p;
        }

        static string Dump(IEnumerable<RollNote> notes) => string.Join(" ", notes.Select(n => n.ToString()));

        // ---- RollNotes（純粋な変換） ---------------------------------------------------

        [Test]
        public void Extract_NoteRunsToNextNoteOffOrTrackEnd()
        {
            var p = MakePattern((0, 60), (4, 62));
            p.tracks[0].cells[6] = Cell.NoteCell(Cell.Off);
            p.tracks[0].cells[10] = Cell.NoteCell(64);
            Assert.AreEqual("60@0+4 62@4+2 64@10+6", Dump(RollNotes.Extract(p, 0)));
        }

        [Test]
        public void Write_PlacesOffAtEnd_KeepsFxOnlyCells()
        {
            var p = MakePattern();
            var fxOnly = Cell.Blank;
            fxOnly.fx1 = new Fx(FxType.Vibrato, 0x44);
            p.tracks[0].cells[3] = fxOnly;
            RollNotes.Write(p, 0, new[]
            {
                new RollNote(0, 2, 60, Cell.NoteCell(60, 1)),
                new RollNote(2, 1, 62, Cell.NoteCell(62, 1)),
                new RollNote(8, 20, 64, Cell.NoteCell(64, 1)),
            });
            var c = p.tracks[0].cells;
            Assert.AreEqual(60, c[0].note);
            Assert.AreEqual(62, c[2].note, "すぐ次のノートが始まるならオフは置かない");
            Assert.IsTrue(c[3].IsOff, "ノートの終わりにオフ");
            Assert.AreEqual(FxType.Vibrato, c[3].fx1.type, "エフェクトだけのセルは残る");
            Assert.AreEqual(64, c[8].note);
            Assert.AreEqual("60@0+2 62@2+1 64@8+8", Dump(RollNotes.Extract(p, 0)), "長さはトラック終端で切る");
        }

        [Test]
        public void Move_ClampsGroupAndOverwritesTarget()
        {
            var notes = RollNotes.Extract(MakePattern((0, 60), (4, 62), (8, 64)), 0);
            var moved = RollNotes.Move(notes, new[] { 0, 4 }, 4, 1, false, 16, 0, 127, out var sel);
            Assert.AreEqual("61@4+4 63@8+4", Dump(moved), "行 8 の既存ノートは動かした側が上書き（長さは保つ）");
            CollectionAssert.AreEquivalent(new[] { 4, 8 }, sel);

            var clamped = RollNotes.Move(notes, new[] { 8 }, 100, 0, false, 16, 0, 127, out _);
            Assert.AreEqual("60@0+4 62@4+4 64@15+1", Dump(clamped), "群ごと範囲内に収める（他ノートの長さは保つ）");
        }

        [Test]
        public void Move_CopyKeepsOriginal()
        {
            var notes = RollNotes.Extract(MakePattern((0, 60)), 0);
            var r = RollNotes.Move(notes, new[] { 0 }, 8, 0, true, 16, 0, 127, out var sel);
            Assert.AreEqual("60@0+8 60@8+8", Dump(r));
            CollectionAssert.AreEquivalent(new[] { 8 }, sel, "複製した側が選択される");
        }

        [Test]
        public void Resize_And_Duplicate()
        {
            var notes = RollNotes.Extract(MakePattern((0, 60), (4, 62)), 0);
            Assert.AreEqual("60@0+4 62@4+12", Dump(notes));
            Assert.AreEqual("60@0+2 62@4+12", Dump(RollNotes.Resize(notes, new[] { 0 }, -2, 16)));
            Assert.AreEqual("60@0+1 62@4+12", Dump(RollNotes.Resize(notes, new[] { 0 }, -9, 16)), "最短 1 行");

            var shortNotes = RollNotes.Resize(notes, new[] { 4 }, -10, 16); // 62@4+2
            var dup = RollNotes.Duplicate(shortNotes, new[] { 0, 4 }, 16, out var copies);
            Assert.AreEqual("60@0+4 62@4+2 60@6+4 62@10+2", Dump(dup), "範囲（0〜6）のすぐ後ろへ");
            CollectionAssert.AreEquivalent(new[] { 6, 10 }, copies);
        }

        // ---- ウィンドウ経由（マウス・キーと同じ入口） --------------------------------------

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
        public void Roll_CreateSelectMoveDeleteUndo_EditsTrackerCells()
        {
            WithWindow((w, song) =>
            {
                w.SetCursor(0, 1, 0);
                var cells = song.patterns[0].tracks[1].cells;
                w.Roll.Create(2, 64, 4);
                Assert.AreEqual(64, cells[2].note, "作成したノートはトラッカーのセル");
                Assert.IsTrue(cells[6].IsOff, "長さ 4 → 行 6 にオフ");
                CollectionAssert.AreEquivalent(new[] { 2 }, w.Roll.Selection, "作ったノートが選択される");

                w.Roll.MoveSelected(2, 3, false);
                Assert.IsFalse(cells[2].HasNote);
                Assert.AreEqual(67, cells[4].note);
                Assert.IsTrue(cells[8].IsOff);

                UnityEditor.Undo.IncrementCurrentGroup(); // 実際の操作はイベントごとに別グループ
                w.Roll.Delete(4);
                Assert.IsFalse(cells.Any(c => c.HasNote || c.IsOff), "ノートもオフも消える");

                UnityEditor.Undo.PerformUndo();
                Assert.AreEqual(67, cells[4].note, "Undo 1 回で 1 操作ぶん戻る");
            });
        }

        [Test]
        public void RollKeys_FollowPreset()
        {
            var prev = DawKeys.Preset;
            try
            {
                WithWindow((w, song) =>
                {
                    var cells = song.patterns[0].tracks[0].cells;
                    w.SetCursor(0, 0, 0);
                    w.State.view = PatternViewKind.PianoRoll;
                    w.Roll.Create(0, 60, 2);

                    Assert.IsFalse(w.HandleKey(KeyCode.Z, false, false), "ピアノロールでは文字キーでノート入力しない");
                    Assert.IsTrue(w.HandleKey(KeyCode.UpArrow, false, false));
                    Assert.AreEqual(61, cells[0].note, "↑ で選択ノートを半音上げる");
                    Assert.IsTrue(w.HandleKey(KeyCode.DownArrow, false, true));
                    Assert.AreEqual(49, cells[0].note, "Shift+↓ で 1 オクターブ");

                    DawKeys.Preset = DawKeyPreset.Cubase;
                    Assert.IsTrue(w.HandleKey(KeyCode.D, true, false), "Cubase: Ctrl+D で複製");
                    Assert.AreEqual(49, cells[2].note);
                    Assert.IsTrue(w.HandleKey(KeyCode.RightArrow, true, false), "Cubase: Ctrl+→ でナッジ");
                    Assert.AreEqual(49, cells[3].note);
                    Assert.IsTrue(w.HandleKey(KeyCode.Alpha8, false, false));
                    Assert.AreEqual(RollTool.Draw, w.State.rollTool, "Cubase: 8 で鉛筆");

                    DawKeys.Preset = DawKeyPreset.StudioOne;
                    Assert.IsTrue(w.HandleKey(KeyCode.Alpha5, false, false));
                    Assert.AreEqual(RollTool.Draw, w.State.rollTool, "Studio One: 5 でペイント");
                    Assert.IsTrue(w.HandleKey(KeyCode.Alpha4, false, false));
                    Assert.AreEqual(RollTool.Erase, w.State.rollTool, "Studio One: 4 で消しゴム");
                    Assert.IsTrue(w.HandleKey(KeyCode.D, false, false), "Studio One: D で複製");
                    Assert.AreEqual(49, cells[5].note);
                    Assert.IsTrue(w.HandleKey(KeyCode.D, true, false), "Studio One: Ctrl+D で選択解除");
                    Assert.AreEqual(0, w.Roll.Selection.Count);

                    Assert.IsTrue(w.HandleKey(KeyCode.A, true, false));
                    Assert.AreEqual(3, w.Roll.Selection.Count, "Ctrl+A で全選択");
                    Assert.IsTrue(w.HandleKey(KeyCode.Delete, false, false));
                    Assert.IsFalse(cells.Any(c => c.HasNote), "Del で選択ノートを削除");
                });
            }
            finally
            {
                DawKeys.Preset = prev;
            }
        }

        [Test]
        public void TransportKeys_DawNumpad()
        {
            WithWindow((w, song) =>
            {
                song.order.Add(0);
                song.order.Add(0);
                song.orderTranspose.Add(0);
                song.orderTranspose.Add(0);
                w.SetCursor(5, 0, 0);

                bool loop = w.State.loop;
                Assert.IsTrue(w.HandleKey(KeyCode.KeypadDivide, false, false));
                Assert.AreNotEqual(loop, w.State.loop, "Num / でサイクル切替");

                Assert.IsTrue(w.HandleKey(KeyCode.KeypadPlus, false, false));
                Assert.IsTrue(w.HandleKey(KeyCode.KeypadPlus, false, false));
                Assert.AreEqual(2, w.State.orderIndex, "Num + で次の順序");
                Assert.IsTrue(w.HandleKey(KeyCode.KeypadMinus, false, false));
                Assert.AreEqual(1, w.State.orderIndex);

                Assert.IsTrue(w.HandleKey(KeyCode.KeypadPeriod, false, false));
                Assert.AreEqual(0, w.State.orderIndex, "Num . で先頭へ");
                Assert.AreEqual(0, w.CursorRow);

                bool rec = w.State.recording;
                Assert.IsTrue(w.HandleKey(KeyCode.KeypadMultiply, false, false));
                Assert.AreNotEqual(rec, w.State.recording, "Num * で録音切替");

                Assert.IsTrue(w.HandleKey(KeyCode.Keypad0, false, false), "Num 0 は停止（16 進入力には使わない）");
                w.SetCursor(0, 0, 2);
                Assert.IsTrue(w.HandleKey(KeyCode.Keypad7, false, false));
                Assert.AreEqual(7, song.patterns[0].tracks[0].cells[0].volume, "Num 1〜9 は 16 進入力のまま");
            });
        }
    }

    public class ShortcutTests
    {
        /// <summary>Shortcut Manager に登録した既定キーが、キーマップのウィンドウ全体キーの一覧と一致する。</summary>
        [Test]
        public void ShortcutsMatchWindowCommands()
        {
            var type = typeof(UniDAWWindow).Assembly.GetType("UniDAW.Editor.UniDAWShortcuts");
            Assert.IsNotNull(type);
            var found = new List<(KeyCode, bool, bool)>();
            foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                foreach (var a in m.CustomAttributes)
                {
                    if (a.AttributeType != typeof(UnityEditor.ShortcutManagement.ShortcutAttribute)) continue;
                    var args = a.ConstructorArguments;
                    var key = (KeyCode)(int)args[2].Value;
                    var mods = args.Count > 3 ? (UnityEditor.ShortcutManagement.ShortcutModifiers)(int)args[3].Value : 0;
                    found.Add((key, (mods & UnityEditor.ShortcutManagement.ShortcutModifiers.Action) != 0,
                        (mods & UnityEditor.ShortcutManagement.ShortcutModifiers.Alt) != 0));
                }
            CollectionAssert.AreEquivalent(ComposerKeymap.WindowCommands, found);
            Assert.IsTrue(ComposerKeymap.IsWindowCommand(KeyCode.Space, false, false, false));
            Assert.IsFalse(ComposerKeymap.IsWindowCommand(KeyCode.Space, false, true, false), "Shift 付きは今まで通りキーマップへ");
            Assert.IsFalse(ComposerKeymap.IsWindowCommand(KeyCode.C, false, false, false), "C はノート入力");
            Assert.IsTrue(ComposerKeymap.IsWindowCommand(KeyCode.L, true, false, true));
        }
    }

    public class CellDescriptionTests
    {
        [Test]
        public void DescribesNoteInstrumentAndFx()
        {
            var song = Song.CreateDefault();
            var cell = Cell.Blank;
            Assert.IsNull(CellDescription.Describe(song, 0, cell), "空のセルは説明なし");
            cell.note = 48;
            cell.instrument = 0;
            cell.fx1 = new Fx { type = FxType.Arpeggio, param = 0x37 };
            cell.probability = 50;
            string t = CellDescription.Describe(song, 0, cell, 4);
            StringAssert.Contains("長さ 4 行", t);
            StringAssert.Contains("楽器 00 " + song.GetInstrument(0).name, t);
            StringAssert.Contains(FxText.Format(cell.fx1) + " = ", t);
            StringAssert.Contains("50%", t);
        }
    }
}
