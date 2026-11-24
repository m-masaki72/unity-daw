using System;
using System.Collections.Generic;
using System.Linq;
using UniDAW.Midi;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Tests
{
    public class SmfTests
    {
        [Test]
        public void WriteRead_RoundTrip()
        {
            var t = new SmfWriter.TrackData { name = "Lead" };
            t.notes.Add(new MidiNote { channel = 0, note = 60, velocity = 100, startTick = 0, endTick = 480 });
            t.notes.Add(new MidiNote { channel = 0, note = 64, velocity = 90, startTick = 480, endTick = 960 });
            t.notes.Add(new MidiNote { channel = 0, note = 67, velocity = 80, startTick = 960, endTick = 1920 });
            var bytes = SmfWriter.Write(new[] { t }, 128, 480);

            var data = SmfReader.Read(bytes);
            Assert.AreEqual(480, data.division);
            Assert.AreEqual(128.0, data.InitialBpm, 0.01);
            var ns = data.notes.OrderBy(n => n.startTick).ToList();
            Assert.AreEqual(3, ns.Count);
            Assert.AreEqual(new[] { 60, 64, 67 }, ns.Select(n => n.note).ToArray());
            Assert.AreEqual(new long[] { 0, 480, 960 }, ns.Select(n => n.startTick).ToArray());
            Assert.AreEqual(new long[] { 480, 960, 1920 }, ns.Select(n => n.endTick).ToArray());
            Assert.Contains("Lead", data.trackNames);
        }

        [Test]
        public void Read_RunningStatus_AndVelocityZeroNoteOff()
        {
            var b = new List<byte>();
            b.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0, 96 });
            var trk = new byte[]
            {
                0x00, 0x90, 0x3C, 0x64, // on 60 @0
                0x60, 0x3E, 0x64,       // (running) on 62 @96
                0x00, 0x3C, 0x00,       // (running) off 60 @96
                0x60, 0x3E, 0x00,       // (running) off 62 @192
                0x00, 0xFF, 0x2F, 0x00,
            };
            b.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, (byte)trk.Length });
            b.AddRange(trk);

            var data = SmfReader.Read(b.ToArray());
            var ns = data.notes.OrderBy(n => n.startTick).ToList();
            Assert.AreEqual(2, ns.Count);
            Assert.AreEqual(60, ns[0].note);
            Assert.AreEqual(0, ns[0].startTick);
            Assert.AreEqual(96, ns[0].endTick);
            Assert.AreEqual(62, ns[1].note);
            Assert.AreEqual(96, ns[1].startTick);
            Assert.AreEqual(192, ns[1].endTick);
            Assert.AreEqual(120.0, data.InitialBpm, 0.01);
        }
    }

    public class MidiImportTests
    {
        const int Q = 480;

        static byte[] BuildQuartet()
        {
            var lead = new SmfWriter.TrackData { name = "Lead" };
            var mid = new SmfWriter.TrackData { name = "Mid" };
            var bass = new SmfWriter.TrackData { name = "Bass" };
            var drums = new SmfWriter.TrackData { name = "Drums" };
            // 8 小節（= 128 行 @ rpb4）
            for (int beat = 0; beat < 32; beat++)
            {
                long t = beat * (long)Q;
                if (beat % 2 == 0) // 半拍だけ鳴らして休む → ノートオフが入るはず
                    lead.notes.Add(new MidiNote { channel = 0, note = 76 + (beat / 2) % 5, velocity = 100, startTick = t, endTick = t + Q / 2 });
                mid.notes.Add(new MidiNote { channel = 1, note = 64, velocity = 80, startTick = t, endTick = t + Q - 10 });
                bass.notes.Add(new MidiNote { channel = 2, note = 36 + beat % 3, velocity = 110, startTick = t, endTick = t + Q - 10 });
                drums.notes.Add(new MidiNote { channel = 9, note = beat % 2 == 0 ? 36 : 38, velocity = 120, startTick = t, endTick = t + 60 });
            }
            return SmfWriter.Write(new[] { lead, mid, bass, drums }, 140, Q);
        }

        [Test]
        public void Import_AssignsVoices()
        {
            var song = MidiToSong.Import(BuildQuartet(), new MidiImportOptions(), "t", out var report);
            try
            {
                Assert.AreEqual(VoiceAssignMode.ByTrack, report.usedMode);
                Assert.AreEqual(140f, song.bpm, 0.1f);
                var p0 = song.GetPattern(song.order[0]);

                // リード → Pulse1
                Assert.AreEqual(76, p0.tracks[0].cells[0].note);
                // ベース → Triangle
                Assert.AreEqual(36, p0.tracks[2].cells[0].note);
                Assert.AreEqual(Song.InstBass, p0.tracks[2].cells[0].instrument);
                // ドラム → Noise（キック）
                var kick = DrumMap.Map(36);
                Assert.AreEqual(kick.pitch, p0.tracks[3].cells[0].note);
                Assert.AreEqual(Song.InstKick, p0.tracks[3].cells[0].instrument);
                var snare = DrumMap.Map(38);
                Assert.AreEqual(snare.pitch, p0.tracks[3].cells[4].note);
            }
            finally { UnityEngine.Object.DestroyImmediate(song); }
        }

        [Test]
        public void Import_InsertsNoteOff()
        {
            var song = MidiToSong.Import(BuildQuartet(), new MidiImportOptions(), "t", out _);
            try
            {
                var cells = song.GetPattern(song.order[0]).tracks[0].cells;
                // 行0で発音、半拍(2行)で終了、行8 で次の音
                Assert.IsTrue(cells[0].HasNote);
                bool off = false;
                for (int r = 1; r < 8; r++) off |= cells[r].IsOff;
                Assert.IsTrue(off, "ノートオフが入っていない");
                Assert.IsTrue(cells[8].HasNote);
            }
            finally { UnityEngine.Object.DestroyImmediate(song); }
        }

        [Test]
        public void Import_SplitsIntoPatterns()
        {
            var song = MidiToSong.Import(BuildQuartet(), new MidiImportOptions { rowsPerPattern = 32 }, "t", out var report);
            try
            {
                Assert.AreEqual(4, song.order.Count);
                Assert.AreEqual(4, report.orderLength);
                foreach (var p in song.patterns) Assert.AreEqual(32, p.length);
            }
            finally { UnityEngine.Object.DestroyImmediate(song); }
        }

        [Test]
        public void SongToMidi_RoundTrip()
        {
            var song = Song.CreateDefault(16);
            var p = song.patterns[0];
            int[] leadRows = { 0, 4, 8, 12 };
            int[] leadNotes = { 72, 74, 76, 79 };
            for (int i = 0; i < 4; i++) p.tracks[0].cells[leadRows[i]] = Cell.NoteCell(leadNotes[i], Song.InstLead);
            p.tracks[0].cells[2] = Cell.NoteCell(Cell.Off);
            for (int r = 0; r < 16; r += 2) p.tracks[2].cells[r] = Cell.NoteCell(40, Song.InstBass);
            p.tracks[3].cells[0] = Cell.NoteCell(DrumMap.Map(36).pitch, Song.InstKick);
            p.tracks[3].cells[8] = Cell.NoteCell(DrumMap.Map(38).pitch, Song.InstSnare);

            var bytes = SongToMidi.Export(song);
            var back = MidiToSong.Import(bytes, new MidiImportOptions { mode = VoiceAssignMode.ByTrack, rowsPerPattern = 16 }, "rt", out _);
            try
            {
                var q = back.GetPattern(back.order[0]);
                for (int i = 0; i < 4; i++) Assert.AreEqual(leadNotes[i], q.tracks[0].cells[leadRows[i]].note, $"lead row {leadRows[i]}");
                Assert.IsTrue(q.tracks[0].cells[2].IsOff, "note-off が往復で失われた");
                for (int r = 0; r < 16; r += 2) Assert.AreEqual(40, q.tracks[2].cells[r].note, $"bass row {r}");
                Assert.AreEqual(DrumMap.Map(36).pitch, q.tracks[3].cells[0].note);
                Assert.AreEqual(DrumMap.Map(38).pitch, q.tracks[3].cells[8].note);
                Assert.AreEqual(song.bpm, back.bpm, 0.1f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(song);
                UnityEngine.Object.DestroyImmediate(back);
            }
        }

        [Test]
        public void DemoMidi_Imports()
        {
            var song = MidiToSong.Import(DemoSongs.BuildDemoMidi(), new MidiImportOptions(), "demo", out var report);
            try
            {
                Assert.Greater(report.sourceNotes, 100);
                Assert.Greater(report.placedNotes.Sum(), report.sourceNotes / 3);
                for (int c = 0; c < Song.ChannelCount; c++) Assert.Greater(report.placedNotes[c], 0, $"ch{c} が空");
            }
            finally { UnityEngine.Object.DestroyImmediate(song); }
        }
    }

    public class EngineTests
    {
        const int Rate = 48000;

        static Song MakeSong(int length = 16)
        {
            var s = Song.CreateDefault(length);
            s.bpm = 150;
            s.rowsPerBeat = 4;
            s.speed = 6;
            return s;
        }

        static List<NoteEvent> Drain(ChipEngine e)
        {
            var l = new List<NoteEvent>();
            while (e.NoteEvents.TryDequeue(out var n)) l.Add(n);
            return l;
        }

        static float[] Render(ChipEngine e, int frames)
        {
            var buf = new float[frames];
            e.Render(buf, 0, frames, 1);
            return buf;
        }

        [Test]
        public void Pulse_FrequencyMatchesNote()
        {
            var s = MakeSong();
            var e = new ChipEngine(Rate) { Song = s };
            e.NoteOn(0, 69, 15, Song.InstLead); // A4
            Render(e, Rate / 10);
            var buf = Render(e, Rate);
            int crossings = 0;
            for (int i = 1; i < buf.Length; i++)
                if ((buf[i - 1] < 0) != (buf[i] < 0)) crossings++;
            Assert.AreEqual(880, crossings, 880 * 0.03);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Noise_IsDeterministic()
        {
            var s = MakeSong();
            float[] Run()
            {
                var e = new ChipEngine(Rate) { Song = s };
                e.NoteOn(3, 9, 15, Song.InstSnare);
                return Render(e, 4800);
            }
            var a = Run();
            var b = Run();
            Assert.AreEqual(a, b);
            Assert.Greater(a.Max(Math.Abs), 0.01f);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Stopped_IsSilent()
        {
            var s = MakeSong();
            s.patterns[0].tracks[0].cells[0] = Cell.NoteCell(60, Song.InstLead);
            var e = new ChipEngine(Rate) { Song = s };
            var buf = Render(e, 9600);
            Assert.AreEqual(0f, buf.Max(Math.Abs));
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void RowTiming_4800SamplesPerRow()
        {
            var s = MakeSong();
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, 4800);
            Assert.AreEqual(0, e.PlayingRow);
            Render(e, 1);
            Assert.AreEqual(1, e.PlayingRow);
            Render(e, 4800 * 9);
            Assert.AreEqual(10, e.PlayingRow);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Arpeggio_CyclesThroughOffsets()
        {
            var s = MakeSong();
            s.instruments[Song.InstBass] = new Instrument("Plain", "15", "", "");
            var c = Cell.NoteCell(60, Song.InstBass);
            c.fx1 = new Fx(FxType.Arpeggio, 0x47);
            s.patterns[0].tracks[2].cells[0] = c;
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            var pitches = new List<float>();
            for (int t = 0; t < 6; t++)
            {
                Render(e, 800);
                pitches.Add(e.GetChannelPitch(2));
            }
            var rounded = pitches.Select(p => Mathf.RoundToInt(p)).ToArray();
            CollectionAssert.AreEqual(new[] { 60, 64, 67, 60, 64, 67 }, rounded);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Probability_ZeroAndHundred()
        {
            var s = MakeSong();
            for (int r = 0; r < 16; r++)
            {
                var a = Cell.NoteCell(60, Song.InstLead);
                a.probability = 0;
                s.patterns[0].tracks[0].cells[r] = a;
                s.patterns[0].tracks[1].cells[r] = Cell.NoteCell(60, Song.InstLead); // 100
            }
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, 4800 * 16);
            var ev = Drain(e);
            Assert.AreEqual(0, ev.Count(n => n.channel == 0));
            Assert.AreEqual(16, ev.Count(n => n.channel == 1));
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Condition_OneOfTwo()
        {
            var s = MakeSong(4);
            var c = Cell.NoteCell(60, Song.InstLead);
            c.condA = 1;
            c.condB = 2;
            s.patterns[0].tracks[0].cells[0] = c;
            var c2 = Cell.NoteCell(62, Song.InstLead);
            c2.condA = 2;
            c2.condB = 2;
            s.patterns[0].tracks[1].cells[0] = c2;
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, 4800 * 4 * 4); // 4 周
            var ev = Drain(e);
            Assert.AreEqual(2, ev.Count(n => n.channel == 0), "1:2 は 1, 3 周目");
            Assert.AreEqual(2, ev.Count(n => n.channel == 1), "2:2 は 2, 4 周目");
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Polymeter_TrackLoopsIndependently()
        {
            var s = MakeSong(8);
            var p = s.patterns[0];
            p.tracks[0].length = 3;
            p.tracks[0].cells[0] = Cell.NoteCell(60, Song.InstLead);
            p.tracks[1].cells[0] = Cell.NoteCell(60, Song.InstLead);
            var e = new ChipEngine(Rate) { Song = s };
            e.PlayPattern(0);
            Render(e, 4800 * 8);
            var ev = Drain(e);
            CollectionAssert.AreEqual(new[] { 0, 3, 6 }, ev.Where(n => n.channel == 0).Select(n => n.row).ToArray());
            CollectionAssert.AreEqual(new[] { 0 }, ev.Where(n => n.channel == 1).Select(n => n.row).ToArray());

            Render(e, 4800 * 8); // 2 周目：絶対行 8..15 → セル 0 は 9, 12, 15 → 行 1, 4, 7
            ev = Drain(e);
            CollectionAssert.AreEqual(new[] { 1, 4, 7 }, ev.Where(n => n.channel == 0).Select(n => n.row).ToArray());
            Assert.AreEqual(1, p.CellIndex(0, 7, 0));
            Assert.AreEqual(0, p.CellIndex(0, 1, 1));
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void DemoSong_RendersAudio()
        {
            var s = DemoSongs.BuildDemoSong();
            var mono = ChipEngine.RenderSong(s, Rate);
            Assert.Greater(mono.Length, Rate * 5);
            double sum = 0;
            foreach (var v in mono) sum += v * v;
            Assert.Greater(Math.Sqrt(sum / mono.Length), 0.01);
            Assert.LessOrEqual(mono.Max(Math.Abs), 1f);
            UnityEngine.Object.DestroyImmediate(s);
        }
    }

    public class AssetTests
    {
        [Test]
        public void SavedDemoAsset_RendersAudio()
        {
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Song>(UniDAW.Editor.UniDAWAssets.DemoSongPath);
            if (s == null) Assert.Ignore("デモアセット未生成");
            var mono = ChipEngine.RenderSong(s, 48000);
            Assert.Greater(mono.Length, 48000 * 5, $"order={s.order.Count} patterns={s.patterns.Count} len0={s.patterns[0].length} bpm={s.bpm}");
        }
    }

    public class NotationTests
    {
        [Test]
        public void Note_RoundTrip()
        {
            for (int n = NoteText.MinNote; n <= NoteText.MaxNote; n++)
            {
                var s = NoteText.Format(n, ChannelKind.Pulse);
                Assert.IsTrue(NoteText.TryParse(s, ChannelKind.Pulse, out int back), s);
                Assert.AreEqual(n, back, s);
            }
            for (int n = 0; n < 16; n++)
            {
                var s = NoteText.Format(n, ChannelKind.Noise);
                Assert.IsTrue(NoteText.TryParse(s, ChannelKind.Noise, out int back), s);
                Assert.AreEqual(n, back, s);
            }
            Assert.IsTrue(NoteText.TryParse("---", ChannelKind.Pulse, out int e));
            Assert.AreEqual(Cell.Empty, e);
            Assert.IsTrue(NoteText.TryParse("===", ChannelKind.Pulse, out int o));
            Assert.AreEqual(Cell.Off, o);
            Assert.AreEqual("C-4", NoteText.Format(60, ChannelKind.Pulse));
            Assert.IsFalse(NoteText.TryParse("H-4", ChannelKind.Pulse, out _));
        }

        [Test]
        public void Fx_RoundTrip()
        {
            foreach (FxType t in Enum.GetValues(typeof(FxType)))
            {
                if (t == FxType.None) continue;
                foreach (int prm in new[] { 0x00, 0x47, 0xFF })
                {
                    var fx = new Fx(t, prm);
                    var s = FxText.Format(fx);
                    Assert.IsTrue(FxText.TryParse(s, out var back), s);
                    Assert.AreEqual(t, back.type, s);
                    Assert.AreEqual(prm, back.param, s);
                    Assert.AreEqual(t, FxText.TypeFromChar(FxText.TypeChar(t)));
                }
            }
            Assert.IsTrue(FxText.TryParse("...", out var none));
            Assert.IsTrue(none.IsEmpty);
        }

        [Test]
        public void Sequence_Parse()
        {
            var q = Sequence.Parse("15 14 | 12 / 8 0");
            CollectionAssert.AreEqual(new[] { 15, 14, 12, 8, 0 }, q.values);
            Assert.AreEqual(2, q.loop);
            Assert.AreEqual(3, q.release);
            var q2 = Sequence.Parse(q.ToString());
            CollectionAssert.AreEqual(q.values, q2.values);
            Assert.AreEqual(q.loop, q2.loop);
            Assert.AreEqual(q.release, q2.release);
        }

        [Test]
        public void Pattern_ResizeAndClone()
        {
            var p = new Pattern(16, "A");
            p.tracks[0].cells[3] = Cell.NoteCell(60);
            var c = p.Clone();
            c.tracks[0].cells[3] = Cell.NoteCell(61);
            Assert.AreEqual(60, p.tracks[0].cells[3].note);
            p.Resize(32);
            Assert.AreEqual(32, p.tracks[0].cells.Length);
            Assert.AreEqual(60, p.tracks[0].cells[3].note);
            Assert.AreEqual(Cell.Empty, p.tracks[0].cells[20].note);
        }
    }

    public class EditorWindowTests
    {
        [Test]
        public void CreateGUI_BuildsBothViews()
        {
            var song = DemoSongs.BuildDemoSong();
            var w = ScriptableObject.CreateInstance<UniDAW.Editor.UniDAWWindow>();
            try
            {
                w.CreateGUI();
                w.SetSong(song);
                var root = w.rootVisualElement;
                Assert.IsNotNull(root.Q<UniDAW.Editor.TrackerView>());
                Assert.IsNotNull(root.Q<UniDAW.Editor.StepView>());
                Assert.Greater(root.Query(className: "cd-trow").ToList().Count, 32);
                w.HandleKey(KeyCode.DownArrow, false, false);
                w.HandleKey(KeyCode.RightArrow, false, false);
                Assert.AreEqual(1, w.CursorRow);
                Assert.AreEqual(1, w.CursorCol);
                w.SetTrackLength(0, 12);
                Assert.AreEqual(12, w.CurrentPattern.TrackLength(0));
                w.StepClick(3, 13, false);
                root.Q<UniDAW.Editor.StepView>().Refresh();
                Assert.Greater(root.Query(className: "cd-step").ToList().Count, 4 * 31);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(w);
                UnityEngine.Object.DestroyImmediate(song);
            }
        }

        [Test]
        public void HandleKey_EntersNotesAndOff()
        {
            var song = Song.CreateDefault(16);
            var w = ScriptableObject.CreateInstance<UniDAW.Editor.UniDAWWindow>();
            try
            {
                w.SetSong(song);
                w.SetCursor(0, 0, 0);
                Assert.IsTrue(w.HandleKey(KeyCode.Z, false, false)); // C-4（oct 4）
                Assert.AreEqual(60, song.patterns[0].tracks[0].cells[0].note);
                Assert.AreEqual(1, w.CursorRow);
                Assert.IsTrue(w.HandleKey(KeyCode.Alpha1, false, false));
                Assert.AreEqual(Cell.Off, song.patterns[0].tracks[0].cells[1].note);
                w.SetCursor(0, 0, 0);
                Assert.IsTrue(w.HandleKey(KeyCode.UpArrow, true, false));
                Assert.AreEqual(61, song.patterns[0].tracks[0].cells[0].note);
                w.SetCursor(0, 0, 2);
                Assert.IsTrue(w.HandleKey(KeyCode.A, false, false));
                Assert.AreEqual(10, song.patterns[0].tracks[0].cells[0].volume);

                w.StepClick(1, 5, true);
                Assert.IsTrue(song.patterns[0].tracks[1].cells[5].HasNote);
                w.StepClick(1, 5, true);
                Assert.IsFalse(song.patterns[0].tracks[1].cells[5].HasNote);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(w);
                UnityEngine.Object.DestroyImmediate(song);
            }
        }
    
        // ---- 壊れた / 悪意のある MIDI --------------------------------------------------

        static byte[] Smf(int division, params byte[] track)
        {
            var b = new List<byte> { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, (byte)(division >> 8), (byte)division };
            b.AddRange(new[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            int n = track.Length;
            b.AddRange(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });
            b.AddRange(track);
            return b.ToArray();
        }

        [Test]
        public void Smf_RejectsZeroDivisionAndBadHeader()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => SmfReader.Read(Smf(0, 0, 0x90, 60, 100)));
            var bad = Smf(96, 0, 0x90, 60, 100);
            bad[4] = 0x7F; // ヘッダ長がファイルより長い
            Assert.Throws<System.IO.InvalidDataException>(() => SmfReader.Read(bad));
        }

        [Test]
        public void Smf_TruncatedDataEndsTrackQuietly()
        {
            // ノートオン → 途中で切れたノートオフ / メタ / SysEx
            foreach (var tail in new[] { new byte[] { 10, 0x80, 60 }, new byte[] { 10, 0xFF, 0x03, 50, 65 }, new byte[] { 10, 0xF0, 0x7F } })
            {
                var track = new List<byte> { 0, 0x90, 60, 100 };
                track.AddRange(tail);
                var d = SmfReader.Read(Smf(96, track.ToArray()));
                Assert.AreEqual(1, d.notes.Count);
            }
            // チャンクの長さが実データより長い
            var b = Smf(96, 0, 0x90, 60, 100, 8, 0x80, 60, 0);
            b[21] = 0xFF;
            Assert.AreEqual(1, SmfReader.Read(b).notes.Count);
        }

        [Test]
        public void MidiImport_HugeTimeIsCapped()
        {
            // 0x0FFFFFFF tick 後のノート × 数個（分解能 1）→ 巨大な配列を確保しない
            var track = new List<byte> { 0, 0x90, 60, 100, 1, 0x80, 60, 0 };
            for (int i = 0; i < 4; i++) track.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 0x90, 64, 100, 1, 0x80, 64, 0 });
            var song = MidiToSong.Import(Smf(1, track.ToArray()), new MidiImportOptions { rowsPerBeat = 16 }, "huge", out var report);
            Assert.LessOrEqual(song.order.Count * 64, MidiToSong.MaxImportRows);
            Assert.AreEqual(4, report.droppedNotes);
            Assert.AreEqual(60, song.patterns[0].tracks[0].cells[0].note);
            UnityEngine.Object.DestroyImmediate(song);
        }
    }
}
