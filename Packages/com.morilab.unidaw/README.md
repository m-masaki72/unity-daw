# UniDAW — Unity エディタ内チップチューン DAW（プロトタイプ）

## インストール
Package Manager の「Add package from git URL」で次を指定します：
`<リポジトリURL>.git?path=Packages/com.morilab.unidaw`

アセンブリ：`UniDAW.Runtime`（モデル・音源・MIDI・再生コンポーネント）／`UniDAW.Editor`（エディタウィンドウ・MIDI インポーター）

Unity をエディタとして使う音楽制作向けの DAW です。ステップシーケンサーとトラッカーのよいところを組み合わせ、曲データはアセットとしてシーン・映像表現から直接参照できます。

## 構成
- 4ch：Pulse1 / Pulse2 / Triangle / Noise（NES 風）
- **Window > UniDAW > Composer** でエディタを開く
  - 上部のトランスポートバー：再生（PATTERN / SONG / STOP / REC）、再生位置の表示、BPM などはドラッグ・ホイールで変更（ダブルクリックで直接入力）、チャンネルごとのレベルメーター
  - トラッカー／ステップ／ピアノロールの 3 表示（同じデータを編集、ツールバーの Tracker / Step / Piano で切替）。ピアノロールは左クリックで置く・消す、右クリックでノートオフ
  - パラメータロック（ステップごとの音量・エフェクト・確率・条件 A:B）
  - チャンネルごとのトラック長（ポリメーター）
  - トラッカーエフェクト：`0xy` アルペジオ / `1xx` `2xx` スライド / `3xx` ポルタメント / `4xy` ビブラート / `Axy` 音量スライド / `Vxx` デューティ / `Fxx` スピード・BPM / `Sxx` ノートカット / `Gxx` ノートディレイ / `Qxy` `Rxy` ノートスライド（速度 x・y 半音）。エフェクト列にカーソルを置くとステータスバーに説明を表示
  - 順序表のトランスポーズ（`Tr −` `Tr +` `+12`）：同じパターンを移調して使い回す（ノイズは対象外）
  - インストゥルメント：音量・アルペジオ・デューティのシーケンス（`|` ループ、`/` リリース）
- MIDI
  - `.mid` を Assets に置くと読み取り専用の Song として自動インポート（Inspector で割り当て方式などを変更可）
  - File > Import MIDI（Auto / By Track / Split By Pitch）、Export MIDI / WAV
  - MIDI 入力（Windows / WinMM）：ツールバーの MIDI In からデバイスを選ぶ。● Rec がオンのとき、停止中はステップ入力、再生中はリアルタイム録音
- 映像連携：`UniDAWPlayer`（AudioSource）＋`UniDAWVisualizer`、`onNote` UnityEvent

## コードの層構成
依存は上から下への一方向のみ。フォルダ＝将来 asmdef を分けるときの境界です。

| 層 | フォルダ | 役割 | 依存してよい先 |
|---|---|---|---|
| Model | `Runtime/Model` | Song / Pattern / Cell / Instrument、記法（NoteText・FxText） | UnityEngine（ScriptableObject のみ） |
| Audio | `Runtime/Audio` | `ChipEngine`（シーケンサー）・`ChipChannel`（発音）・`ChipMixer`・`TriggerConditions`・`OfflineRenderer`・WAV | Model（UnityEngine 非依存） |
| Midi | `Runtime/Midi` | SMF 読み書き・Song 変換・MIDI 入力（`UniDAW.Midi`） | Model |
| Unity | `Runtime/Unity` | `UniDAWPlayer` / `UniDAWVisualizer`（シーン用コンポーネント） | Model・Audio |
| Services | `Editor/Services` | アセット入出力・インポーター・CLI・プレビュー音声（`IAudioHost`）・MIDI 入力の橋渡し | Runtime 全体 |
| Composer | `Editor/Composer` | エディタの状態と操作。UIElements に依存しない（`ComposerState`・`ComposerSession`・`Transport`・`CellCommands`・`ComposerKeymap`・`MidiRecorder`・`SongFileCommands`） | Services |
| UI | `Editor/UI` | `UniDAWWindow`（配置と配線のみ）、パネル、`Views/`（トラッカー・ステップ・ピアノロール）、`Controls/`（自作部品：`IconButton`・`NumberBox`・`SegmentedControl`・`LevelMeter` など。Composer に依存しない） | Composer（`ComposerContext` 経由） |

- 新しい編集操作は Composer に足し、UI からは呼ぶだけにする（Undo・読み取り専用チェックは `ComposerSession` が一括で行う）
- 新しい表示は `IPatternView` を実装して `UniDAWWindow.BuildCenter` に並べ、`PatternViewKind` に値を**追記**する
- Audio / Midi / Model は UnityEngine の実行時 API を使わない（オーディオスレッド・テスト・CLI から使うため）

## キー操作（Cubase / Studio One 準拠）
≡ メニュー → Key Commands でプリセットを切り替えられます（差があるのはピアノロールのツール・複製・ナッジ）。

### トランスポート（共通）
| キー | 動作 |
|---|---|
| Space | 再生／停止の切替 |
| Enter（テンキーも） | 再生開始 |
| Num 0 / Esc | 停止 |
| Num . | 先頭へ |
| Num / | サイクル（ループ）切替：オンでパターン、オフで曲を再生 |
| Num * | 録音切替 |
| Num - / Num + | 前／次の順序 |

### ピアノロール
| 操作 | Cubase | Studio One |
|---|---|---|
| 選択／鉛筆／消しゴム | 1 / 8 / 5 | 1 / 5 / 4 |
| 複製 | Ctrl+D | D |
| 選択解除 | — | Ctrl+D |
| 1 行ずらす | Ctrl+←→ | Alt+←→ |

共通：ドラッグで移動（Alt+ドラッグで複製）、右端ドラッグで長さ、空き地ドラッグで範囲選択、Shift/Ctrl+クリックで追加選択、
ダブルクリックでノート作成・削除、↑↓ で移調（Shift で 1 オクターブ）、←→ で前後のノートを選択、
Ctrl+A / C / X / V、Del で削除、右クリックでメニュー。

## キー操作（トラッカー）
| キー | 動作 |
|---|---|
| Z〜M / Q〜P | ノート入力（2段鍵盤）、`1` ノートオフ、Del 消去 |
| 0-9 A-F | 数値列（16進）、Fx 列は `0 1 2 3 4 A V F S G Q R` |
| `[` `]` | オクターブ、Tab 次チャンネル |
| Ctrl+↑↓（+Shift） | 半音（1オクターブ）移調 |
| Ctrl+C / Ctrl+V | トラックのコピー / ペースト |

## デモ
File > Create Demo Assets、または次のコマンド：

```
Unity.exe -batchmode -projectPath . -executeMethod UniDAW.Editor.UniDAWCli.CreateDemo
Unity.exe -batchmode -projectPath . -executeMethod UniDAW.Editor.UniDAWCli.RenderMidi -unidawMidi in.mid -unidawWav out.wav
```

## テスト
`Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`（利用側プロジェクトでは manifest.json の `testables` に `com.morilab.unidaw` を追加）

## 今後（フェーズ2）
- Scriptable Audio Pipeline / Burst によるオーディオバックエンド
- VFX Graph / Timeline 連携
- macOS の MIDI 入力
