using UniDAW.Composer;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// ウィンドウ全体で効かせるキー（再生・トランスポート・表示の開閉）を Unity の Shortcut Manager に登録する。
    /// ウィンドウ内のどこにフォーカスがあっても効き、Edit &gt; Shortcuts の「UniDAW」で割り当てを変えられる。
    /// 中身は <see cref="ComposerKeymap"/> に既定のキーを渡すだけで、一覧は <see cref="ComposerKeymap.WindowCommands"/> と揃える。
    /// Return は Shortcut Manager に割り当てられないので、Enter での再生はキーマップのまま。
    /// </summary>
    static class UniDAWShortcuts
    {
        const ShortcutModifiers CtrlAlt = ShortcutModifiers.Action | ShortcutModifiers.Alt;

        static void Run(ShortcutArguments args, KeyCode key, bool ctrl = false, bool alt = false) =>
            (args.context as Context)?.window.RunWindowCommand(key, ctrl, alt);

        [Shortcut("UniDAW/Play-Stop", typeof(Context), KeyCode.Space)]
        static void PlayStop(ShortcutArguments a) => Run(a, KeyCode.Space);

        [Shortcut("UniDAW/Play (Keypad Enter)", typeof(Context), KeyCode.KeypadEnter)]
        static void PlayKeypad(ShortcutArguments a) => Run(a, KeyCode.KeypadEnter);

        [Shortcut("UniDAW/Stop", typeof(Context), KeyCode.Keypad0)]
        static void Stop(ShortcutArguments a) => Run(a, KeyCode.Keypad0);

        [Shortcut("UniDAW/Go To Start", typeof(Context), KeyCode.KeypadPeriod)]
        static void GoToStart(ShortcutArguments a) => Run(a, KeyCode.KeypadPeriod);

        [Shortcut("UniDAW/Cycle", typeof(Context), KeyCode.KeypadDivide)]
        static void Cycle(ShortcutArguments a) => Run(a, KeyCode.KeypadDivide);

        [Shortcut("UniDAW/Record", typeof(Context), KeyCode.KeypadMultiply)]
        static void Record(ShortcutArguments a) => Run(a, KeyCode.KeypadMultiply);

        [Shortcut("UniDAW/Previous Order", typeof(Context), KeyCode.KeypadMinus)]
        static void PrevOrder(ShortcutArguments a) => Run(a, KeyCode.KeypadMinus);

        [Shortcut("UniDAW/Next Order", typeof(Context), KeyCode.KeypadPlus)]
        static void NextOrder(ShortcutArguments a) => Run(a, KeyCode.KeypadPlus);

        [Shortcut("UniDAW/Key Help", typeof(Context), KeyCode.F1)]
        static void KeyHelp(ShortcutArguments a) => Run(a, KeyCode.F1);

        [Shortcut("UniDAW/Mixer", typeof(Context), KeyCode.F3)]
        static void Mixer(ShortcutArguments a) => Run(a, KeyCode.F3);

        [Shortcut("UniDAW/Metronome", typeof(Context), KeyCode.C, ShortcutModifiers.Alt)]
        static void Metronome(ShortcutArguments a) => Run(a, KeyCode.C, alt: true);

        [Shortcut("UniDAW/Lower Zone", typeof(Context), KeyCode.E, CtrlAlt)]
        static void LowerZone(ShortcutArguments a) => Run(a, KeyCode.E, true, true);

        [Shortcut("UniDAW/Arrange Band", typeof(Context), KeyCode.A, CtrlAlt)]
        static void ArrangeBand(ShortcutArguments a) => Run(a, KeyCode.A, true, true);

        [Shortcut("UniDAW/Left Zone", typeof(Context), KeyCode.L, CtrlAlt)]
        static void LeftZone(ShortcutArguments a) => Run(a, KeyCode.L, true, true);

        [Shortcut("UniDAW/Right Zone", typeof(Context), KeyCode.R, CtrlAlt)]
        static void RightZone(ShortcutArguments a) => Run(a, KeyCode.R, true, true);

        /// <summary>
        /// ウィンドウごとのショートカットの有効範囲。ウィンドウが手前にあり、文字入力欄にフォーカスが無いときだけ有効
        /// （入力欄では Space などをそのまま文字として入れる）。
        /// </summary>
        internal sealed class Context : IShortcutContext
        {
            public readonly UniDAWWindow window;
            public Context(UniDAWWindow window) => this.window = window;
            public bool active => window != null && UnityEditor.EditorWindow.focusedWindow == window && !window.IsTyping;
        }
    }
}
