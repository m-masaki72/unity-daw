namespace UniDAW.Editor
{
    /// <summary>パターンの見え方（トラッカー／ステップ／ピアノロール）共通の更新口。</summary>
    internal interface IPatternView
    {
        void Refresh();
        void UpdatePlayhead();
    }
}
