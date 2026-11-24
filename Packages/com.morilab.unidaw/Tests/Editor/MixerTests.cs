using System;
using NUnit.Framework;
using UniDAW.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniDAW.Tests
{
    /// <summary>ミキサー（曲に保存される音量・パン・ミュート/ソロ）、ステレオ出力、ピッチマクロ、楽器エディタの操作。</summary>
    public class MixerTests
    {
        const int Rate = 48000;
        const int Frames = 9600;

        static Song MakeSong()
        {
            var s = Song.CreateDefault(16);
            s.bpm = 150;
            s.speed = 6;
            s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstBass);
            s.patterns[0].tracks[2].cells[0] = Cell.NoteCell(48, Song.InstBass);
            return s;
        }

        /// <summary>パターンを鳴らして L/R の RMS を返す（冒頭の立ち上がりは捨てる）。</summary>
        static (double l, double r) RenderLR(Song s, int outChannels = 2)
        {
            var e = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            e.PlayPattern(0);
            var buf = new float[Frames * outChannels];
            e.Render(buf, 0, Frames, outChannels);
            e.Render(buf, 0, Frames, outChannels);
            double l = 0, r = 0;
            for (int i = 0; i < Frames; i++)
            {
                float a = buf[i * outChannels], b = buf[i * outChannels + (outChannels > 1 ? 1 : 0)];
                l += a * a;
                r += b * b;
            }
            return (Math.Sqrt(l / Frames), Math.Sqrt(r / Frames));
        }

        static void Only(Song s, int ch)
        {
            for (int c = 0; c < Song.ChannelCount; c++)
                if (c != ch) s.patterns[0].tracks[c].cells[0] = Cell.Blank;
        }

        [Test]
        public void CenterPan_StereoMatchesMono()
        {
            var s = MakeSong();
            var (l, r) = RenderLR(s);
            var (m, _) = RenderLR(s, 1);
            Assert.Greater(l, 0.01);
            Assert.AreEqual(l, r, 1e-6, "センターは左右同じ");
            Assert.AreEqual(l, m, 1e-6, "モノラルに畳んでも音量が変わらない");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Pan_MovesChannelToOneSide()
        {
            var s = MakeSong();
            Only(s, 0);
            s.mixer[0].pan = -1f;
            var (l, r) = RenderLR(s);
            Assert.Greater(l, 0.01);
            Assert.Less(r, 1e-4, "左いっぱい → 右は無音");

            s.mixer[0].pan = 0.5f;
            var (l2, r2) = RenderLR(s);
            Assert.AreEqual(0.5, l2 / r2, 0.02, "R32 → 左は半分、右はそのまま");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Volume_Mute_Solo_FromSongMixer()
        {
            var s = MakeSong();
            Only(s, 0);
            var (full, _) = RenderLR(s);
            s.mixer[0].volumeDb = -6.0206f;
            Assert.AreEqual(0.5, RenderLR(s).l / full, 0.02, "-6 dB で半分");
            s.mixer[0].volumeDb = ChannelMix.MinDb;
            Assert.Less(RenderLR(s).l, 1e-4, "-∞ で無音");
            s.mixer[0].volumeDb = 0;

            s.mixer[0].mute = true;
            Assert.Less(RenderLR(s).l, 1e-4, "ミュート");
            s.mixer[0].mute = false;

            s.mixer[1].solo = true;
            Assert.Less(RenderLR(s).l, 1e-4, "他チャンネルのソロで黙る");
            s.mixer[0].solo = true;
            Assert.AreEqual(full, RenderLR(s).l, 1e-6, "自分もソロなら鳴る");

            s.masterDb = -6.0206f;
            Assert.AreEqual(0.5, RenderLR(s).l / full, 0.02, "マスター");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void EngineMuteSolo_StillOverridesForGames()
        {
            var s = MakeSong();
            Only(s, 0);
            var e = new ChipEngine(Rate) { Song = s, EmitNoteEvents = false };
            e.Solo[3] = true;
            e.PlayPattern(0);
            var buf = new float[Frames * 2];
            e.Render(buf, 0, Frames, 2);
            Assert.Less(Mathf.Max(e.Levels[0], e.OutputLevels[0]), 1e-4, "ゲーム側のソロも効く");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void StereoRender_IsInterleaved()
        {
            var s = MakeSong();
            Only(s, 0);
            s.mixer[0].pan = 1f;
            var st = ChipEngine.RenderSongStereo(s, Rate, 2);
            var mono = ChipEngine.RenderSong(s, Rate, 2);
            Assert.AreEqual(mono.Length * 2, st.Length);
            double l = 0, r = 0;
            for (int i = 0; i + 1 < st.Length; i += 2)
            {
                l += Math.Abs(st[i]);
                r += Math.Abs(st[i + 1]);
            }
            Assert.Less(l, r * 1e-3, "右いっぱい");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void PitchMacro_AccumulatesSixteenthsOfSemitone()
        {
            var s = MakeSong();
            s.instruments.Add(new Instrument("Up", "15", "", "2", "8 8 0"));
            int idx = s.instruments.Count - 1;
            var e = new ChipEngine(Rate) { Song = s };
            e.NoteOn(0, 60, 15, idx);
            e.Render(new float[Rate / 5], 0, Rate / 5, 1);
            Assert.AreEqual(61f, e.GetChannelPitch(0), 0.01f, "8/16 + 8/16 = 1 半音上がって止まる");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void Validate_MigratesOldSongs()
        {
            var s = MakeSong();
            s.mixer = null;
            s.masterDb = 99f;
            s.instruments[0].pitch = null;
            s.Validate();
            Assert.AreEqual(Song.ChannelCount, s.mixer.Count);
            Assert.AreEqual(ChannelMix.MaxDb, s.masterDb);
            Assert.IsNotNull(s.instruments[0].pitch);
            Assert.AreEqual("-∞", ChannelMix.FormatDb(ChannelMix.MinDb));
            Assert.AreEqual("L32", ChannelMix.FormatPan(-0.5f));
            Object.DestroyImmediate(s);
        }

        // ---- ウィンドウ経由 --------------------------------------------------------------

        static void WithWindow(Action<UniDAWWindow, Song> body)
        {
            var song = MakeSong();
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
        public void MixerCommands_MuteSoloAndFaderUndo()
        {
            WithWindow((w, song) =>
            {
                var m = w.Mixer;
                m.ToggleMute(1);
                Assert.IsTrue(song.mixer[1].mute);
                m.ToggleSolo(2);
                Assert.IsTrue(m.IsSilenced(0), "ソロ中は他チャンネルが黙る");
                m.ExclusiveSolo(0);
                Assert.IsTrue(song.mixer[0].solo);
                Assert.IsFalse(song.mixer[2].solo, "Ctrl+クリックは単独ソロ");
                m.ClearMuteSolo();
                Assert.IsFalse(song.mixer[1].mute || song.mixer[0].solo);

                UnityEditor.Undo.IncrementCurrentGroup();
                m.PreviewVolume(0, -3f);
                m.PreviewVolume(0, -10f);
                Assert.AreEqual(-10f, song.mixer[0].volumeDb, "ドラッグ中はライブ");
                m.CommitVolume(0, 0f, -10f);
                Assert.AreEqual(-10f, song.mixer[0].volumeDb);
                UnityEditor.Undo.PerformUndo();
                Assert.AreEqual(0f, song.mixer[0].volumeDb, "Undo 1 回でドラッグ前へ");

                UnityEditor.Undo.IncrementCurrentGroup();
                m.CommitPan(3, 0f, 2f);
                Assert.AreEqual(1f, song.mixer[3].pan, "範囲に収める");
            });
        }

        [Test]
        public void LowerZoneKeys_AndMacroEdit()
        {
            WithWindow((w, song) =>
            {
                var st = w.State;
                st.lowerOpen = false;
                Assert.IsTrue(w.HandleKey(KeyCode.F3, false, false));
                Assert.IsTrue(st.lowerOpen);
                Assert.AreEqual(LowerTab.Mixer, st.lowerTab, "F3 でミキサー");
                Assert.IsTrue(w.HandleKey(KeyCode.F3, false, false));
                Assert.IsFalse(st.lowerOpen, "もう一度 F3 で閉じる");
                Assert.IsTrue(w.HandleKey(KeyCode.E, true, false, true));
                Assert.IsTrue(st.lowerOpen, "Ctrl+Alt+E で下ゾーン");

                w.SetCursor(0, 0, 0);
                var inst = w.Session.CursorInstrument;
                UnityEditor.Undo.IncrementCurrentGroup();
                w.Session.SetMacro(MacroKind.Pitch, Sequence.Parse("| 1 -1"));
                Assert.AreEqual("| 1 -1", inst.pitch.ToString());
                UnityEditor.Undo.PerformUndo();
                Assert.IsTrue(w.Session.CursorInstrument.pitch.IsEmpty, "マクロ編集も Undo できる");
            });
        }
    }
}
