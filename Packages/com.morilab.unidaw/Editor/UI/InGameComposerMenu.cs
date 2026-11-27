using UniDAW.Composer;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>シーンにゲーム内 DAW（<see cref="UniDAWComposer"/>）を置くメニュー。</summary>
    static class InGameComposerMenu
    {
        [MenuItem("GameObject/UniDAW/Composer (In-Game)", false, 10)]
        static void Create(MenuCommand cmd)
        {
            var go = new GameObject("UniDAW Composer");
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            go.AddComponent<UniDAWComposer>();
            Undo.RegisterCreatedObjectUndo(go, "Create UniDAW Composer");
            Selection.activeObject = go;
        }
    }
}
