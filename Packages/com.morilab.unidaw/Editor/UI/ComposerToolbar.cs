using System.IO;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 上部のトランスポートバー：メニュー・曲・再生操作・位置表示（BPM・キー）・レベル・MIDI 入力・ゾーンの開閉。
    /// 編集中のパターンに関する操作は <see cref="EditorHeader"/>、曲の設定は右パネルの <see cref="SongPanel"/> に置く。
    /// Unity 標準のツールバー部品は使わず、<c>Controls/</c> の自作部品で組む。
    /// </summary>
    public sealed class ComposerToolbar : VisualElement
    {

        readonly ComposerContext ctx;
        readonly MenuChip songChip, midiChip;
        readonly IconButton playButton, recButton, loopButton;
        readonly Label lcdState, lcdOrder, lcdPattern, lcdRow, lcdBpm, lcdKey;
        readonly VisualElement lcd;
        readonly ToggleChip clickChip, preChip, leftChip, bandChip, lowerChip, rightChip;
        readonly LevelMeter meter;

        public ComposerToolbar(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            var t = ctx.Transport;
            AddToClassList("cd-tbar");

            // ---- メニュー・曲 ----
            var left = Group();
            var menuButton = new IconButton(Glyph.Menu, null, null, "ファイル");
            menuButton.AddToClassList("flat");
            menuButton.AddManipulator(new Clickable(() => ShowFileMenu(menuButton)));
            left.Add(menuButton);
            songChip = new MenuChip(ShowSongMenu, "編集する Song（Project からドラッグでも開けます）");
            songChip.AddToClassList("cd-songchip");
            left.Add(songChip);
            Add(left);
            Add(Separator());

            // ---- 再生操作 ----
            var transport = Group();
            // Cubase / Studio One と同じ並び：先頭へ・停止・再生・録音・サイクル
            var toStart = new IconButton(Glyph.ToStart, "START", () => { s.GoToStart(); ctx.FocusMain(); }, "先頭へ (Num .)");
            var stop = new IconButton(Glyph.Stop, "STOP", () => { t.Stop(); ctx.FocusMain(); }, "停止 (Num 0 / Esc)");
            playButton = new IconButton(Glyph.Play, "PLAY", () => { t.TogglePlay(); ctx.FocusMain(); },
                "再生／停止 (Space)。サイクルがオンならパターンをループ、オフなら順序表の位置から曲を再生");
            recButton = new IconButton(Glyph.Record, "REC", () => s.SetRecording(!ctx.State.recording),
                "MIDI 入力を記録 (Num *)。停止中はステップ入力、再生中はリアルタイム録音");
            loopButton = new IconButton(Glyph.Loop, "CYCLE", () => { t.ToggleLoop(); ctx.FocusMain(); },
                "サイクル (Num /)。オン＝パターンをループ、オフ＝曲を通して再生");
            playButton.AddToClassList("play");
            recButton.AddToClassList("rec");
            loopButton.AddToClassList("loop");
            transport.Add(toStart);
            transport.Add(stop);
            transport.Add(playButton);
            transport.Add(recButton);
            transport.Add(loopButton);
            clickChip = new ToggleChip("CLICK", _ => { t.ToggleMetronome(); ctx.FocusMain(); }, "メトロノーム (Alt+C)。再生中に拍ごとにクリック（書き出しには入りません）");
            preChip = new ToggleChip("PRE", _ => { t.ToggleCountIn(); ctx.FocusMain(); }, "プリカウント。録音オンで再生を始めると 1 小節数えてから始めます");
            transport.Add(clickChip);
            transport.Add(preChip);
            Add(transport);

            // ---- 位置表示（液晶風） ----
            lcd = new VisualElement();
            lcd.AddToClassList("cd-lcd");
            lcdState = new Label();
            lcdState.AddToClassList("cd-lcd-state");
            lcd.Add(lcdState);
            lcdOrder = LcdField("ORDER");
            lcdPattern = LcdField("PATTERN");
            lcdRow = LcdField("ROW");
            lcdBpm = LcdField("BPM");
            lcdKey = LcdField("KEY");
            // BPM・キーはクリックで右パネルの Song 欄を開いて編集
            foreach (var f in new[] { lcdBpm.parent, lcdKey.parent })
            {
                f.AddToClassList("cd-lcd-link");
                f.tooltip = "クリックで右パネルの Song 欄を開く（BPM / Rows/Beat / Speed / キー）";
                f.AddManipulator(new Clickable(s.ShowRightZone));
            }
            Add(lcd);
            Add(Separator());

            var spacer = new VisualElement();
            spacer.AddToClassList("cd-tb-spacer");
            Add(spacer);

            // ---- レベル・MIDI 入力 ----
            meter = new LevelMeter(Song.ChannelCount) { tooltip = "チャンネルごとの出力レベル" };
            Add(meter);
            midiChip = new MenuChip(ShowMidiMenu, "MIDI 入力デバイス");
            midiChip.AddToClassList("cd-midichip");
            Add(midiChip);

            // ---- ゾーンの開閉（Cubase の Left / Lower / Right Zone）----
            var zones = Group();
            leftChip = new ToggleChip("LEFT", _ => s.ToggleLeftZone(), "左ゾーン：パターン一覧（Ctrl+Alt+L）");
            bandChip = new ToggleChip("ARRANGE", _ => s.ToggleBandZone(), "上部のアレンジ帯（Ctrl+Alt+A）");
            lowerChip = new ToggleChip("LOWER", _ => s.ToggleLowerZone(), "下ゾーン：ミキサーなど（Ctrl+Alt+E）");
            rightChip = new ToggleChip("RIGHT", _ => s.ToggleRightZone(), "右ゾーン：インスペクター（Ctrl+Alt+R）");
            zones.Add(leftChip);
            zones.Add(bandChip);
            zones.Add(lowerChip);
            zones.Add(rightChip);
            Add(zones);
            var keys = new Button(() => s.SetKeyHelp(!ctx.State.keyHelpOpen)) { text = "?", tooltip = "キー操作の一覧（F1）" };
            keys.AddToClassList("cd-tb-help");
            Add(keys);
        }

        static VisualElement Group()
        {
            var g = new VisualElement();
            g.AddToClassList("cd-tb-group");
            return g;
        }

        static VisualElement Separator()
        {
            var v = new VisualElement();
            v.AddToClassList("cd-tb-sep");
            return v;
        }

        Label LcdField(string caption)
        {
            var f = new VisualElement();
            f.AddToClassList("cd-lcd-field");
            var cap = new Label(caption);
            cap.AddToClassList("cd-lcd-cap");
            var val = new Label("--");
            val.AddToClassList("cd-lcd-val");
            f.Add(cap);
            f.Add(val);
            lcd.Add(f);
            return val;
        }

        // =====================================================================
        // メニュー
        // =====================================================================

        static void Item(GenericMenu m, string path, bool enabled, GenericMenu.MenuFunction action, bool on = false)
        {
            if (enabled) m.AddItem(new UnityEngine.GUIContent(path), on, action);
            else m.AddDisabledItem(new UnityEngine.GUIContent(path), on);
        }

        void ShowFileMenu(VisualElement anchor)
        {
            var f = ctx.Files;
            bool has = ctx.Session.Song != null;
            var m = new GenericMenu();
            Item(m, "New Song...", true, f.NewSong);
            Item(m, "Demo Samples (Package Manager)...", true, f.OpenSamples);
            m.AddSeparator("");
            Item(m, "Import MIDI.../Auto", true, () => f.ImportMidi(VoiceAssignMode.Auto));
            Item(m, "Import MIDI.../By Track", true, () => f.ImportMidi(VoiceAssignMode.ByTrack));
            Item(m, "Import MIDI.../Split By Pitch", true, () => f.ImportMidi(VoiceAssignMode.SplitByPitch));
            m.AddSeparator("");
            Item(m, "Export WAV...", has, f.ExportWav);
            Item(m, "Export Stems (WAV per channel)...", has, f.ExportStems);
            Item(m, "Export MIDI...", has, f.ExportMidi);
            m.AddSeparator("");
            foreach (DawKeyPreset p in System.Enum.GetValues(typeof(DawKeyPreset)))
            {
                var preset = p;
                Item(m, "Key Commands/" + DawKeys.DisplayName(preset), true, () => ctx.Session.SetKeyPreset(preset), DawKeys.Preset == preset);
            }
            m.DropDown(anchor.worldBound);
        }

        void ShowSongMenu(VisualElement anchor)
        {
            var s = ctx.Session;
            var current = s.Song;
            var m = new GenericMenu();
            var paths = UniDAWAssets.FindSongPaths();
            if (paths.Length == 0) Item(m, "(Song がありません)", false, null);
            foreach (var p in paths)
            {
                string path = p;
                // メニューの区切り文字 "/" を避けて「名前  —  フォルダ」の形で出す
                string label = Path.GetFileNameWithoutExtension(path) + "   —   " + Path.GetDirectoryName(path).Replace('\\', '∕').Replace('/', '∕');
                bool on = current != null && AssetDatabase.GetAssetPath(current) == path;
                Item(m, label, true, () => s.SetSong(AssetDatabase.LoadAssetAtPath<Song>(path)), on);
            }
            m.AddSeparator("");
            Item(m, "Ping in Project", current != null, () => EditorGUIUtility.PingObject(current));
            Item(m, "New Song...", true, ctx.Files.NewSong);
            m.DropDown(anchor.worldBound);
        }

        void ShowMidiMenu(VisualElement anchor)
        {
            var s = ctx.Session;
            var m = new GenericMenu();
            var names = MidiInput.GetDeviceNames();
            if (names.Length == 0) Item(m, "(デバイスなし)", false, null);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                string n = names[i];
                Item(m, $"{i}: {n}", true, () =>
                {
                    if (!MidiInput.Open(idx)) s.Notify($"MIDI デバイス {n} を開けませんでしたわ");
                    s.NotifyChanged();
                }, MidiInput.OpenDeviceName == n);
            }
            m.AddSeparator("");
            Item(m, "Close", MidiInput.IsOpen, () => { MidiInput.Close(); s.NotifyChanged(); });
            m.DropDown(anchor.worldBound);
        }

        // =====================================================================
        // 再表示
        // =====================================================================

        public void Refresh()
        {
            var st = ctx.State;
            songChip.Text = st.song != null ? st.song.name : "Song を選択";
            songChip.EnableInClassList("empty", st.song == null);
            recButton.Active = st.recording;
            loopButton.Active = st.loop;
            clickChip.Value = st.metronome;
            preChip.Value = st.countIn;
            leftChip.Value = st.leftOpen;
            bandChip.Value = st.bandOpen;
            lowerChip.Value = st.lowerOpen;
            rightChip.Value = st.rightOpen;
            midiChip.Text = MidiInput.IsOpen ? "MIDI  " + MidiInput.OpenDeviceName : "MIDI  —";
            midiChip.EnableInClassList("on", MidiInput.IsOpen);
            if (st.song != null)
            {
                Set(lcdBpm, st.song.bpm.ToString("0.#"));
                var key = st.song.Key;
                Set(lcdKey, key.IsChromatic ? "—" : Scale.RootNames[key.root] + " " + Scale.ModeNames[(int)key.mode]);
            }
            UpdatePlayhead();
        }

        /// <summary>毎フレーム呼ぶ：再生状態・位置・レベル。</summary>
        public void UpdatePlayhead()
        {
            var st = ctx.State;
            var e = ctx.Transport.Engine;
            bool playing = e != null && e.IsPlaying;
            playButton.Active = playing;
            meter.Push(playing ? e.Levels : null);

            lcd.EnableInClassList("playing", playing);
            Set(lcdState, playing ? (e.CountingIn ? "COUNT" : e.IsPatternMode ? "LOOP" : "PLAY") : "STOP");
            Set(lcdOrder, (playing ? e.PlayingOrder : st.orderIndex).ToString("X2"));
            Set(lcdPattern, (playing ? e.PlayingPattern : st.patternIndex).ToString("X2"));
            Set(lcdRow, (playing ? e.PlayingRow : st.cursorRow).ToString("X2"));
        }

        static void Set(Label l, string text)
        {
            if (l.text != text) l.text = text;
        }
    }
}
