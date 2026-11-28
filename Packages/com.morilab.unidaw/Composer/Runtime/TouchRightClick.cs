using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// タッチ画面で右クリックの操作（ピアノロールの消去・各所のメニュー）を使うためのモード切り替えボタン。
    /// ボタンを押した後の 1 回のタッチ（押して・動かして・離す）を、右ボタンのマウス操作として送り直す。
    /// </summary>
    public sealed class TouchRightClick
    {
        readonly VisualElement root;
        readonly Button button;
        bool armed;
        int activePointer = PointerId.invalidPointerId;
        bool resending;

        public TouchRightClick(VisualElement root)
        {
            this.root = root;
            button = new Button(() => Armed = !Armed) { text = "右", tooltip = "次のタッチを右クリックとして扱う（消去・メニュー）" };
            button.AddToClassList("cd-touch-right");
            root.Add(button);
            root.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(_ => End(), TrickleDown.TrickleDown);
        }

        public VisualElement Button => button;

        /// <summary>次のタッチを右クリックとして扱うか。</summary>
        public bool Armed
        {
            get => armed;
            set
            {
                armed = value;
                button.EnableInClassList("cd-touch-right--on", value);
            }
        }

        void OnDown(PointerDownEvent e)
        {
            if (resending || !armed || activePointer != PointerId.invalidPointerId) return;
            // マウスには本物の右ボタンがある（送り直した右クリックもマウスとして届く）
            if (e.pointerType == UnityEngine.UIElements.PointerType.mouse) return;
            if (e.target is VisualElement t && (t == button || button.Contains(t))) return;
            activePointer = e.pointerId;
            Swallow(e);
            Resend(EventType.MouseDown, e.position);
        }

        void OnMove(PointerMoveEvent e)
        {
            if (resending || e.pointerId != activePointer) return;
            Swallow(e);
            Resend(EventType.MouseDrag, e.position);
        }

        void OnUp(PointerUpEvent e)
        {
            if (resending || e.pointerId != activePointer) return;
            Swallow(e);
            Resend(EventType.MouseUp, e.position);
            End();
        }

        void End()
        {
            activePointer = PointerId.invalidPointerId;
            Armed = false;
        }

        static void Swallow(EventBase e) => e.StopImmediatePropagation();

        void Resend(EventType type, Vector2 position)
        {
            var panel = root.panel;
            if (panel == null) return;
            var ev = new Event { type = type, button = 1, mousePosition = position, clickCount = 1 };
            resending = true;
            try
            {
                switch (type)
                {
                    case EventType.MouseDown:
                        using (var pe = PointerDownEvent.GetPooled(ev)) panel.visualTree.SendEvent(pe);
                        break;
                    case EventType.MouseUp:
                        using (var pe = PointerUpEvent.GetPooled(ev)) panel.visualTree.SendEvent(pe);
                        break;
                    default:
                        using (var pe = PointerMoveEvent.GetPooled(ev)) panel.visualTree.SendEvent(pe);
                        break;
                }
            }
            finally
            {
                resending = false;
            }
        }
    }
}
