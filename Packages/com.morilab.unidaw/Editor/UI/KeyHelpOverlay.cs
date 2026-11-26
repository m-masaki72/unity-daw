using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>キー操作の一覧をウィンドウ全体に重ねて出す（F1 / ツールバーの ? で開閉、Esc・背景クリックで閉じる）。</summary>
    public sealed class KeyHelpOverlay : VisualElement
    {
        readonly VisualElement columns;
        string built;

        public KeyHelpOverlay(ComposerContext ctx)
        {
            AddToClassList("cd-keyhelp");
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) ctx.Session.SetKeyHelp(false);
            });
            var card = new VisualElement();
            card.AddToClassList("cd-keyhelp-card");
            var head = new VisualElement();
            head.AddToClassList("cd-keyhelp-head");
            head.Add(ComposerContext.Title("Keys"));
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            head.Add(spacer);
            head.Add(new Button(() => ctx.Session.SetKeyHelp(false)) { text = "×", tooltip = "閉じる（F1 / Esc）" });
            card.Add(head);
            var sv = new ScrollView();
            sv.style.flexShrink = 1;
            columns = new VisualElement();
            columns.AddToClassList("cd-keyhelp-cols");
            sv.Add(columns);
            card.Add(sv);
            Add(card);
        }

        public void Refresh(string helpText)
        {
            if (helpText == built) return;
            built = helpText;
            columns.Clear();
            // 空行で区切られたまとまりごとに 1 枠
            foreach (var block in helpText.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var sec = new Label(block);
                sec.AddToClassList("cd-keyhelp-sec");
                columns.Add(sec);
            }
        }
    }
}
