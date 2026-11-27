using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 描画で中身を出す要素（ピアノロールのキャンバス、トラッカーのセル等）が、ポインタ位置に応じた説明を返す。
    /// null を返すと要素自身の <see cref="VisualElement.tooltip"/> と親へ回す。
    /// </summary>
    public interface ITooltipSource
    {
        /// <param name="picked">ポインタの下にある要素（自分自身か子孫）</param>
        /// <param name="worldPos">パネル座標のポインタ位置</param>
        string TooltipAt(VisualElement picked, Vector2 worldPos);
    }

    /// <summary>
    /// ウィンドウ内のツールチップを自前で出す。Unity 標準のツールチップはエディタが待機中だと出るのが遅れたり出なかったりするので、
    /// ウィンドウの更新（<see cref="Tick"/>）で一定の待ち時間で出し、続けて別の要素へ移ったときはすぐ出す。
    /// </summary>
    public sealed class TooltipOverlay : VisualElement
    {
        public const double Delay = 0.45, Warm = 0.6;
        const float OffsetX = 14f, OffsetY = 20f, Margin = 4f;

        readonly VisualElement root;
        readonly Label label;
        string pendingText, shownText;
        object pendingOwner, suppressedOwner;
        Vector2 pointer;
        double pendingSince, hiddenAt = double.MinValue;
        bool shown;

        public TooltipOverlay(VisualElement root)
        {
            this.root = root;
            pickingMode = PickingMode.Ignore;
            AddToClassList("cd-tooltip");
            label = new Label { pickingMode = PickingMode.Ignore };
            Add(label);
            style.display = DisplayStyle.None;
            root.Add(this);
            root.RegisterCallback<PointerMoveEvent>(e => OnMove(e.position, e.pressedButtons != 0), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(_ => Suppress(), TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(_ => Suppress(), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => Clear());
            // 標準のツールチップは出さない（二重に出る・タイミングが不揃い）
            root.RegisterCallback<TooltipEvent>(e =>
            {
                e.tooltip = null;
                e.rect = Rect.zero;
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        static double Now => DawPlatform.Current.Now;

        /// <summary>ポインタ位置の説明と、その持ち主（同じ持ち主・同じ文なら出し直さない）。</summary>
        public static string Find(IPanel panel, Vector2 worldPos, out object owner)
        {
            owner = null;
            var picked = panel?.Pick(worldPos);
            for (var e = picked; e != null; e = e.parent)
            {
                if (e is ITooltipSource src)
                {
                    string t = src.TooltipAt(picked, worldPos);
                    if (!string.IsNullOrEmpty(t))
                    {
                        owner = e;
                        return t;
                    }
                }
                if (!string.IsNullOrEmpty(e.tooltip))
                {
                    owner = e;
                    return e.tooltip;
                }
            }
            return null;
        }

        void OnMove(Vector2 pos, bool pressed)
        {
            pointer = pos;
            if (pressed)
            {
                Suppress();
                return;
            }
            string text = Find(root.panel, pos, out object owner);
            if (owner != null && owner == suppressedOwner && text == pendingText) return;
            suppressedOwner = null;
            if (text == pendingText && owner == pendingOwner)
            {
                if (shown) Place();
                return;
            }
            pendingText = text;
            pendingOwner = owner;
            pendingSince = Now;
            if (shown) Hide();
            if (text != null && Now - hiddenAt < Warm) Show();
        }

        /// <summary>クリック・ホイール・ドラッグ中は消し、同じ要素の上にいる間は出し直さない。</summary>
        void Suppress()
        {
            if (shown) Hide();
            suppressedOwner = pendingOwner;
            hiddenAt = double.MinValue;
        }

        void Clear()
        {
            if (shown) Hide();
            pendingText = null;
            pendingOwner = null;
            suppressedOwner = null;
        }

        /// <summary>ウィンドウの更新から毎回呼ぶ。</summary>
        public void Tick()
        {
            if (shown || pendingText == null || suppressedOwner != null) return;
            if (Now - pendingSince >= Delay) Show();
        }

        void Show()
        {
            shown = true;
            if (shownText != pendingText) label.text = shownText = pendingText;
            style.display = DisplayStyle.Flex;
            BringToFront();
            Place();
            // 大きさが決まってから画面内に収める
            schedule.Execute(Place);
        }

        void Hide()
        {
            shown = false;
            hiddenAt = Now;
            style.display = DisplayStyle.None;
        }

        void Place()
        {
            var bounds = root.layout;
            var local = root.WorldToLocal(pointer);
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w)) w = 0;
            if (float.IsNaN(h)) h = 0;
            float x = Mathf.Min(local.x + OffsetX, bounds.width - w - Margin);
            float y = local.y + OffsetY;
            if (y + h > bounds.height - Margin) y = local.y - h - Margin;
            style.left = Mathf.Max(Margin, x);
            style.top = Mathf.Max(Margin, y);
        }
    }
}
