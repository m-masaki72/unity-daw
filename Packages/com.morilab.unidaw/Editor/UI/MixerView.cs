using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// ミキサー（Cubase の MixConsole 風）：チャンネル毎のパン・ミュート/ソロ・フェーダー・メーターとマスター。
    /// 値は曲に保存され、操作は <see cref="MixerCommands"/> に任せる。
    /// </summary>
    public sealed class MixerView : VisualElement
    {
        sealed class Strip
        {
            public VisualElement root;
            public Fader volume, pan, delay, reverb;
            public readonly Fader[] param = new Fader[4];
            public readonly Label[] paramLabel = new Label[4];
            public Label db, panLabel;
            public Button mute, solo;
            public LevelMeter meter;
        }

        readonly ComposerContext ctx;
        readonly Strip[] strips = new Strip[Song.ChannelCount];
        readonly Fader master;
        readonly Label masterDb;
        readonly LevelMeter masterMeter;
        readonly float[] one = new float[1];
        readonly Fader[] fxFaders = new Fader[6];
        readonly Label[] fxLabels = new Label[6];
        readonly MenuChip delayTime, scSource;
        readonly ToggleChip pingPong;

        public MixerView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-mixer");
            var m = ctx.Mixer;

            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                var s = strips[c] = new Strip { root = new VisualElement() };
                s.root.AddToClassList("cd-strip");
                s.root.AddToClassList("ch" + c);

                var name = new Label(Song.ChannelNames[c]);
                name.AddToClassList("cd-chname");
                name.AddToClassList("cd-strip-name");
                s.root.Add(name);

                s.pan = new Fader(false, -1f, 1f, 0f, 1f / 16f, bipolar: true);
                s.pan.Dragged += v => m.PreviewPan(ch, v);
                s.pan.Committed += (a, b) => m.CommitPan(ch, a, b);
                s.root.Add(s.pan);
                s.panLabel = SmallLabel(s.root, "cd-strip-pan");

                var ms = new VisualElement();
                ms.AddToClassList("cd-btnrow");
                s.mute = MsButton("M", "ミュート", () => m.ToggleMute(ch), null);
                s.solo = MsButton("S", "ソロ（Ctrl+クリックで単独ソロ）", () => m.ToggleSolo(ch), () => m.ExclusiveSolo(ch));
                ms.Add(s.mute);
                ms.Add(s.solo);
                s.root.Add(ms);

                s.delay = SendFader(s.root, "DLY", ch, SendKind.Delay);
                s.reverb = SendFader(s.root, "REV", ch, SendKind.Reverb);
                ParamFader(s, "LO", ch, MixParam.EqLow, "EQ ロー（120 Hz シェルフ、±12 dB）\nダブルクリックで 0 dB");
                ParamFader(s, "MID", ch, MixParam.EqMid, "EQ ミッド（1 kHz、±12 dB）\nダブルクリックで 0 dB");
                ParamFader(s, "HI", ch, MixParam.EqHigh, "EQ ハイ（6 kHz シェルフ、±12 dB）\nダブルクリックで 0 dB");
                ParamFader(s, "DUCK", ch, MixParam.Duck, "サイドチェイン：トリガー元（Send FX の Sidechain）が鳴ったときに下げる深さ");

                var faderRow = new VisualElement();
                faderRow.AddToClassList("cd-strip-faderrow");
                s.volume = new Fader(true, ChannelMix.MinDb, ChannelMix.MaxDb, 0f, 1f);
                s.volume.Dragged += v => m.PreviewVolume(ch, v);
                s.volume.Committed += (a, b) => m.CommitVolume(ch, a, b);
                faderRow.Add(s.volume);
                s.meter = new LevelMeter(1, "c" + c);
                s.meter.AddToClassList("cd-strip-meter");
                faderRow.Add(s.meter);
                s.root.Add(faderRow);
                s.db = SmallLabel(s.root, "cd-strip-db");
                Add(s.root);
            }

            // ---- センドエフェクト
            var fx = new VisualElement();
            fx.AddToClassList("cd-strip");
            fx.AddToClassList("cd-fxstrip");
            var fxTitle = new Label("Send FX");
            fxTitle.AddToClassList("cd-strip-name");
            fx.Add(fxTitle);
            fx.Add(Caption("Delay"));
            var dRow = new VisualElement();
            dRow.AddToClassList("cd-btnrow");
            delayTime = new MenuChip(ShowDelayMenu, "ディレイタイム（拍、テンポに同期）");
            dRow.Add(delayTime);
            pingPong = new ToggleChip("PING", v => m.SetPingPong(v), "左右に交互に跳ねる");
            dRow.Add(pingPong);
            fx.Add(dRow);
            FxFader(fx, "FB", FxParam.DelayFeedback);
            FxFader(fx, "RET", FxParam.DelayReturn);
            fx.Add(Caption("Reverb"));
            FxFader(fx, "SIZE", FxParam.ReverbSize);
            FxFader(fx, "DAMP", FxParam.ReverbDamp);
            FxFader(fx, "RET", FxParam.ReverbReturn);
            fx.Add(Caption("Sidechain"));
            var scRow = new VisualElement();
            scRow.AddToClassList("cd-btnrow");
            scSource = new MenuChip(ShowSourceMenu, "サイドチェインのトリガー元（フェーダー前の音で検出、ミュートしても効く）\n各チャンネルの DUCK で下げる深さを決める");
            scRow.Add(scSource);
            fx.Add(scRow);
            FxFader(fx, "REL", FxParam.DuckRelease);
            Add(fx);

            var mr = new VisualElement();
            mr.AddToClassList("cd-strip");
            mr.AddToClassList("master");
            var title = new Label("Master");
            title.AddToClassList("cd-strip-name");
            mr.Add(title);
            var clear = new Button(m.ClearMuteSolo) { text = "M/S 解除", tooltip = "すべてのミュートとソロを解除" };
            clear.AddToClassList("cd-strip-clear");
            mr.Add(clear);
            var reset = new Button(m.ResetMixer) { text = "Reset", tooltip = "ミキサーを初期値（0 dB・センター）に戻す" };
            reset.AddToClassList("cd-strip-clear");
            mr.Add(reset);
            var row = new VisualElement();
            row.AddToClassList("cd-strip-faderrow");
            master = new Fader(true, ChannelMix.MinDb, ChannelMix.MaxDb, 0f, 1f);
            master.Dragged += m.PreviewMaster;
            master.Committed += m.CommitMaster;
            row.Add(master);
            masterMeter = new LevelMeter(2, "cm");
            masterMeter.AddToClassList("cd-strip-meter");
            row.Add(masterMeter);
            mr.Add(row);
            masterDb = SmallLabel(mr, "cd-strip-db");
            Add(mr);
        }

        static Label Caption(string text)
        {
            var l = new Label(text);
            l.AddToClassList("cd-fx-caption");
            return l;
        }

        Fader SendFader(VisualElement parent, string caption, int ch, SendKind kind)
        {
            var m = ctx.Mixer;
            var row = new VisualElement();
            row.AddToClassList("cd-send");
            var cap = new Label(caption);
            cap.AddToClassList("cd-send-cap");
            row.Add(cap);
            var f = new Fader(false, 0f, 1f, 0f, 1f / 32f);
            f.tooltip = (kind == SendKind.Delay ? "ディレイ" : "リバーブ") + "への送り量\n" + f.tooltip;
            f.Dragged += v => m.PreviewSend(ch, kind, v);
            f.Committed += (a, b) => m.CommitSend(ch, kind, a, b);
            row.Add(f);
            parent.Add(row);
            return f;
        }

        void ParamFader(Strip s, string caption, int ch, MixParam p, string tip)
        {
            var m = ctx.Mixer;
            var (min, max, def) = ChannelMix.Range(p);
            var row = new VisualElement();
            row.AddToClassList("cd-send");
            row.AddToClassList("narrow");
            var cap = new Label(caption);
            cap.AddToClassList("cd-send-cap");
            row.Add(cap);
            var f = new Fader(false, min, max, def, p == MixParam.Duck ? 1f / 32f : 0.5f, bipolar: p != MixParam.Duck);
            f.tooltip = tip + "\n" + f.tooltip;
            f.Dragged += v => m.PreviewParam(ch, p, v);
            f.Committed += (a, b) => m.CommitParam(ch, p, a, b);
            row.Add(f);
            var val = new Label();
            val.AddToClassList("cd-send-val");
            row.Add(val);
            s.root.Add(row);
            s.param[(int)p] = f;
            s.paramLabel[(int)p] = val;
        }

        static string ParamText(MixParam p, float v) =>
            p == MixParam.Duck ? Mathf.RoundToInt(v * 100) + "%" : v.ToString("+0.#;-0.#;0", System.Globalization.CultureInfo.InvariantCulture);

        void ShowSourceMenu(VisualElement anchor)
        {
            var m = ctx.Mixer;
            var menu = new UnityEditor.GenericMenu();
            for (int c = 0; c < Song.ChannelCount; c++)
            {
                int ch = c;
                menu.AddItem(new GUIContent(Song.ChannelNames[c]), m.SidechainSource == c, () => m.SetSidechainSource(ch));
            }
            menu.DropDown(anchor.worldBound);
        }

        void FxFader(VisualElement parent, string caption, FxParam p)
        {
            var m = ctx.Mixer;
            var (min, max, def) = SongEffects.Range(p);
            var row = new VisualElement();
            row.AddToClassList("cd-send");
            var cap = new Label(caption);
            cap.AddToClassList("cd-send-cap");
            row.Add(cap);
            var f = new Fader(false, min, max, def, (max - min) / 48f);
            f.Dragged += v => m.PreviewFx(p, v);
            f.Committed += (a, b) => m.CommitFx(p, a, b);
            row.Add(f);
            var val = new Label();
            val.AddToClassList("cd-send-val");
            row.Add(val);
            parent.Add(row);
            fxFaders[(int)p] = f;
            fxLabels[(int)p] = val;
        }

        void ShowDelayMenu(VisualElement anchor)
        {
            var m = ctx.Mixer;
            var menu = new UnityEditor.GenericMenu();
            for (int i = 0; i < SongEffects.DelayBeatNames.Length; i++)
            {
                int idx = i;
                menu.AddItem(new GUIContent(SongEffects.DelayBeatNames[i]), m.DelayChoice == i, () => m.SetDelayChoice(idx));
            }
            menu.DropDown(anchor.worldBound);
        }

        static string FxText(FxParam p, float v) =>
            p == FxParam.DelayReturn || p == FxParam.ReverbReturn ? ChannelMix.FormatDb(v)
            : p == FxParam.DuckRelease ? Mathf.RoundToInt(SongEffects.ReleaseMs(v)) + "ms"
            : Mathf.RoundToInt(v * 100) + "%";

        static Label SmallLabel(VisualElement parent, string cls)
        {
            var l = new Label();
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        static Button MsButton(string text, string tip, System.Action click, System.Action ctrlClick)
        {
            var b = new Button { text = text, tooltip = tip };
            b.AddToClassList("cd-ms");
            b.clickable = null;
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.actionKey && ctrlClick != null) ctrlClick();
                else click();
                e.StopPropagation();
            });
            return b;
        }

        public void Refresh()
        {
            var m = ctx.Mixer;
            for (int c = 0; c < strips.Length; c++)
            {
                var s = strips[c];
                if (!s.volume.IsDragging) s.volume.SetValueWithoutNotify(m.VolumeDb(c));
                if (!s.pan.IsDragging) s.pan.SetValueWithoutNotify(m.Pan(c));
                s.delay.SetValueWithoutNotify(m.SendLevel(c, SendKind.Delay));
                s.reverb.SetValueWithoutNotify(m.SendLevel(c, SendKind.Reverb));
                for (int i = 0; i < s.param.Length; i++)
                    if (!s.param[i].IsDragging) s.param[i].SetValueWithoutNotify(m.Param(c, (MixParam)i));
                // トリガー元自身は下げない
                s.param[(int)MixParam.Duck].SetEnabled(c != m.SidechainSource);
                s.mute.EnableInClassList("on", m.IsMuted(c));
                s.solo.EnableInClassList("on", m.IsSoloed(c));
                s.root.EnableInClassList("silenced", m.IsSilenced(c));
            }
            master.SetValueWithoutNotify(m.MasterDb);
            for (int i = 0; i < fxFaders.Length; i++) fxFaders[i].SetValueWithoutNotify(m.Fx((FxParam)i));
            delayTime.Text = SongEffects.DelayBeatNames[m.DelayChoice];
            pingPong.Value = m.PingPong;
            scSource.Text = "← " + Song.ChannelNames[m.SidechainSource];
            UpdateLabels();
        }

        /// <summary>毎フレーム：メーターと数値表示（ドラッグ中の値も追う）。</summary>
        public void UpdateMeters()
        {
            var e = ctx.Transport.Engine;
            for (int c = 0; c < strips.Length; c++)
            {
                one[0] = e != null ? e.Levels[c] : 0f;
                strips[c].meter.Push(one);
            }
            masterMeter.Push(e?.OutputLevels);
            UpdateLabels();
        }

        void UpdateLabels()
        {
            for (int c = 0; c < strips.Length; c++)
            {
                var s = strips[c];
                SetText(s.db, ChannelMix.FormatDb(s.volume.Value));
                SetText(s.panLabel, ChannelMix.FormatPan(s.pan.Value));
                for (int i = 0; i < s.param.Length; i++) SetText(s.paramLabel[i], ParamText((MixParam)i, s.param[i].Value));
            }
            SetText(masterDb, ChannelMix.FormatDb(master.Value));
            for (int i = 0; i < fxFaders.Length; i++) SetText(fxLabels[i], FxText((FxParam)i, fxFaders[i].Value));
        }

        static void SetText(Label l, string t)
        {
            if (l.text != t) l.text = t;
        }
    }
}
