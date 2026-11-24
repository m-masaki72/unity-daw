using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>キー操作をどの DAW に合わせるか（EditorPrefs に int で保存されるので値は固定）。</summary>
    public enum DawKeyPreset { Cubase = 0, StudioOne = 1 }

    /// <summary>
    /// DAW ごとに違うキー割り当て（ツール切替・複製・ナッジ）。共通部分（Space / テンキー / Ctrl+A 等）は
    /// <see cref="ComposerKeymap"/> が直接持ち、ここには差がある所だけを置く。
    /// 既定値は Cubase Pro / Studio One Pro の初期キーコマンド表に合わせている。
    /// </summary>
    public static class DawKeys
    {
        const string PrefKey = "UniDAW.KeyPreset";

        static DawKeyPreset? cached;

        /// <summary>選択中のプリセット（ユーザー設定としてプロジェクトをまたいで保存）。</summary>
        public static DawKeyPreset Preset
        {
            get => cached ??= (DawKeyPreset)EditorPrefs.GetInt(PrefKey, (int)DawKeyPreset.Cubase);
            set
            {
                cached = value;
                EditorPrefs.SetInt(PrefKey, (int)value);
            }
        }

        public static string DisplayName(DawKeyPreset p) => p == DawKeyPreset.StudioOne ? "Studio One" : "Cubase";

        /// <summary>
        /// 数字キー → ツール。Cubase: 1 選択 / 5 消しゴム / 8 鉛筆、
        /// Studio One: 1 矢印 / 4 消しゴム / 5 ペイント。該当しなければ null。
        /// </summary>
        public static RollTool? ToolForKey(DawKeyPreset p, KeyCode k)
        {
            int n = k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9 ? k - KeyCode.Alpha0 : -1;
            if (p == DawKeyPreset.StudioOne)
            {
                switch (n)
                {
                    case 1: return RollTool.Select;
                    case 4: return RollTool.Erase;
                    case 5: return RollTool.Draw;
                }
                return null;
            }
            switch (n)
            {
                case 1: return RollTool.Select;
                case 5: return RollTool.Erase;
                case 8: return RollTool.Draw;
            }
            return null;
        }

        /// <summary>複製：Cubase は Ctrl+D、Studio One は D（Studio One の Ctrl+D は選択解除）。</summary>
        public static bool IsDuplicate(DawKeyPreset p, KeyCode k, bool ctrl) =>
            k == KeyCode.D && (p == DawKeyPreset.StudioOne ? !ctrl : ctrl);

        /// <summary>選択解除：Studio One の Ctrl+D（Cubase は Esc 相当が無いので割り当てない）。</summary>
        public static bool IsDeselect(DawKeyPreset p, KeyCode k, bool ctrl) =>
            p == DawKeyPreset.StudioOne && ctrl && k == KeyCode.D;

        /// <summary>ナッジ（左右へ 1 行ずらす）：Cubase は Ctrl+←→、Studio One は Alt+←→。</summary>
        public static bool IsNudgeModifier(DawKeyPreset p, bool ctrl, bool alt) =>
            p == DawKeyPreset.StudioOne ? alt : ctrl;

        /// <summary>ツールの名前と、そのプリセットでのキー（表示用）。</summary>
        public static string ToolLabel(DawKeyPreset p, RollTool t)
        {
            bool s1 = p == DawKeyPreset.StudioOne;
            switch (t)
            {
                case RollTool.Draw: return s1 ? "Paint (5)" : "Draw (8)";
                case RollTool.Erase: return s1 ? "Eraser (4)" : "Erase (5)";
                default: return s1 ? "Arrow (1)" : "Select (1)";
            }
        }
    }
}
