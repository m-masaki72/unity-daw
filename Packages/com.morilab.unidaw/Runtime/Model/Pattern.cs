using System;

namespace UniDAW
{
    [Serializable]
    public class Track
    {
        /// <summary>0 = パターン長に従う。1..パターン長でポリメーター（トラック単位のループ長）。</summary>
        public int length;
        public Cell[] cells = Array.Empty<Cell>();

        public Track() { }

        public Track(int rows)
        {
            cells = new Cell[rows];
            for (int i = 0; i < rows; i++) cells[i] = Cell.Blank;
        }

        public Track Clone() => new Track { length = length, cells = (Cell[])cells.Clone() };
    }

    [Serializable]
    public class Pattern
    {
        public const int MaxRows = 256;

        public string name = "";
        public int length = 16;
        /// <summary>コード進行（"C G Am F" / "I V vi IV"）。パターン全体に均等に割り付ける。空 = なし。</summary>
        public string chords = "";
        public Track[] tracks = Array.Empty<Track>();

        public Pattern() { }

        public Pattern(int length, string name = "")
        {
            this.name = name;
            this.length = Math.Max(1, Math.Min(MaxRows, length));
            tracks = new Track[Song.ChannelCount];
            for (int c = 0; c < tracks.Length; c++) tracks[c] = new Track(this.length);
        }

        /// <summary>そのチャンネルで実際にループする長さ。</summary>
        public int TrackLength(int ch)
        {
            int l = tracks[ch].length;
            return l > 0 && l < length ? l : length;
        }

        /// <summary>パターン上の行 row（ループ回数 loop）で鳴るセルの添字。</summary>
        public int CellIndex(int ch, int row, int loop = 0)
        {
            int tl = TrackLength(ch);
            long abs = (long)loop * length + row;
            return (int)(abs % tl);
        }

        public void EnsureShape()
        {
            if (tracks == null || tracks.Length != Song.ChannelCount)
            {
                var old = tracks ?? Array.Empty<Track>();
                tracks = new Track[Song.ChannelCount];
                for (int c = 0; c < tracks.Length; c++)
                    tracks[c] = c < old.Length && old[c] != null ? old[c] : new Track(length);
            }
            Resize(length);
        }

        public void Resize(int newLength)
        {
            newLength = Math.Max(1, Math.Min(MaxRows, newLength));
            length = newLength;
            foreach (var t in tracks)
            {
                if (t.cells == null) t.cells = Array.Empty<Cell>();
                if (t.cells.Length == newLength) continue;
                var cells = new Cell[newLength];
                for (int i = 0; i < newLength; i++)
                    cells[i] = i < t.cells.Length ? t.cells[i] : Cell.Blank;
                t.cells = cells;
                if (t.length >= newLength) t.length = 0;
            }
        }

        public Pattern Clone()
        {
            var p = new Pattern { name = name, length = length, chords = chords, tracks = new Track[tracks.Length] };
            for (int i = 0; i < tracks.Length; i++) p.tracks[i] = tracks[i].Clone();
            return p;
        }

        public bool ContentEquals(Pattern o)
        {
            if (o == null || o.length != length || o.tracks.Length != tracks.Length) return false;
            for (int c = 0; c < tracks.Length; c++)
            {
                if (tracks[c].length != o.tracks[c].length) return false;
                var a = tracks[c].cells;
                var b = o.tracks[c].cells;
                if (a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (!a[i].ContentEquals(b[i])) return false;
            }
            return true;
        }
    }
}
