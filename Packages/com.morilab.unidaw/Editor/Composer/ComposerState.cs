using System;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>パターンの見え方（int としてシリアライズされるので値は固定）。</summary>
    public enum PatternViewKind { Tracker = 0, Step = 1, PianoRoll = 2 }

    /// <summary>ピアノロールのツール（int としてシリアライズされるので値は固定）。</summary>
    public enum RollTool { Select = 0, Draw = 1, Erase = 2 }

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
        public bool[] muted = new bool[Song.ChannelCount];
        public bool[] soloed = new bool[Song.ChannelCount];

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

        /// <summary>デシリアライズ後の配列長の補正。</summary>
        public void EnsureArrays()
        {
            if (muted == null || muted.Length != Song.ChannelCount) muted = new bool[Song.ChannelCount];
            if (soloed == null || soloed.Length != Song.ChannelCount) soloed = new bool[Song.ChannelCount];
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
        }
    }
}
