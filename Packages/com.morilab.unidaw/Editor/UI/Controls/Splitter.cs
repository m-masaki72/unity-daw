using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// パネルの境界。ドラッグ中は対象の幅 / 高さだけを直接変え、離したときに 1 回だけ <c>commit</c> する
    /// （ドラッグ中に全体を再描画しない）。ダブルクリックで対象パネルを閉じる。
    /// </summary>
    public sealed class Splitter : VisualElement
    {
        readonly VisualElement target;
        readonly PanelEdge edge;
        readonly Func<float> min, max;
        readonly Action<float> commit;
        readonly Action toggle;
        bool dragging;
        float startPointer, startSize, size;

        bool Vertical => edge == PanelEdge.Lower || edge == PanelEdge.Band;

        public Splitter(VisualElement target, PanelEdge edge, Func<float> min, Func<float> max, Action<float> commit, Action toggle, string tooltip)
        {
            this.target = target;
            this.edge = edge;
            this.min = min;
            this.max = max;
            this.commit = commit;
            this.toggle = toggle;
            this.tooltip = tooltip;
            AddToClassList("cd-splitter");
            AddToClassList(Vertical ? "cd-splitter-h" : "cd-splitter-v");
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        float Coord(Vector3 p) => Vertical ? p.y : p.x;

        /// <summary>ポインタが動いた向きと、対象が大きくなる向きの対応（右・下のパネルは逆向き）。</summary>
        float Sign => edge == PanelEdge.Left || edge == PanelEdge.Band ? 1f : -1f;

        float Current => Vertical ? target.resolvedStyle.height : target.resolvedStyle.width;

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                toggle?.Invoke();
                e.StopPropagation();
                return;
            }
            float cur = Current;
            if (float.IsNaN(cur)) return;
            dragging = true;
            startPointer = Coord(e.position);
            startSize = size = cur;
            this.CapturePointer(e.pointerId);
            AddToClassList("drag");
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!dragging || !this.HasPointerCapture(e.pointerId)) return;
            float lo = min(), hi = Mathf.Max(lo, max());
            size = Mathf.Clamp(startSize + (Coord(e.position) - startPointer) * Sign, lo, hi);
            if (Vertical) target.style.height = size;
            else target.style.width = size;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!dragging) return;
            dragging = false;
            RemoveFromClassList("drag");
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (!Mathf.Approximately(size, startSize)) commit(size);
            e.StopPropagation();
        }
    }
}
