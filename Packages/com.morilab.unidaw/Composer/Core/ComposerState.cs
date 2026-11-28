using System;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>パターンの見え方（int としてシリアライズされるので値は固定）。</summary>
    public enum PatternViewKind { Tracker = 0, Step = 1, PianoRoll = 2 }

    /// <summary>ピアノロールのツール（int としてシリアライズされるので値は固定）。</summary>
    public enum RollTool { Select = 0, Draw = 1, Erase = 2 }

    /// <summary>下ゾーン（Cubase の Lower Zone）のタブ（int としてシリアライズされるので値は固定）。Arrange は上部の帯に移った（読み込み時は Mixer に読み替え）。</summary>
    /// <summary>下ゾーンのタブ。Assist（右ゾーンへ移動）と Arrange（上部の帯）は以前の版で保存された値の読み込み用。</summary>
    public enum LowerTab { Mixer = 0, Instrument = 1, Assist = 2, Arrange = 3, Master = 4 }

    /// <summary>右ゾーンのタブ。</summary>
    public enum RightTab { Inspector = 0, Assist = 1 }

    /// <summary>
    /// コンポーザーの編集状態（ウィンドウにシリアライズされ、ドメインリロードを越えて残る）。
    /// 純粋なデータで、変更は <see cref="ComposerSession"/> 経由で行う。
    /// </summary>
    [Serializable]
    public class ComposerState
    {
        /// <summary>トラッカーでカーソルが乗る列数（Note Ins Vol FxT FxP FxT FxP）。</summary>
        public const int TrackerColumns = 7;
        public const int MaxOctave = 8, MaxEditStep = 16;

        public Song song;
        public int patternIndex;
        public int orderIndex;
        public int cursorRow;
        public int cursorCh;
        public int cursorCol;
        public int octave = 4;
        public int editStep = 1;
        public PatternViewKind view;
        public bool follow = true;
        /// <summary>DAW のサイクル（ループ）。オン = パターンをループ再生、オフ = 順序表に沿って曲を再生。</summary>
        public bool loop = true;
        public RollTool rollTool;
        public bool recording;
        /// <summary>メトロノーム（再生中に拍のクリック）。</summary>
        public bool metronome;
        /// <summary>録音オンで再生を始めるとき 1 小節のプリカウントを入れる。</summary>
        public bool countIn = true;
        /// <summary>アレンジ表示の横幅（1 行あたりのピクセル）。</summary>
        public float arrangeZoom = 3f;
        public const float MinArrangeZoom = 0.5f, MaxArrangeZoom = 16f;
        /// <summary>下ゾーン（ミキサー / 楽器エディタ）を開いているか。</summary>
        public bool lowerOpen = true;
        public LowerTab lowerTab;
        public RightTab rightTab;
        /// <summary>キー操作の一覧（F1）を重ねて表示中か。保存しない。</summary>
        [NonSerialized] public bool keyHelpOpen;
        /// <summary>左（パターン一覧）・右（インスペクター）パネルの開閉と、ドラッグで決めた大きさ。</summary>
        public bool leftOpen = true, rightOpen = true;
        public float leftWidth = 150f, rightWidth = 270f, lowerHeight = 230f;
        public const float MinSideWidth = 120f, MaxSideWidth = 480f, MinLowerHeight = 90f, MaxLowerHeight = 600f;
        /// <summary>上部のアレンジ帯の開閉と高さ。</summary>
        public bool bandOpen = true;
        public float bandHeight = 120f;
        public const float MinBandHeight = 60f, MaxBandHeight = 360f;
        /// <summary>楽器エディタで編集中のマクロ。</summary>
        public MacroKind macro;
        /// <summary>入力支援：伴奏の型・メロディ提案の長さ / ばらつき / 乱数の種。</summary>
        public BassStyle assistBass = BassStyle.Octave;
        public ChordStyle assistChord = ChordStyle.Arpeggio;
        public DrumStyle assistDrums = DrumStyle.Basic;
        public bool assistFill = true;
        public int assistRows = 16;
        public float assistTemperature = 1f;
        public int assistSeed;
        public AiTarget aiTarget;
        public int aiModel;
        public string aiPrompt = "";
        /// <summary>Claude に書き換えさせないチャンネル（ビット）と小節（"1, 3-4"、パターン内の 1 始まり）。</summary>
        public int aiLockChannels;
        public string aiLockBars = "";
        /// <summary>曲の下書きを最後まで自動で進める（false なら 1 段ずつ案を選んで「次へ」）。</summary>
        public bool aiDraftAuto = true;
        /// <summary>曲の下書きの後、続けてミックス・マスタリング・講評まで頼む。</summary>
        public bool aiDraftFinish;

        public Pattern CurrentPattern => song != null ? song.GetPattern(patternIndex) : null;

        /// <summary>セル単位（列なし）で編集するビューか。</summary>
        public bool CellMode => view != PatternViewKind.Tracker;

        /// <summary>カーソル位置のセル（ポリメーターの折り返しを考慮）。</summary>
        public Cell CursorCell
        {
            get
            {
                var p = CurrentPattern;
                return p != null ? p.tracks[cursorCh].cells[p.CellIndex(cursorCh, cursorRow)] : Cell.Blank;
            }
        }

        /// <summary>インデックス・カーソルを曲の範囲内に収める。</summary>
        public void Clamp()
        {
            if (song == null) return;
            orderIndex = Mathf.Clamp(orderIndex, 0, song.order.Count - 1);
            patternIndex = Mathf.Clamp(patternIndex, 0, song.patterns.Count - 1);
            cursorRow = Mathf.Clamp(cursorRow, 0, CurrentPattern.length - 1);
            cursorCh = Mathf.Clamp(cursorCh, 0, Song.ChannelCount - 1);
            cursorCol = Mathf.Clamp(cursorCol, 0, TrackerColumns - 1);
            octave = Mathf.Clamp(octave, 0, MaxOctave);
            editStep = Mathf.Clamp(editStep, 0, MaxEditStep);
            if (lowerTab == LowerTab.Arrange || lowerTab == LowerTab.Assist) lowerTab = LowerTab.Mixer;
            if (rightTab != RightTab.Assist) rightTab = RightTab.Inspector;
        }
    }
}
