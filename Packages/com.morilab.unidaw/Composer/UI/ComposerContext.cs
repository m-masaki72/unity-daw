using System;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// UI 部品（パネル・ビュー）が使う窓口一式。部品はウィンドウではなくこれに依存するので、
    /// 別のウィンドウや配置にも組み込める。
    /// </summary>
    public sealed class ComposerContext
    {
        readonly ComposerRoot root;

        /// <summary>画面の Composer 層をそのまま見せる（Commands を足しても ComposerRoot に足すだけでよい）。</summary>
        public ComposerContext(ComposerRoot root) => this.root = root;

        public ComposerSession Session => root.Session;
        public CellCommands Cells => root.Cells;
        public RollEditor Roll => root.Roll;
        public ISongFiles Files => root.Files;
        public MixerCommands Mixer => root.Mixer;
        public OrderCommands Order => root.Order;
        public AssistCommands Assist => root.Assist;
        public MasteringCommands Mastering => root.Mastering;
        public Transport Transport => Session.Transport;
        public ComposerState State => Session.State;
        public IDawPlatform Platform => Session.Platform;

        /// <summary>この画面のホストに出すメニュー。</summary>
        public DawMenu NewMenu() => new DawMenu(Platform);

        /// <summary>キー入力をパターン編集エリアへ戻す。</summary>
        public void FocusMain() => root.FocusMain();

        /// <summary>ラベル付きフィールドの入力欄を詰める（ツールバー等の小さい欄用）。</summary>
        public static T Small<T>(T field, int inputWidth) where T : VisualElement
        {
            field.AddToClassList("cd-small");
            var input = field.Q(className: "unity-base-field__input");
            if (input != null) input.style.width = inputWidth;
            return field;
        }

        /// <summary>ボタンを横に並べる行。</summary>
        public static VisualElement ButtonRow(params Button[] buttons)
        {
            var row = new VisualElement();
            row.AddToClassList("cd-btnrow");
            foreach (var b in buttons) row.Add(b);
            return row;
        }

        public static Label Title(string text)
        {
            var l = new Label(text);
            l.AddToClassList("cd-panel-title");
            return l;
        }
    }
}
