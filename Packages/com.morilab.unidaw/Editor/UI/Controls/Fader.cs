using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// DAW 風のフェーダー（縦=音量、横=パン）。相対ドラッグ（Shift で細かく）・ホイールで変更し、
    /// ダブルクリック / Ctrl+クリック / Alt+クリックで既定値に戻す。
    /// ドラッグ中は <see cref="Dragged"/> で値を流し、離したときに <see cref="Committed"/>（開始値, 確定値）を 1 回だけ通知する。
    /// </summary>
    public sealed class Fader : VisualElement
    {
        static readonly Color TrackColor = new Color(0.06f, 0.06f, 0.08f);
        static readonly Color FillColor = new Color(0.35f, 0.5f, 0.85f, 0.55f);
        static readonly Color ThumbColor = new Color(0.82f, 0.84f, 0.9f);
        static readonly Color ZeroColor = new Color(1f, 1f, 1f, 0.18f);
        const float Thumb = 10f, Groove = 4f, FineFactor = 0.1f;

        readonly bool vertical, bipolar;
        readonly float min, max, defaultValue, wheelStep;
        float value, startValue, startPointer;
        bool dragging;

        /// <summary>ドラッグ・ホイール中の値（ライブ反映用）。</summary>
        public event Action<float> Dragged;
        /// <summary>確定（開始値, 確定値）。</summary>
        public event Action<float, float> Committed;

        public Fader(bool vertical, float min, float max, float defaultValue, float wheelStep, bool bipolar = false)
        {
            this.vertical = vertical;
            this.min = min;
            this.max = max;
            this.defaultValue = defaultValue;
            this.wheelStep = wheelStep;
            this.bipolar = bipolar;
            value = defaultValue;
            AddToClassList("cd-fader");
            AddToClassList(vertical ? "vertical" : "horizontal");
            tooltip = "ドラッグ（Shift で微調整）・ホイールで変更\nダブルクリック / Ctrl+クリックで既定値";
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
        }

        public float Value => value;
        public bool IsDragging => dragging;

        public void SetValueWithoutNotify(float v)
        {
            if (dragging) return;
            Show(v);
        }

        void Show(float v)
        {
            v = Mathf.Clamp(v, min, max);
            if (v == value) return;
            value = v;
            MarkDirtyRepaint();
        }

        float Travel => Mathf.Max(1f, (vertical ? contentRect.height : contentRect.width) - Thumb);
        float Norm(float v) => (v - min) / (max - min);

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            e.StopPropagation();
            if (e.clickCount >= 2 || e.actionKey || e.altKey)
            {
                float before = value;
                Show(defaultValue);
                if (before != value) Committed?.Invoke(before, value);
                return;
            }
            dragging = true;
            startValue = value;
            startPointer = vertical ? -e.localPosition.y : e.localPosition.x;
            this.CapturePointer(e.pointerId);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!dragging || !this.HasPointerCapture(e.pointerId)) return;
            float pos = vertical ? -e.localPosition.y : e.localPosition.x;
            float delta = (pos - startPointer) / Travel * (max - min) * (e.shiftKey ? FineFactor : 1f);
            float before = value;
            Show(startValue + delta);
            if (value != before) Dragged?.Invoke(value);
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!dragging) return;
            dragging = false;
            this.ReleasePointer(e.pointerId);
            if (value != startValue) Committed?.Invoke(startValue, value);
        }

        void OnWheel(WheelEvent e)
        {
            if (dragging || e.delta.y == 0) return;
            e.StopPropagation();
            float before = value;
            Show(value - Mathf.Sign(e.delta.y) * wheelStep * (e.shiftKey ? FineFactor : 1f));
            if (value != before) Committed?.Invoke(before, value);
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 2 || r.height < 2) return;
            var p = mgc.painter2D;
            float t = Norm(value), z = bipolar ? Norm(0f) : 0f;
            if (vertical)
            {
                float cx = r.width * 0.5f;
                float Y(float n) => r.height - Thumb * 0.5f - n * Travel;
                Rect(p, cx - Groove * 0.5f, Thumb * 0.5f, Groove, r.height - Thumb, TrackColor);
                float y0 = Y(z), y1 = Y(t);
                Rect(p, cx - Groove * 0.5f, Mathf.Min(y0, y1), Groove, Mathf.Abs(y1 - y0), FillColor);
                if (bipolar) Rect(p, 2, y0 - 0.5f, r.width - 4, 1, ZeroColor);
                Rect(p, 1, y1 - Thumb * 0.5f, r.width - 2, Thumb, ThumbColor);
                Rect(p, 3, y1 - 0.5f, r.width - 6, 1, TrackColor);
            }
            else
            {
                float cy = r.height * 0.5f;
                float X(float n) => Thumb * 0.5f + n * Travel;
                Rect(p, Thumb * 0.5f, cy - Groove * 0.5f, r.width - Thumb, Groove, TrackColor);
                float x0 = X(z), x1 = X(t);
                Rect(p, Mathf.Min(x0, x1), cy - Groove * 0.5f, Mathf.Abs(x1 - x0), Groove, FillColor);
                if (bipolar) Rect(p, x0 - 0.5f, 1, 1, r.height - 2, ZeroColor);
                Rect(p, x1 - Thumb * 0.3f, 1, Thumb * 0.6f, r.height - 2, ThumbColor);
            }
        }

        internal static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (w <= 0 || h <= 0) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }
    }
}
