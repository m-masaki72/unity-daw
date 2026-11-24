using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>中央上部：パターンの切替・名前・長さ・追加/複製。</summary>
    public sealed class PatternBar : VisualElement
    {
        readonly ComposerContext ctx;
        readonly Label label;
        readonly TextField nameField;
        readonly IntegerField lenField;

        public PatternBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            AddToClassList("cd-patbar");
            Add(new Button(() => s.SelectPattern(ctx.State.patternIndex - 1)) { text = "◀" });
            label = new Label();
            label.AddToClassList("cd-patlabel");
            Add(label);
            Add(new Button(() => s.SelectPattern(ctx.State.patternIndex + 1)) { text = "▶" });
            nameField = new TextField { isDelayed = true };
            nameField.AddToClassList("cd-patname");
            nameField.RegisterValueChangedCallback(e => s.RenamePattern(e.newValue));
            Add(nameField);
            lenField = new IntegerField("Len") { isDelayed = true };
            lenField.AddToClassList("cd-small");
            lenField.RegisterValueChangedCallback(e => s.ResizePattern(e.newValue));
            Add(lenField);
            Add(new Button(s.NewPattern) { text = "New", tooltip = "空のパターンを追加" });
            Add(new Button(s.ClonePattern) { text = "Clone", tooltip = "現在のパターンを複製" });
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            var p = ctx.State.CurrentPattern;
            if (song == null || p == null)
            {
                label.text = "(Song なし)";
                return;
            }
            label.text = $"Pattern {ctx.State.patternIndex:X2}/{song.patterns.Count - 1:X2}";
            nameField.SetValueWithoutNotify(p.name);
            lenField.SetValueWithoutNotify(p.length);
        }
    }
}
