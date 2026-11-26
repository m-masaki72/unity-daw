using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// エディタの見出し：表示切替・表示ごとの道具・入力設定（OCT / STEP / FOLLOW）・編集中のパターン（◀▶・名前・長さ・New / Clone）。
    /// 「いま編集しているもの」に関する操作はここにまとめ、曲全体の操作は上部バーと右パネルに置く。
    /// </summary>
    public sealed class EditorHeader : VisualElement
    {
        static readonly string[] ViewNames = { "Tracker", "Step", "Piano" };

        readonly ComposerContext ctx;
        readonly SegmentedControl viewSeg;
        readonly VisualElement toolsHost;
        readonly NumberBox octaveBox, stepBox;
        readonly ToggleChip followChip;
        readonly Label label;
        readonly TextField nameField;
        readonly IntegerField lenField;

        public EditorHeader(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            AddToClassList("cd-edhead");

            viewSeg = new SegmentedControl(ViewNames, i => { s.SetView((PatternViewKind)i); ctx.FocusMain(); });
            Add(Group(viewSeg));

            toolsHost = Group();
            Add(toolsHost);

            octaveBox = new NumberBox("OCT", 0, ComposerState.MaxOctave, 1, "0", v => s.SetOctave((int)v), "ノート入力のオクターブ（[ ]）");
            stepBox = new NumberBox("STEP", 0, ComposerState.MaxEditStep, 1, "0", v => s.SetEditStep((int)v), "ノート入力後に進む行数");
            followChip = new ToggleChip("FOLLOW", v => s.SetFollow(v), "再生位置に表示を追従");
            Add(Group(octaveBox, stepBox, followChip));

            var spacer = new VisualElement();
            spacer.AddToClassList("cd-tb-spacer");
            Add(spacer);

            var pat = Group();
            pat.Add(new Button(() => { s.SelectPattern(ctx.State.patternIndex - 1); ctx.FocusMain(); }) { text = "◀", tooltip = "前のパターン" });
            label = new Label();
            label.AddToClassList("cd-patlabel");
            pat.Add(label);
            pat.Add(new Button(() => { s.SelectPattern(ctx.State.patternIndex + 1); ctx.FocusMain(); }) { text = "▶", tooltip = "次のパターン" });
            nameField = new TextField { isDelayed = true, tooltip = "パターン名" };
            nameField.AddToClassList("cd-patname");
            nameField.RegisterValueChangedCallback(e => s.RenamePattern(e.newValue));
            pat.Add(nameField);
            lenField = new IntegerField("LEN") { isDelayed = true, tooltip = "パターンの行数" };
            lenField.AddToClassList("cd-small");
            lenField.RegisterValueChangedCallback(e => s.ResizePattern(e.newValue));
            pat.Add(lenField);
            pat.Add(new Button(() => { s.NewPattern(); ctx.FocusMain(); }) { text = "New", tooltip = "空のパターンを追加" });
            pat.Add(new Button(() => { s.ClonePattern(); ctx.FocusMain(); }) { text = "Clone", tooltip = "現在のパターンを複製" });
            Add(pat);
        }

        static VisualElement Group(params VisualElement[] children)
        {
            var g = new VisualElement();
            g.AddToClassList("cd-edhead-group");
            foreach (var c in children) g.Add(c);
            return g;
        }

        internal void Refresh(IPatternView view)
        {
            var st = ctx.State;
            viewSeg.SetIndex((int)st.view);
            var tools = view?.HeaderTools;
            if (tools == null) toolsHost.Clear();
            else if (tools.parent != toolsHost)
            {
                toolsHost.Clear();
                toolsHost.Add(tools);
            }
            toolsHost.style.display = tools == null ? DisplayStyle.None : DisplayStyle.Flex;
            octaveBox.SetValueWithoutNotify(st.octave);
            stepBox.SetValueWithoutNotify(st.editStep);
            followChip.Value = st.follow;

            var song = st.song;
            var p = st.CurrentPattern;
            if (song == null || p == null)
            {
                label.text = "(Song なし)";
                return;
            }
            label.text = $"{st.patternIndex:X2}/{song.patterns.Count - 1:X2}";
            nameField.SetValueWithoutNotify(p.name);
            lenField.SetValueWithoutNotify(p.length);
        }
    }
}
