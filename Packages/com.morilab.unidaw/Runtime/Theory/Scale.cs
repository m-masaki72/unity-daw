using System;

namespace UniDAW
{
    /// <summary>スケールの種類（int としてシリアライズされるので値は固定。0 = 指定なし）。</summary>
    public enum ScaleMode
    {
        Chromatic = 0,
        Major = 1,
        Minor = 2,
        Dorian = 3,
        Phrygian = 4,
        Lydian = 5,
        Mixolydian = 6,
        HarmonicMinor = 7,
        MajorPentatonic = 8,
        MinorPentatonic = 9,
        Blues = 10,
    }

    /// <summary>キー（主音 + スケール）。音の判定・最寄りの音への補正・度数での移動。</summary>
    [Serializable]
    public struct Scale
    {
        public static readonly string[] RootNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        public static readonly string[] ModeNames =
        {
            "Chromatic", "Major", "Minor", "Dorian", "Phrygian", "Lydian", "Mixolydian",
            "Harmonic Minor", "Major Penta", "Minor Penta", "Blues",
        };

        static readonly int[][] Steps =
        {
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            new[] { 0, 2, 4, 5, 7, 9, 11 },
            new[] { 0, 2, 3, 5, 7, 8, 10 },
            new[] { 0, 2, 3, 5, 7, 9, 10 },
            new[] { 0, 1, 3, 5, 7, 8, 10 },
            new[] { 0, 2, 4, 6, 7, 9, 11 },
            new[] { 0, 2, 4, 5, 7, 9, 10 },
            new[] { 0, 2, 3, 5, 7, 8, 11 },
            new[] { 0, 2, 4, 7, 9 },
            new[] { 0, 3, 5, 7, 10 },
            new[] { 0, 3, 5, 6, 7, 10 },
        };

        public int root;
        public ScaleMode mode;

        public Scale(int root, ScaleMode mode)
        {
            this.root = Pc(root);
            this.mode = (int)mode >= 0 && (int)mode < Steps.Length ? mode : ScaleMode.Chromatic;
        }

        public static int Pc(int note) => ((note % 12) + 12) % 12;

        public bool IsChromatic => mode == ScaleMode.Chromatic;

        /// <summary>主音からの半音（昇順）。</summary>
        public int[] Intervals => Steps[(int)mode];

        public bool Contains(int note)
        {
            int rel = Pc(note - root);
            foreach (var s in Intervals) if (s == rel) return true;
            return false;
        }

        /// <summary>
        /// スケール内の最寄りの音へ。等距離なら dir の向き（0 なら下）。
        /// </summary>
        public int Snap(int note, int dir = 0)
        {
            if (Contains(note)) return note;
            for (int d = 1; d < 12; d++)
            {
                bool up = Contains(note + d), down = Contains(note - d);
                if (up && down) return dir > 0 ? note + d : note - d;
                if (up) return note + d;
                if (down) return note - d;
            }
            return note;
        }

        /// <summary>スケール上で degrees 段ずらす（スケール外の音はまず補正する）。</summary>
        public int Step(int note, int degrees)
        {
            note = Snap(note);
            int dir = Math.Sign(degrees);
            for (int i = 0; i < Math.Abs(degrees); i++)
            {
                do note += dir; while (!Contains(note));
            }
            return note;
        }

        public override string ToString() => IsChromatic ? "—" : $"{RootNames[Pc(root)]} {ModeNames[(int)mode]}";
    }
}
