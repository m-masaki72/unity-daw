using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 見出し＋大きな数値の入力欄。上下ドラッグ（Shift で細かく）・ホイールで増減し、
    /// ダブルクリックで直接入力。値の確定はドラッグを離したとき／入力を確定したときに 1 回だけ通知する。
    /// </summary>
    public sealed class NumberBox : VisualElement
    {
        const float PixelsPerStep = 4f;

        readonly Label valueLabel;
        readonly TextField editor;
        readonly float min, max, step, fine;
        readonly string format;
        readonly Action<float> commit;

        float value, dragStartValue, dragStartY;
        bool dragging, editing;

        public NumberBox(string caption, float min, float max, float step, string format, Action<float> commit, string tooltip = null)
        {
            this.min = min;
            this.max = max;
            this.step = step;
            fine = format.Contains(".") ? step * 0.1f : step;
            this.format = format;
            this.commit = commit;
            this.tooltip = (tooltip != null ? tooltip + "\n" : "") + "上下ドラッグ・ホイールで変更、ダブルクリックで入力";
            AddToClassList("cd-nbox");

            var cap = new Label(caption) { pickingMode = PickingMode.Ignore };
            cap.AddToClassList("cd-nbox-cap");
            Add(cap);
            valueLabel = new Label { pickingMode = PickingMode.Ignore };
            valueLabel.AddToClassList("cd-nbox-val");
            Add(valueLabel);

            editor = new TextField { isDelayed = true };
            editor.AddToClassList("cd-nbox-edit");
            editor.style.display = DisplayStyle.None;
            editor.RegisterValueChangedCallback(e => EndEdit(true));
            editor.RegisterCallback<KeyDownEvent>(OnEditorKey, TrickleDown.TrickleDown);
            editor.RegisterCallback<FocusOutEvent>(_ => EndEdit(true));
            Add(editor);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
        }

        public float Value => value;

        public void SetValueWithoutNotify(float v)
        {
            if (dragging || editing) return;
            Show(v);
        }

        void Show(float v)
        {
            value = v;
            valueLabel.text = v.ToString(format, CultureInfo.InvariantCulture);
        }

        float Clamp(float v) => Mathf.Clamp(Mathf.Round(v / fine) * fine, min, max);

        void Commit(float v)
        {
            v = Clamp(v);
            Show(v);
            commit?.Invoke(v);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || editing) return;
            e.StopPropagation();
            if (e.clickCount >= 2)
            {
                BeginEdit();
                return;
            }
            dragging = true;
            dragStartY = e.position.y;
            dragStartValue = value;
            this.CapturePointer(e.pointerId);
            AddToClassList("dragging");
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!dragging || !this.HasPointerCapture(e.pointerId)) return;
            float steps = Mathf.Floor((dragStartY - e.position.y) / PixelsPerStep);
            float per = e.shiftKey ? fine : step;
            Show(Clamp(dragStartValue + steps * per));
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!dragging) return;
            dragging = false;
            this.ReleasePointer(e.pointerId);
            RemoveFromClassList("dragging");
            if (!Mathf.Approximately(value, dragStartValue)) Commit(value);
        }

        void OnWheel(WheelEvent e)
        {
            if (editing || dragging || e.delta.y == 0) return;
            float per = e.shiftKey ? fine : step;
            Commit(value - Mathf.Sign(e.delta.y) * per);
            e.StopPropagation();
        }

        void BeginEdit()
        {
            editing = true;
            valueLabel.style.visibility = Visibility.Hidden;
            editor.style.display = DisplayStyle.Flex;
            editor.SetValueWithoutNotify(value.ToString(format, CultureInfo.InvariantCulture));
            editor.schedule.Execute(() =>
            {
                editor.Focus();
                editor.SelectAll();
            });
        }

        void EndEdit(bool apply)
        {
            if (!editing) return;
            editing = false;
            editor.style.display = DisplayStyle.None;
            valueLabel.style.visibility = Visibility.Visible;
            if (apply && float.TryParse(editor.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) Commit(v);
        }

        void OnEditorKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                EndEdit(false);
                e.StopImmediatePropagation();
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                EndEdit(true);
                e.StopImmediatePropagation();
            }
        }
    }
}
