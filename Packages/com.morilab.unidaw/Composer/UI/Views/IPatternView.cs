using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>パターンの見え方（トラッカー／ステップ／ピアノロール）共通の更新口。</summary>
    public interface IPatternView
    {
        void Refresh();
        void UpdatePlayhead();

        /// <summary>どの表示か（<see cref="ComposerState.view"/> と照らして選ぶ）。</summary>
        PatternViewKind Kind { get; }

        /// <summary>エディタ見出しに並べる、この表示だけの道具（無ければ null）。</summary>
        VisualElement HeaderTools { get; }
    }
}
