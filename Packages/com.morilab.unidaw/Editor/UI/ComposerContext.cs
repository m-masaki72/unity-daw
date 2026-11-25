using System;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// UI 部品（パネル・ビュー）が使う窓口一式。部品はウィンドウではなくこれに依存するので、
    /// 別のウィンドウや配置にも組み込める。
    /// </summary>
    public sealed class ComposerContext
    {
        public ComposerSession Session { get; }
        public CellCommands Cells { get; }
        public RollEditor Roll { get; }
        public SongFileCommands Files { get; }
        public MixerCommands Mixer { get; }
        public OrderCommands Order { get; }
        public AssistCommands Assist { get; }
        public Transport Transport => Session.Transport;
        public ComposerState State => Session.State;

        readonly Action focusMain;

        public ComposerContext(ComposerSession session, CellCommands cells, RollEditor roll, SongFileCommands files, MixerCommands mixer, OrderCommands order, AssistCommands assist, Action focusMain)
        {
            Session = session;
            Cells = cells;
            Roll = roll;
            Files = files;
            Mixer = mixer;
            Order = order;
            Assist = assist;
            this.focusMain = focusMain;
        }

        /// <summary>キー入力をパターン編集エリアへ戻す。</summary>
        public void FocusMain() => focusMain?.Invoke();

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
