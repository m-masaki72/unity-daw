using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>パターンの見え方（トラッカー／ステップ／ピアノロール）共通の更新口。</summary>
    internal interface IPatternView
    {
        void Refresh();
        void UpdatePlayhead();

        /// <summary>エディタ見出しに並べる、この表示だけの道具（無ければ null）。</summary>
        VisualElement HeaderTools { get; }
    }
}
