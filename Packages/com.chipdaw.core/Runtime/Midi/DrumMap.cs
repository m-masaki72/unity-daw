namespace ChipDaw.Midi
{
    /// <summary>GM ドラム → ノイズチャンネル（ピッチ 0..15, インストゥルメント, 優先度）。</summary>
    public static class DrumMap
    {
        public struct Hit
        {
            public int pitch;
            public int instrument;
            public int priority;
        }

        public static Hit Map(int gmNote)
        {
            switch (gmNote)
            {
                case 35:
                case 36: return new Hit { pitch = 3, instrument = Song.InstKick, priority = 5 };
                case 37:
                case 38:
                case 39:
                case 40: return new Hit { pitch = 9, instrument = Song.InstSnare, priority = 4 };
                case 41:
                case 43:
                case 45: return new Hit { pitch = 5, instrument = Song.InstSnare, priority = 3 };
                case 47:
                case 48:
                case 50: return new Hit { pitch = 7, instrument = Song.InstSnare, priority = 3 };
                case 49:
                case 52:
                case 55:
                case 57: return new Hit { pitch = 11, instrument = Song.InstCrash, priority = 2 };
                case 46: return new Hit { pitch = 13, instrument = Song.InstCrash, priority = 1 };
                case 42:
                case 44: return new Hit { pitch = 14, instrument = Song.InstHat, priority = 0 };
                case 51:
                case 53:
                case 59: return new Hit { pitch = 12, instrument = Song.InstHat, priority = 1 };
                default: return new Hit { pitch = 10, instrument = Song.InstHat, priority = 0 };
            }
        }
    }
}
