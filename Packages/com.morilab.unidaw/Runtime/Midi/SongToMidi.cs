using System;
using System.Collections.Generic;

namespace UniDAW.Midi
{
    /// <summary>Song → Standard MIDI File（順序表を1周展開。エフェクト・確率・条件は無視）。</summary>
    public static class SongToMidi
    {
        /// <summary>ノイズのピッチ → GM ドラム番号（DrumMap の逆の近似）。</summary>
        public static int NoiseToGm(int pitch, int instrument)
        {
            if (instrument == Song.InstKick) return 36;
            if (instrument == Song.InstSnare) return pitch < 8 ? 45 : 38;
            if (instrument == Song.InstCrash) return 49;
            if (instrument == Song.InstHat) return pitch >= 14 ? 42 : 51;
            if (pitch < 6) return 36;
            if (pitch < 12) return 38;
            return 42;
        }

        public static byte[] Export(Song song, int division = 480)
        {
            if (song == null) throw new ArgumentNullException(nameof(song));
            song.Validate();
            int rpb = Math.Max(1, song.rowsPerBeat);
            int ticksPerRow = Math.Max(1, division / rpb);

            var tracks = new List<SmfWriter.TrackData>();
            var open = new MidiNote?[Song.ChannelCount];
            var lastInst = new int[Song.ChannelCount];
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                tracks.Add(new SmfWriter.TrackData { name = Song.ChannelNames[c] });
                lastInst[c] = -1;
            }

            long tick = 0;
            foreach (int pi in song.order)
            {
                var p = song.GetPattern(pi);
                if (p == null) continue;
                for (int row = 0; row < p.length; row++, tick += ticksPerRow)
                {
                    for (int c = 0; c < Song.ChannelCount; c++)
                    {
                        var cell = p.tracks[c].cells[p.CellIndex(c, row)];
                        if (cell.instrument >= 0) lastInst[c] = cell.instrument;
                        if (!cell.HasNote && !cell.IsOff) continue;
                        Close(tracks[c], ref open[c], tick);
                        if (cell.IsOff) continue;
                        bool noise = Song.Kinds[c] == ChannelKind.Noise;
                        int vol = cell.volume >= 0 ? cell.volume : 15;
                        open[c] = new MidiNote
                        {
                            channel = noise ? 9 : c,
                            note = noise ? NoiseToGm(cell.note, lastInst[c]) : cell.note,
                            velocity = Math.Max(1, Math.Min(127, (int)Math.Round(vol / 15.0 * 127))),
                            startTick = tick,
                        };
                        if (noise)
                        {
                            // ドラムは短く
                            Close(tracks[c], ref open[c], tick + Math.Max(1, ticksPerRow / 2));
                        }
                    }
                }
            }
            for (int c = 0; c < Song.ChannelCount; c++) Close(tracks[c], ref open[c], tick);
            return SmfWriter.Write(tracks, song.bpm, division);
        }

        static void Close(SmfWriter.TrackData t, ref MidiNote? n, long tick)
        {
            if (!n.HasValue) return;
            var v = n.Value;
            v.endTick = Math.Max(v.startTick + 1, tick);
            t.notes.Add(v);
            n = null;
        }
    }
}
