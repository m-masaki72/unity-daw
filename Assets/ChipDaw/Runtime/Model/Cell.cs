using System;

namespace ChipDaw
{
    /// <summary>トラッカーのエフェクト種別。表示文字は <see cref="FxText"/> を参照。</summary>
    public enum FxType : byte
    {
        None = 0,
        Arpeggio,     // 0xy : 0 / +x / +y 半音をティック毎に巡回
        SlideUp,      // 1xx : ティック毎に xx/16 半音上昇
        SlideDown,    // 2xx : ティック毎に xx/16 半音下降
        Portamento,   // 3xx : 次ノートへ xx/16 半音/tick で滑る（再トリガーしない）
        Vibrato,      // 4xy : 速度 x, 深さ y
        VolumeSlide,  // Axy : x 上昇 / y 下降（1/8 段階/tick）
        Duty,         // Vxx : デューティ（ノイズは bit0 でショートモード）
        Speed,        // Fxx : xx<0x20 ならスピード(tick/row)、それ以上は BPM
        NoteCut,      // Sxx : xx tick 後にカット
    }

    [Serializable]
    public struct Fx
    {
        public FxType type;
        public byte param;

        public Fx(FxType type, int param)
        {
            this.type = type;
            this.param = (byte)Math.Max(0, Math.Min(255, param));
        }

        public bool IsEmpty => type == FxType.None;
        public int X => param >> 4;
        public int Y => param & 15;

        public static Fx None => default;
        public override string ToString() => FxText.Format(this);
    }

    /// <summary>
    /// 1ステップ（1行×1チャンネル）。ステップシーケンサーの「パラメータロック」と
    /// トラッカーの「列」は同じデータ（volume / fx / probability / condition）を指す。
    /// </summary>
    [Serializable]
    public struct Cell
    {
        public const int Empty = -1;
        public const int Off = -2;

        /// <summary>-1=空, -2=ノートオフ, 0..127=MIDIノート（ノイズは 0..15 のピッチ）</summary>
        public int note;
        /// <summary>-1=指定なし</summary>
        public int instrument;
        /// <summary>-1=指定なし, 0..15</summary>
        public int volume;
        public Fx fx1;
        public Fx fx2;
        /// <summary>0..100 (%)</summary>
        public int probability;
        /// <summary>条件トリガー A:B（B回に1回、A回目だけ鳴る）。B<=1 なら常に。</summary>
        public int condA;
        public int condB;

        public static Cell Blank => new Cell
        {
            note = Empty, instrument = -1, volume = -1, probability = 100, condA = 0, condB = 0,
        };

        public static Cell NoteCell(int note, int instrument = -1, int volume = -1)
        {
            var c = Blank;
            c.note = note;
            c.instrument = instrument;
            c.volume = volume;
            return c;
        }

        public bool HasNote => note >= 0;
        public bool IsOff => note == Off;
        public bool HasCondition => condB > 1;

        public bool IsBlank =>
            note == Empty && instrument < 0 && volume < 0 && fx1.IsEmpty && fx2.IsEmpty;

        /// <summary>ノート以外に何か「ロック」されているか（ステップ表示のマーカー用）</summary>
        public bool HasLocks =>
            volume >= 0 || !fx1.IsEmpty || !fx2.IsEmpty || probability < 100 || HasCondition;

        public bool HasFx(FxType t) => fx1.type == t || fx2.type == t;

        public bool ContentEquals(in Cell o) =>
            note == o.note && instrument == o.instrument && volume == o.volume &&
            fx1.type == o.fx1.type && fx1.param == o.fx1.param &&
            fx2.type == o.fx2.type && fx2.param == o.fx2.param &&
            probability == o.probability && condA == o.condA && condB == o.condB;
    }
}
