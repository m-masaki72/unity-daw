using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// 下端の状態表示：左に操作の結果メッセージ（数秒で消える）、右にカーソル位置でできる操作の説明。
    /// 再生位置・BPM・キーはツールバーの液晶、オクターブ・ステップは編集エリアの見出しにあるので、ここには出さない。
    /// </summary>
    public sealed class StatusBar : VisualElement
    {
        const double MessageSeconds = 5;

        readonly ComposerContext ctx;
        readonly Label message, hint;
        double messageUntil;

        public StatusBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-status");
            message = new Label();
            message.AddToClassList("cd-status-msg");
            Add(message);
            hint = new Label();
            hint.AddToClassList("cd-status-hint");
            Add(hint);
        }

        public string Message => message.text;

        /// <summary>結果のメッセージを出す（<see cref="MessageSeconds"/> 秒で消える）。</summary>
        public void ShowMessage(string text, bool error = false)
        {
            message.text = text ?? "";
            message.EnableInClassList("error", error);
            messageUntil = DawPlatform.Current.Now + MessageSeconds;
        }

        /// <summary>毎フレーム：時間が来たらメッセージを消す。</summary>
        public void Tick()
        {
            if (messageUntil > 0 && DawPlatform.Current.Now >= messageUntil)
            {
                messageUntil = 0;
                message.text = "";
            }
        }

        public void Refresh() => hint.text = Hint(ctx.State, ctx.Session.FxHelpAtCursor());

        /// <summary>カーソル位置・表示に応じた操作の説明（テストからも使う）。</summary>
        public static string Hint(ComposerState st, string fxHelp)
        {
            if (st.song == null) return "New / Open / Import MIDI から始めてくださいませ   F1 キー一覧";
            if (!string.IsNullOrEmpty(fxHelp)) return fxHelp + "   F1 キー一覧";
            switch (st.view)
            {
                case PatternViewKind.PianoRoll:
                    return "ダブルクリック：作成 / 削除   ドラッグ：移動（Alt で複製）・右端で長さ   空き地ドラッグ：範囲選択   F1 キー一覧";
                case PatternViewKind.Step:
                    return "クリック：ステップのオン / オフ   Tab：次のチャンネル   F1 キー一覧";
                default:
                    switch (st.cursorCol)
                    {
                        case 0: return "Z〜M / Q〜P：ノート入力   1：ノートオフ   Del：消去   [ ]：オクターブ   F1 キー一覧";
                        case 1: return "0-9 A-F：楽器番号（16進）   Del：消去   F1 キー一覧";
                        case 2: return "0-9 A-F：音量（16進）   Ctrl+I：上下の値の間を補間   F1 キー一覧";
                        default: return "エフェクト：0 1 2 3 4 A V F S などの文字と 16 進の値   F1 キー一覧";
                    }
            }
        }
    }
}
