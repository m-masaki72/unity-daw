using UniDAW.Midi;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>下端の状態表示：再生位置・カーソル・入力設定・MIDI・エフェクト説明。</summary>
    public sealed class StatusBar : Label
    {
        readonly ComposerContext ctx;

        public StatusBar(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-status");
        }

        public void Refresh()
        {
            var st = ctx.State;
            if (st.song == null)
            {
                text = "Song アセットを選ぶか、File > New Song / Create Demo Assets から始めてくださいませ。";
                return;
            }
            var dev = MidiInput.IsOpen ? MidiInput.OpenDeviceName : "なし";
            var e = ctx.Transport.Engine;
            string play = e != null && e.IsPlaying ? $"▶ Order {e.PlayingOrder:X2} Pat {e.PlayingPattern:X2} Row {e.PlayingRow:X2}" : "■ Stop";
            var help = ctx.Session.FxHelpAtCursor();
            text = $"{play}   |   Cursor {Song.ChannelNames[st.cursorCh]} Row {st.cursorRow:X2}   Oct {st.octave}  Step {st.editStep}   |   MIDI: {dev}{(st.recording ? "  ● REC" : "")}{(help != null ? "   |   " + help : "")}";
        }
    }
}
