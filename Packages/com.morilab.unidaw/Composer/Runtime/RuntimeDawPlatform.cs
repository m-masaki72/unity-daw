using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// ゲーム内（プレイヤー）用のホスト。設定は PlayerPrefs、Undo は曲の JSON スナップショット、
    /// メニューは UI Toolkit の <see cref="GenericDropdownMenu"/>、音はシーン上の <see cref="UniDAWPlayer"/> から出す。
    /// 毎フレーム <see cref="BasicDawPlatform.Tick"/> を呼ぶこと（<see cref="UniDAWComposer"/> が呼ぶ）。
    /// </summary>
    public class RuntimeDawPlatform : BasicDawPlatform
    {
        const string PrefPrefix = "UniDAW.";

        readonly IAudioHost audio;
        readonly string dataFolder;

        public RuntimeDawPlatform(IAudioHost audio, string dataFolder = null)
        {
            history = new SnapshotUndo(() => Now);
            this.audio = audio;
            this.dataFolder = dataFolder ?? Path.Combine(Application.persistentDataPath, "UniDAW");
        }

        public override double Now => Time.realtimeSinceStartupAsDouble;

        // ---- 設定
        public override int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(PrefPrefix + key, defaultValue);
        public override void SetInt(string key, int value) => PlayerPrefs.SetInt(PrefPrefix + key, value);
        public override string GetString(string key, string defaultValue) => PlayerPrefs.GetString(PrefPrefix + key, defaultValue);
        public override void SetString(string key, string value) => PlayerPrefs.SetString(PrefPrefix + key, value);
        public override void DeleteKey(string key) => PlayerPrefs.DeleteKey(PrefPrefix + key);
        public override string UserDataFolder => dataFolder;

        public override IAudioHost AudioHost => audio;

        // ---- Undo（曲全体の JSON スナップショット）
        readonly SnapshotUndo history;

        public bool CanUndo => history.CanUndo;
        public bool CanRedo => history.CanRedo;

        public override void RecordUndo(Song song, string name) => history.Record(song, name);

        public bool Undo() => After(history.Undo());
        public bool Redo() => After(history.Redo());

        bool After(bool done)
        {
            if (done) RaiseUndoRedo();
            return done;
        }

        // ---- メニュー
        /// <summary>メニューを出す先（UIDocument のルート）。</summary>
        public VisualElement MenuRoot { get; set; }
        Vector2 lastPointer;

        /// <summary>座標の指定が無いメニュー（右クリック等）を出す位置として、押した場所を覚える。</summary>
        public void TrackPointer(VisualElement root) =>
            root.RegisterCallback<PointerDownEvent>(e => lastPointer = e.position, TrickleDown.TrickleDown);

        public override void ShowMenu(DawMenu menu, Rect? worldRect)
        {
            if (MenuRoot?.panel == null || menu == null) return;
            var dd = new GenericDropdownMenu();
            foreach (var e in menu.Entries)
            {
                // サブメニューは一段に開いて「親 › 子」と見せる
                string label = e.path.Replace("/", " › ");
                if (e.separator) dd.AddSeparator(label);
                else if (e.enabled && e.action != null)
                {
                    var act = e.action;
                    dd.AddItem(label, e.on, () => act());
                }
                else dd.AddDisabledItem(label, e.on);
            }
            dd.DropDown(worldRect ?? new Rect(lastPointer, Vector2.zero), MenuRoot, false);
        }
    }

    /// <summary>シーン上の <see cref="UniDAWPlayer"/> を音の出口にする。</summary>
    public sealed class PlayerAudioHost : IAudioHost
    {
        readonly UniDAWPlayer player;

        public PlayerAudioHost(UniDAWPlayer player)
        {
            this.player = player;
            player.externalControl = true;
            player.playOnStart = false;
        }

        public ChipEngine Engine => player != null ? player.Engine : null;
        public void EnsureRunning() => player?.EnsureSourceRunning();
        public void DispatchEvents() => player?.DispatchEvents();
    }
}
