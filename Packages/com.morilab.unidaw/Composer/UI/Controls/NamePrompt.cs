using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// 名前を 1 つ聞く小さな画面（ファイルダイアログの無いホスト用）。Enter / OK で決定、Esc / 枠の外 / キャンセルでやめる。
    /// </summary>
    public sealed class NamePrompt : VisualElement
    {
        readonly TextField field;
        readonly Action<string> ok;

        NamePrompt(string title, string label, string initial, Action<string> ok)
        {
            this.ok = ok;
            AddToClassList("cd-keyhelp");
            AddToClassList("cd-prompt");
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) Close();
            });
            var card = new VisualElement();
            card.AddToClassList("cd-keyhelp-card");
            card.AddToClassList("cd-prompt-card");
            card.Add(ComposerContext.Title(title));
            var hint = new Label(label);
            hint.AddToClassList("cd-prompt-label");
            card.Add(hint);
            field = new TextField { value = initial ?? "" };
            field.AddToClassList("cd-prompt-field");
            // Enter は入力欄で決定、Esc はボタンにフォーカスがあっても閉じる（画面のキー処理には渡さない）
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                Submit();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                Close();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            card.Add(field);
            var row = new VisualElement();
            row.AddToClassList("cd-prompt-buttons");
            row.Add(new Button(Close) { text = "キャンセル" });
            var okButton = new Button(Submit) { text = "OK" };
            okButton.AddToClassList("cd-prompt-ok");
            row.Add(okButton);
            card.Add(row);
            Add(card);
        }

        /// <summary><paramref name="root"/> の上に出す。空の名前では決定しない。</summary>
        public static NamePrompt Show(VisualElement root, string title, string label, string initial, Action<string> ok)
        {
            var p = new NamePrompt(title, label, initial, ok);
            root.Add(p);
            p.schedule.Execute(() =>
            {
                p.field.Focus();
                p.field.SelectAll();
            });
            return p;
        }

        public string Value
        {
            get => field.value;
            set => field.value = value;
        }

        public void Submit()
        {
            var name = field.value?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            Close();
            ok(name);
        }

        public void Close() => RemoveFromHierarchy();
    }
}
