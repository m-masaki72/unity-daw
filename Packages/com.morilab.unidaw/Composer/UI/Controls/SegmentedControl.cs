using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>横並びの択一ボタン（表示切替など）。</summary>
    public sealed class SegmentedControl : VisualElement
    {
        readonly List<Label> items = new List<Label>();

        public SegmentedControl(IReadOnlyList<string> names, Action<int> onSelect)
        {
            AddToClassList("cd-seg");
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i;
                var l = new Label(names[i]);
                l.AddToClassList("cd-seg-item");
                l.AddManipulator(new Clickable(() => onSelect(idx)));
                items.Add(l);
                Add(l);
            }
        }

        public void SetIndex(int index)
        {
            for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("on", i == index);
        }
    }

    /// <summary>オン・オフを切り替える丸い札。</summary>
    public sealed class ToggleChip : Label
    {
        public ToggleChip(string text, Action<bool> onChange, string tooltip = null) : base(text)
        {
            this.tooltip = tooltip;
            AddToClassList("cd-tchip");
            this.AddManipulator(new Clickable(() =>
            {
                Value = !Value;
                onChange(Value);
            }));
        }

        public bool Value
        {
            get => ClassListContains("on");
            set => EnableInClassList("on", value);
        }
    }

    /// <summary>クリックでメニューを出す札（曲の選択・MIDI 入力など）。</summary>
    public sealed class MenuChip : VisualElement
    {
        readonly Label label;

        public MenuChip(Action<VisualElement> showMenu, string tooltip = null)
        {
            this.tooltip = tooltip;
            AddToClassList("cd-mchip");
            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("cd-mchip-text");
            Add(label);
            var arrow = new Label("▾") { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("cd-mchip-arrow");
            Add(arrow);
            this.AddManipulator(new Clickable(() => showMenu(this)));
        }

        public string Text
        {
            get => label.text;
            set => label.text = value;
        }
    }
}
