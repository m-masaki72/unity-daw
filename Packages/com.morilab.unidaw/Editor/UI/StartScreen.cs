using System.IO;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>Song を開いていないときに中央へ出す開始画面：New / Open / Import MIDI と、プロジェクト内の Song 一覧。</summary>
    public sealed class StartScreen : VisualElement
    {
        const int MaxListed = 12;

        readonly ComposerContext ctx;
        readonly VisualElement list;

        public StartScreen(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-start");
            var card = new VisualElement();
            card.AddToClassList("cd-start-card");
            var title = new Label("UniDAW");
            title.AddToClassList("cd-start-title");
            card.Add(title);
            var sub = new Label("Song を作るか開いて始めてくださいませ。.mid をウィンドウへドロップしても読み込めますわ。");
            sub.AddToClassList("cd-start-sub");
            card.Add(sub);
            var f = ctx.Files;
            card.Add(ComposerContext.ButtonRow(
                new Button(f.NewSong) { text = "New Song", tooltip = "空の Song を作って保存先を選ぶ" },
                new Button(f.OpenSong) { text = "Open…", tooltip = "プロジェクト内の Song アセットを選んで開く" },
                new Button(() => f.ImportMidi(VoiceAssignMode.Auto)) { text = "Import MIDI…", tooltip = "MIDI ファイルを 4ch に割り振って Song にする" },
                new Button(f.CreateDemo) { text = "Demo Song", tooltip = "デモ曲（とサンプルシーン）を作って開く" }));
            var cap = new Label("Songs in project");
            cap.AddToClassList("cd-start-cap");
            card.Add(cap);
            list = new VisualElement();
            list.AddToClassList("cd-start-list");
            card.Add(list);
            Add(card);
        }

        public void Refresh()
        {
            list.Clear();
            var paths = UniDAWAssets.FindSongPaths();
            if (paths.Length == 0)
            {
                var none = new Label("（まだ Song がありません）");
                none.AddToClassList("cd-start-sub");
                list.Add(none);
                return;
            }
            for (int i = 0; i < paths.Length && i < MaxListed; i++)
            {
                string path = paths[i];
                var b = new Button(() => ctx.Session.SetSong(AssetDatabase.LoadAssetAtPath<Song>(path)))
                {
                    text = Path.GetFileNameWithoutExtension(path) + "   " + Path.GetDirectoryName(path).Replace('\\', '/'),
                    tooltip = path,
                };
                b.AddToClassList("cd-start-song");
                list.Add(b);
            }
            if (paths.Length > MaxListed)
            {
                var more = new Label($"ほか {paths.Length - MaxListed} 曲は Open… から");
                more.AddToClassList("cd-start-sub");
                list.Add(more);
            }
        }
    }
}
