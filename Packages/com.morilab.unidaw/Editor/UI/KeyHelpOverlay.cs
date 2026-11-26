using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// キー操作の一覧をウィンドウ全体に重ねて出す（F1 / ツールバーの ? で開閉、Esc・背景クリックで閉じる）。
    /// いまの表示に関係する区画を先頭に枠付きで出し、検索欄で行を絞り込める。
    /// </summary>
    public sealed class KeyHelpOverlay : VisualElement
    {
        readonly ComposerContext ctx;
        readonly VisualElement columns;
        readonly TextField search;
        readonly Label presetLabel;
        DawKeyPreset builtPreset = (DawKeyPreset)(-1);
        PatternViewKind builtView = (PatternViewKind)(-1);
        string builtQuery;

        public KeyHelpOverlay(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-keyhelp");
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == this) Close();
            });
            var card = new VisualElement();
            card.AddToClassList("cd-keyhelp-card");
            var head = new VisualElement();
            head.AddToClassList("cd-keyhelp-head");
            head.Add(ComposerContext.Title("Keys"));
            search = new TextField { tooltip = "キーや操作の名前で絞り込む（空白区切りで AND。Esc で閉じる）" };
            search.AddToClassList("cd-keyhelp-search");
            search.textEdition.placeholder = "検索（例：移調、Ctrl、ノート）";
            search.RegisterValueChangedCallback(_ => Rebuild());
            // 入力欄にフォーカスがあるとウィンドウのキー処理は動かないので、Esc はここで閉じる
            search.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                Close();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            head.Add(search);
            presetLabel = new Label();
            presetLabel.AddToClassList("cd-keyhelp-preset");
            head.Add(presetLabel);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            head.Add(spacer);
            head.Add(new Button(Close) { text = "×", tooltip = "閉じる（F1 / Esc）" });
            card.Add(head);
            var sv = new ScrollView();
            sv.style.flexShrink = 1;
            columns = new VisualElement();
            columns.AddToClassList("cd-keyhelp-cols");
            sv.Add(columns);
            card.Add(sv);
            Add(card);
        }

        void Close()
        {
            ctx.Session.SetKeyHelp(false);
            ctx.FocusMain();
        }

        public void Refresh(DawKeyPreset preset, PatternViewKind view)
        {
            if (preset == builtPreset && view == builtView && search.value == builtQuery) return;
            builtPreset = preset;
            builtView = view;
            presetLabel.text = DawKeys.DisplayName(preset) + " 準拠";
            Rebuild();
        }

        void Rebuild()
        {
            builtQuery = search.value;
            columns.Clear();
            var sections = KeyHelp.Arrange(KeyHelp.Sections(builtPreset), builtView, builtQuery);
            if (sections.Count == 0)
            {
                var none = new Label($"「{builtQuery}」に合う操作はありません");
                none.AddToClassList("cd-keyhelp-empty");
                columns.Add(none);
                return;
            }
            foreach (var sec in sections)
            {
                var box = new VisualElement();
                box.AddToClassList("cd-keyhelp-sec");
                box.EnableInClassList("current", sec.IsFor(builtView));
                var title = new Label(sec.IsFor(builtView) ? sec.Title + "（いまの表示）" : sec.Title);
                title.AddToClassList("cd-keyhelp-title");
                box.Add(title);
                foreach (var (keys, desc) in sec.Rows)
                {
                    var row = new VisualElement();
                    row.AddToClassList("cd-keyhelp-row");
                    var k = new Label(keys);
                    k.AddToClassList("cd-keyhelp-key");
                    row.Add(k);
                    var d = new Label(desc);
                    d.AddToClassList("cd-keyhelp-desc");
                    row.Add(d);
                    box.Add(row);
                }
                columns.Add(box);
            }
        }
    }
}
