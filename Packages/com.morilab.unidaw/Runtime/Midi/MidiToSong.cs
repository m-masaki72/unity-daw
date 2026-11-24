using System;
using System.Collections.Generic;
using System.Linq;

namespace UniDAW.Midi
{
    public enum VoiceAssignMode
    {
        /// <summary>トラック/チャンネル数を見て自動選択</summary>
        Auto,
        /// <summary>MIDI トラック単位で割り当て（最高音域→Pulse1, 最低音域→Triangle）</summary>
        ByTrack,
        /// <summary>全旋律ノートを行ごとに音高で振り分け（ピアノ1トラック等向け）</summary>
        SplitByPitch,
    }

    public sealed class MidiImportOptions
    {
        public int rowsPerBeat = 4;
        public int rowsPerPattern = 64;
        public int speed = 6;
        public VoiceAssignMode mode = VoiceAssignMode.Auto;
        public bool includeDrums = true;
        public bool velocityToVolume = true;
        public bool dedupePatterns = true;
        public int transpose = 0;
    }

    public sealed class MidiImportReport
    {
        public int sourceNotes;
        public int[] placedNotes = new int[Song.ChannelCount];
        public int droppedNotes;
        public int patterns;
        public int orderLength;
        public VoiceAssignMode usedMode;
        public string[] channelSources = new string[Song.ChannelCount];

        public override string ToString() =>
            $"mode={usedMode} source={sourceNotes} placed=[{string.Join(",", placedNotes)}] dropped={droppedNotes} " +
            $"patterns={patterns} order={orderLength} sources=[{string.Join(" | ", channelSources)}]";
    }

    /// <summary>MIDI → 4ch チップ曲 変換（ポリフォニーを単音へ落とし込む）。</summary>
    public static class MidiToSong
    {
        /// <summary>取り込む長さの上限（行）。これより後ろのノートは捨てる（壊れたファイルでの巨大な確保を防ぐ）。</summary>
        public const int MaxImportRows = Pattern.MaxRows * 256;

        struct RowNote
        {
            public int row;
            public int endRow;
            public int note;
            public int vol;
            public int instrument;
            public int priority;
            public bool drum;
        }

        public static Song Import(byte[] smf, MidiImportOptions opt, string title, out MidiImportReport report)
        {
            var data = SmfReader.Read(smf);
            return Import(data, opt, title, out report);
        }

