using System;

namespace UniDAW.Editor
{
    /// <summary>ドラッグで大きさを変えられる境界。</summary>
    public enum PanelEdge { Left, Right, Lower, Band }

    /// <summary>周辺パネル（左・右・上のアレンジ帯・下ゾーン）の開閉。</summary>
    [Serializable]
    public struct PanelVisibility
    {
        public bool left, right, band, lower;

        public PanelVisibility(bool left, bool right, bool band, bool lower)
        {
            this.left = left;
            this.right = right;
            this.band = band;
            this.lower = lower;
        }

        public override string ToString() => $"L{(left ? 1 : 0)} R{(right ? 1 : 0)} B{(band ? 1 : 0)} Z{(lower ? 1 : 0)}";
    }

    /// <summary>
    /// ウィンドウが狭いときに、編集エリアの最小サイズを守るため周辺パネルを自動で畳む規則（UI 非依存の純粋関数）。
    /// 横は 右 → 左、縦は アレンジ帯 → 下ゾーン の順に畳む。ユーザーが開閉した状態そのものは変えず、表示だけに使う。
    /// </summary>
    public static class PanelLayout
    {
        /// <summary>トラッカーは 4ch × 6 列を横スクロールなしで見せたい幅。他のビューは伸縮できる。</summary>
        public const float TrackerMinWidth = 700f, EditorMinWidth = 360f, EditorMinHeight = 160f;

        public static float MinEditorWidth(PatternViewKind view) =>
            view == PatternViewKind.Tracker ? TrackerMinWidth : EditorMinWidth;

        /// <param name="windowWidth">ウィンドウ（本体部分）の幅。</param>
        /// <param name="windowHeight">ツールバー・ステータスバーを除いた本体部分の高さ。</param>
        /// <param name="wanted">ユーザーが開いているパネル。</param>
        public static PanelVisibility Fit(float windowWidth, float windowHeight, PanelVisibility wanted,
            float leftWidth, float rightWidth, float bandHeight, float lowerHeight, PatternViewKind view)
        {
            // レイアウト前（NaN / 0）は判断できないので、そのまま返す
            if (!(windowWidth > 0) || !(windowHeight > 0) || float.IsInfinity(windowWidth) || float.IsInfinity(windowHeight))
                return wanted;

            var v = wanted;
            float minW = MinEditorWidth(view);
            float Width() => windowWidth - (v.left ? Size(leftWidth) : 0) - (v.right ? Size(rightWidth) : 0);
            if (Width() < minW) v.right = false;
            if (Width() < minW) v.left = false;

            float Height() => windowHeight - (v.band ? Size(bandHeight) : 0) - (v.lower ? Size(lowerHeight) : 0);
            if (Height() < EditorMinHeight) v.band = false;
            if (Height() < EditorMinHeight) v.lower = false;
            return v;
        }

        static float Size(float x) => x > 0 && !float.IsInfinity(x) ? x : 0;
    }
}
