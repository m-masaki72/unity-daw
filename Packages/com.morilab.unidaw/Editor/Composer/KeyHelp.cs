using System;
using System.Collections.Generic;
using System.Linq;

namespace UniDAW.Editor
{
    /// <summary>キー早見表の 1 区画（見出しと「キー → 説明」の行）。</summary>
    public sealed class KeyHelpSection
    {
        public readonly string Title;
        /// <summary>この区画がいちばん関係する表示（null = どの表示でも）。</summary>
        public readonly PatternViewKind[] Views;
        public readonly (string keys, string desc)[] Rows;

        public KeyHelpSection(string title, PatternViewKind[] views, params (string keys, string desc)[] rows)
        {
            Title = title;
            Views = views;
            Rows = rows;
        }

        public bool IsFor(PatternViewKind view) => Views != null && Array.IndexOf(Views, view) >= 0;
    }

    /// <summary>キー早見表の中身（プリセットで変わる）と、検索・表示に合わせた並べ替え。UI 非依存。</summary>
    public static class KeyHelp
    {
        static readonly PatternViewKind[] TrackerStep = { PatternViewKind.Tracker, PatternViewKind.Step };
        static readonly PatternViewKind[] Piano = { PatternViewKind.PianoRoll };

        public static KeyHelpSection[] Sections(DawKeyPreset preset)
        {
            bool s1 = preset == DawKeyPreset.StudioOne;
            return new[]
            {
                new KeyHelpSection("再生・画面", null,
                    ("Space", "再生 / 停止"),
                    ("Enter", "再生"),
                    ("Num0 / Esc", "停止"),
                    ("Num .", "先頭へ"),
                    ("Num /", "サイクル（ループ）"),
                    ("Num *", "録音"),
                    ("Num - / Num +", "前 / 次の順序へ"),
                    ("Alt+C", "メトロノーム"),
                    ("F1", "この一覧"),
                    ("F3", "ミキサー"),
                    ("Ctrl+Alt+E", "下ゾーン"),
                    ("Ctrl+Alt+A", "アレンジ帯"),
                    ("Ctrl+Alt+L / R", "左 / 右ゾーン")),
                new KeyHelpSection("トラッカー / ステップ", TrackerStep,
                    ("Z〜M / Q〜P", "ノート入力（2 段鍵盤）"),
                    ("1", "ノートオフ"),
                    ("Del", "消去"),
                    ("0-9 A-F", "数値列（16 進）"),
                    ("0 1 2 3 4 A V F S …", "Fx 列のエフェクト"),
                    ("[ ]", "オクターブ ±1"),
                    ("Tab", "次のチャンネル"),
                    ("PgUp / PgDn", "16 行ずつ移動"),
                    ("Home / End", "先頭 / 末尾の行"),
                    ("Ctrl+↑↓", "半音移調（+Shift で 1 オクターブ）"),
                    ("Ctrl+C / Ctrl+V", "トラックのコピー / 貼り付け"),
                    ("Ctrl+I", "補間（音量列・Fx 列の上下の値の間を直線で）")),
                new KeyHelpSection("ピアノロール", Piano, PianoRows(s1)),
                new KeyHelpSection("順序表（どの表示でも）", null,
                    ("Alt+Ins", "順序を追加"),
                    ("Alt+Del", "順序を外す"),
                    ("Alt+PgUp / PgDn", "前後へ移動"),
                    ("Alt+[ ]", "パターン番号 ±1"),
                    ("Ctrl+Alt+↑↓", "移調（+Shift で 1 オクターブ）"),
                    ("Ctrl+Alt+←→", "Claude 候補の切替")),
                new KeyHelpSection("そのほか", null,
                    (".mid をドロップ", "MIDI のインポート"),
                    ("Edit > Shortcuts", "再生・テンキー・F1 / F3・Alt+C・Ctrl+Alt+… はウィンドウ内のどこでも効く。ここで変更できる")),
            };
        }

        static (string, string)[] PianoRows(bool s1)
        {
            var rows = new List<(string, string)>
            {
                ("ダブルクリック", "ノートの作成 / 削除"),
                ("ドラッグ", "移動（Alt で複製）・右端で長さ"),
                ("空き地をドラッグ", "範囲選択"),
                s1 ? ("1 / 4 / 5", "矢印 / 消しゴム / ペイント") : ("1 / 5 / 8", "選択 / 消しゴム / 鉛筆"),
                ("↑↓", "移調（+Shift で 1 オクターブ）"),
                ("←→", "前後のノート"),
                s1 ? ("Alt+←→", "ナッジ") : ("Ctrl+←→", "ナッジ"),
                s1 ? ("D", "複製") : ("Ctrl+D", "複製"),
            };
            if (s1) rows.Add(("Ctrl+D", "選択解除"));
            rows.Add(("Ctrl+A", "すべて選択"));
            rows.Add(("Ctrl+C / X / V", "コピー / 切り取り / 貼り付け"));
            rows.Add(("Del", "削除"));
            return rows.ToArray();
        }

        /// <summary>
        /// 表示する区画：いまの表示に関係する区画を先頭に、検索語（空白区切りの AND、大文字小文字を区別しない）に合う行だけ残す。
        /// 見出しが合えば、その区画の行はすべて残す。行が残らない区画は外す。
        /// </summary>
        public static List<KeyHelpSection> Arrange(KeyHelpSection[] all, PatternViewKind view, string query)
        {
            var words = (query ?? "").Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<KeyHelpSection>();
            foreach (var sec in all.OrderBy(x => x.IsFor(view) ? 0 : 1))
            {
                if (words.Length == 0 || Matches(sec.Title, words))
                {
                    result.Add(sec);
                    continue;
                }
                var rows = sec.Rows.Where(r => Matches(r.keys + " " + r.desc, words)).ToArray();
                if (rows.Length > 0) result.Add(new KeyHelpSection(sec.Title, sec.Views, rows));
            }
            return result;
        }

        static bool Matches(string text, string[] words) =>
            words.All(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>テキストにした一覧（ステータス表示・テスト用）。</summary>
        public static string Text(DawKeyPreset preset)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("キー操作（").Append(DawKeys.DisplayName(preset)).Append(" 準拠）\n");
            foreach (var sec in Sections(preset))
            {
                sb.Append('\n').Append(sec.Title).Append('\n');
                foreach (var (keys, desc) in sec.Rows) sb.Append(keys).Append(" : ").Append(desc).Append('\n');
            }
            return sb.ToString();
        }
    }
}