        public static Song Import(MidiFileData data, MidiImportOptions opt, string title, out MidiImportReport report)
        {
            opt = opt ?? new MidiImportOptions();
            report = new MidiImportReport { sourceNotes = data.notes.Count };
            if (data.division <= 0) throw new ArgumentException("MIDI の分解能 (division) が不正です");
            int rowsPerBeat = Math.Max(1, Math.Min(16, opt.rowsPerBeat));
            var song = Song.CreateDefault(opt.rowsPerPattern);
            song.title = title;
            song.name = title;
            song.bpm = (float)Math.Round(Math.Max(20, Math.Min(400, data.InitialBpm)), 2);
            song.rowsPerBeat = rowsPerBeat;
            song.speed = opt.speed;

            double ticksPerRow = data.division / (double)rowsPerBeat;
            RowNote ToRow(MidiNote n, int instrument)
            {
                int r = (int)Math.Min(int.MaxValue / 2, Math.Round(n.startTick / ticksPerRow));
                int e = (int)Math.Min(int.MaxValue / 2, Math.Round(n.endTick / ticksPerRow));
                if (e <= r) e = r + 1;
                int vol = opt.velocityToVolume ? Math.Max(1, Math.Min(15, (int)Math.Round(n.velocity / 127.0 * 15))) : 15;
                return new RowNote { row = r, endRow = e, note = n.note + opt.transpose, vol = vol, instrument = instrument };
            }

            var melodic = data.notes.Where(n => n.channel != 9).ToList();
            var drums = data.notes.Where(n => n.channel == 9).ToList();

            var lanes = new List<RowNote>[Song.ChannelCount];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = new List<RowNote>();
            bool[] pickLowest = { false, false, true, false };

            var groups = melodic.GroupBy(n => (n.track, n.channel))
                .Select(g => new { key = g.Key, notes = g.ToList(), avg = g.Average(n => (double)n.note) })
                .ToList();
            // ごく少数のノートしかないグループはノイズ扱いで後回し
            int total = Math.Max(1, melodic.Count);
            var major = groups.Where(g => g.notes.Count >= total * 0.03).ToList();
            var minor = groups.Except(major).ToList();

            var mode = opt.mode;
            if (mode == VoiceAssignMode.Auto) mode = major.Count >= 3 ? VoiceAssignMode.ByTrack : VoiceAssignMode.SplitByPitch;
            report.usedMode = mode;

            string GroupName(int track, int channel)
            {
                string n = track < data.trackNames.Count ? data.trackNames[track] : "";
                return string.IsNullOrEmpty(n) ? $"T{track}/ch{channel + 1}" : $"{n}(ch{channel + 1})";
            }

            if (mode == VoiceAssignMode.ByTrack && major.Count >= 2)
            {
                var bass = major.OrderBy(g => g.avg).First();
                var rest = major.Where(g => g != bass).ToList();
                var lead = rest.OrderByDescending(g => g.avg).First();
                var others = rest.Where(g => g != lead).OrderByDescending(g => g.notes.Count).ToList();
                lanes[0].AddRange(lead.notes.Select(n => ToRow(n, Song.InstLead)));
                lanes[2].AddRange(bass.notes.Select(n => ToRow(n, Song.InstBass)));
                foreach (var g in others) lanes[1].AddRange(g.notes.Select(n => ToRow(n, Song.InstSoft)));
                foreach (var g in minor) lanes[1].AddRange(g.notes.Select(n => ToRow(n, Song.InstSoft)));
                report.channelSources[0] = GroupName(lead.key.track, lead.key.channel);
                report.channelSources[1] = string.Join("+", others.Concat(minor).Select(g => GroupName(g.key.track, g.key.channel)));
                report.channelSources[2] = GroupName(bass.key.track, bass.key.channel);
            }
            else
            {
                report.usedMode = VoiceAssignMode.SplitByPitch;
                var all = melodic.Select(n => ToRow(n, Song.InstLead)).GroupBy(n => n.row);
                foreach (var g in all)
                {
                    var ns = g.OrderByDescending(n => n.note).ToList();
                    if (ns.Count == 1)
                    {
                        var n = ns[0];
                        if (n.note < 52) { n.instrument = Song.InstBass; lanes[2].Add(n); }
                        else lanes[0].Add(n);
                        continue;
                    }
                    lanes[0].Add(ns[0]);
                    var low = ns[ns.Count - 1];
                    low.instrument = Song.InstBass;
                    lanes[2].Add(low);
                    if (ns.Count >= 3)
                    {
                        var mid = ns[1];
                        mid.instrument = Song.InstSoft;
                        lanes[1].Add(mid);
                    }
                    report.droppedNotes += Math.Max(0, ns.Count - 3);
                }
                report.channelSources[0] = "highest";
                report.channelSources[1] = "2nd highest";
                report.channelSources[2] = "lowest";
            }

            if (opt.includeDrums)
            {
                foreach (var n in drums)
                {
                    var hit = DrumMap.Map(n.note);
                    var rn = ToRow(n, hit.instrument);
                    rn.note = hit.pitch;
                    rn.priority = hit.priority;
                    rn.drum = true;
                    lanes[3].Add(rn);
                }
                report.channelSources[3] = drums.Count > 0 ? "GM drums (ch10)" : "-";
            }

            // ---- 行配置 ----
            int totalRows = 1;
            foreach (var l in lanes)
            {
                report.droppedNotes += l.RemoveAll(n => n.row >= MaxImportRows);
                foreach (var n in l)
                    totalRows = Math.Max(totalRows, Math.Min(MaxImportRows, n.endRow));
            }
            int rpp = Math.Max(1, Math.Min(Pattern.MaxRows, opt.rowsPerPattern));
            int patternCount = (totalRows + rpp - 1) / rpp;
            var flat = new Cell[Song.ChannelCount, patternCount * rpp];
            for (int c = 0; c < Song.ChannelCount; c++)
                for (int r = 0; r < patternCount * rpp; r++)
                    flat[c, r] = Cell.Blank;

            for (int c = 0; c < Song.ChannelCount; c++)
            {
                var byRow = new List<RowNote>();
                foreach (var g in lanes[c].GroupBy(n => n.row).OrderBy(g => g.Key))
                {
                    var list = g.ToList();
                    RowNote pick;
                    if (c == 3) pick = list.OrderByDescending(n => n.priority).ThenByDescending(n => n.vol).First();
                    else if (pickLowest[c]) pick = list.OrderBy(n => n.note).First();
                    else pick = list.OrderByDescending(n => n.note).First();
                    report.droppedNotes += list.Count - 1;
                    byRow.Add(pick);
                }

                for (int i = 0; i < byRow.Count; i++)
                {
                    var n = byRow[i];
                    int note = n.drum ? n.note : Math.Max(NoteText.MinNote, Math.Min(NoteText.MaxNote, n.note));
                    var cell = Cell.NoteCell(note, n.instrument, n.vol < 15 ? n.vol : -1);
                    flat[c, n.row] = cell;
                    report.placedNotes[c]++;

                    if (n.drum) continue;
                    int nextRow = i + 1 < byRow.Count ? byRow[i + 1].row : int.MaxValue;
                    if (n.endRow < nextRow && n.endRow < patternCount * rpp)
                        flat[c, n.endRow] = new Cell { note = Cell.Off, instrument = -1, volume = -1, probability = 100 };
                }
            }

            // ---- パターン分割・重複排除 ----
            song.patterns.Clear();
            song.order.Clear();
            for (int p = 0; p < patternCount; p++)
            {
                var pat = new Pattern(rpp, p.ToString("X2"));
                for (int c = 0; c < Song.ChannelCount; c++)
                    for (int r = 0; r < rpp; r++)
                        pat.tracks[c].cells[r] = flat[c, p * rpp + r];

                int found = opt.dedupePatterns ? song.patterns.FindIndex(x => x.ContentEquals(pat)) : -1;
                if (found >= 0)
                {
                    song.order.Add(found);
                }
                else
                {
                    pat.name = song.patterns.Count.ToString("X2");
                    song.patterns.Add(pat);
                    song.order.Add(song.patterns.Count - 1);
                }
            }

            report.patterns = song.patterns.Count;
            report.orderLength = song.order.Count;
            song.Validate();
            return song;
        }
    }
}
