using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 下ゾーンの Master タブ：マスターチェーン（EQ・幅・コンプ・ゲイン・リミッター）と、
    /// 曲の解析（ラウドネス・ピーク・帯域・チャンネルごとの音量・改善のヒント）、目標ラウドネスへの自動調整。
    /// </summary>
    public sealed class MasterView : VisualElement
    {
        static readonly (MasterParam p, string cap, string tip)[] Params =
        {
            (MasterParam.EqLow, "LO", "マスター EQ ロー（120 Hz シェルフ）"),
            (MasterParam.EqMid, "MID", "マスター EQ ミッド（1 kHz）"),
            (MasterParam.EqHigh, "HI", "マスター EQ ハイ（6 kHz シェルフ）"),
            (MasterParam.Width, "WIDTH", "ステレオ幅（0% = モノラル、100% = そのまま、200% = 広げる）"),
            (MasterParam.Threshold, "THR", "コンプのスレッショルド：これを超えた分を圧縮する"),
            (MasterParam.Ratio, "RATIO", "コンプのレシオ（1:1 = 掛けない）。大きいほど強く圧縮する"),
            (MasterParam.Gain, "GAIN", "リミッター前のゲイン。上げるほど音が大きくなる（リミッターで頭を抑える）"),
            (MasterParam.Ceiling, "CEIL", "リミッターの上限（dBFS）。配信向けは -1 dB が目安"),
        };

        readonly ComposerContext ctx;
        readonly ToggleChip onChip, limitChip;
        readonly Fader[] faders = new Fader[SongMaster.ParamCount];
        readonly Label[] values = new Label[SongMaster.ParamCount];
        readonly Label reduction, status, report;
        readonly MenuChip target;
        readonly Button analyze, match;
        float grHold;

        MasteringCommands M => ctx.Mastering;

        public MasterView(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-mixer");
            AddToClassList("cd-master");

            // ---- チェーン
            var chain = new VisualElement();
            chain.AddToClassList("cd-strip");
            chain.AddToClassList("cd-fxstrip");
            chain.AddToClassList("cd-master-chain");
            var title = new Label("Master Chain");
            title.AddToClassList("cd-strip-name");
            chain.Add(title);
            var row = new VisualElement();
            row.AddToClassList("cd-btnrow");
            onChip = new ToggleChip("ON", v => M.SetEnabled(v), "マスターチェーンを使う（OFF で素通し。聴き比べに）");
            limitChip = new ToggleChip("LIMIT", v => M.SetLimiter(v), "リミッター：上限（CEIL）を超えないように音量を抑える");
            var reset = new Button(() => M.ResetChain()) { text = "Reset", tooltip = "EQ・幅・コンプ・ゲイン・リミッターを初期値に戻す" };
            reset.AddToClassList("cd-strip-clear");
            row.Add(onChip);
            row.Add(limitChip);
            row.Add(reset);
            chain.Add(row);
            for (int i = 0; i < Params.Length; i++)
            {
                if (i == 0) chain.Add(Caption("EQ / Width"));
                if (i == 4) chain.Add(Caption("Comp / Limiter"));
                AddFader(chain, i);
            }
            reduction = new Label { tooltip = "ゲインリダクション：コンプ・リミッターが下げている量（再生中）" };
            reduction.AddToClassList("cd-master-gr");
            chain.Add(reduction);
            Add(chain);

            // ---- 解析
            var an = new VisualElement();
            an.AddToClassList("cd-master-analysis");
            var at = new Label("Analysis");
            at.AddToClassList("cd-strip-name");
            an.Add(at);
            var arow = new VisualElement();
            arow.AddToClassList("cd-btnrow");
            target = new MenuChip(ShowTargetMenu, "目標ラウドネス（統合 LUFS）。配信サービスは -14 前後で再生音量をそろえる");
            analyze = new Button(() => M.Analyze()) { text = "解析", tooltip = "曲を 1 周レンダリングして、ラウドネス・ピーク・帯域・チャンネルごとの音量を測る" };
            match = new Button(() => M.MatchLoudness()) { text = "ラウドネスを合わせる", tooltip = "目標ラウドネスになるようにマスターのゲインを決める（マスターとリミッターも ON になる）" };
            arow.Add(target);
            arow.Add(analyze);
            arow.Add(match);
            an.Add(arow);
            status = new Label();
            status.AddToClassList("cd-master-status");
            an.Add(status);
            report = new Label();
            report.AddToClassList("cd-master-report");
            an.Add(report);
            Add(an);
        }

        static Label Caption(string text)
        {
            var l = new Label(text);
            l.AddToClassList("cd-fx-caption");
            return l;
        }

        void AddFader(VisualElement parent, int i)
        {
            var (p, cap, tip) = Params[i];
            var (min, max, def) = SongMaster.Range(p);
            var row = new VisualElement();
            row.AddToClassList("cd-send");
            var c = new Label(cap);
            c.AddToClassList("cd-send-cap");
            row.Add(c);
            float step = p == MasterParam.Width ? 0.05f : p == MasterParam.Ratio ? 0.5f : 0.5f;
            var f = new Fader(false, min, max, def, step, bipolar: p <= MasterParam.EqHigh || p == MasterParam.Gain);
            f.tooltip = tip + "\n" + f.tooltip;
            f.Dragged += v => M.Preview(p, v);
            f.Committed += (a, b) => M.Commit(p, a, b);
            row.Add(f);
            var val = new Label();
            val.AddToClassList("cd-send-val");
            row.Add(val);
            parent.Add(row);
            faders[i] = f;
            values[i] = val;
        }

        void ShowTargetMenu(VisualElement anchor)
        {
            var menu = new UnityEditor.GenericMenu();
            for (int i = 0; i < SongMaster.TargetChoices.Length; i++)
            {
                float v = SongMaster.TargetChoices[i];
                menu.AddItem(new GUIContent(SongMaster.TargetNames[i]), Mathf.Approximately(M.TargetLufs, v), () => M.SetTarget(v));
            }
            menu.DropDown(anchor.worldBound);
        }

        public void Refresh()
        {
            var m = M;
            bool has = m.Master != null;
            onChip.Value = m.Enabled;
            limitChip.Value = m.Limiter;
            for (int i = 0; i < faders.Length; i++)
                if (!faders[i].IsDragging) faders[i].SetValueWithoutNotify(m.Get(Params[i].p));
            target.Text = "目標 " + m.TargetLufs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " LUFS";
            analyze.SetEnabled(has && !m.Busy);
            match.SetEnabled(has && !m.Busy && !ctx.Session.IsReadOnly);
            status.text = m.Status;
            status.style.display = string.IsNullOrEmpty(m.Status) ? DisplayStyle.None : DisplayStyle.Flex;
            report.text = m.Report != null ? m.Report.Text() : "「解析」で曲のラウドネス・ピーク・帯域のバランスを測ります。";
            EnableInClassList("off", !m.Enabled);
            UpdateLabels();
        }

        /// <summary>毎フレーム：数値表示とゲインリダクション。</summary>
        public void UpdateMeters()
        {
            var e = ctx.Transport.Engine;
            float gr = e != null && ctx.Transport.IsPlaying && M.Enabled ? e.MasterReductionDb : 0f;
            grHold = gr < grHold ? gr : grHold * 0.92f;
            UpdateLabels();
        }

        void UpdateLabels()
        {
            for (int i = 0; i < faders.Length; i++)
            {
                string t = SongMaster.FormatParam(Params[i].p, faders[i].Value);
                if (values[i].text != t) values[i].text = t;
            }
            string g = "GR " + (grHold > -0.05f ? "0.0" : grHold.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) + " dB";
            if (reduction.text != g) reduction.text = g;
        }
    }
}
