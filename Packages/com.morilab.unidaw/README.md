# UniDAW — Unity エディタ内チップチューン DAW（プロトタイプ）

## インストール
Package Manager の「Add package from git URL」で次を指定します：
`<リポジトリURL>.git?path=Packages/com.morilab.unidaw`

アセンブリ：`UniDAW.Runtime`（モデル・音源・MIDI・再生コンポーネント）／`UniDAW.Editor`（エディタウィンドウ・MIDI インポーター）

Unity をエディタとして使う音楽制作向けの DAW です。ステップシーケンサーとトラッカーのよいところを組み合わせ、曲データはアセットとしてシーン・映像表現から直接参照できます。

## 構成
- 4ch：Pulse1 / Pulse2 / Triangle / Noise（NES 風）
- **Window > UniDAW > Composer** でエディタを開く
  - トラッカー／ステップ／ピアノロールの 3 表示（同じデータを編集、ツールバーの View で切替）。ピアノロールは左クリックで置く・消す、右クリックでノートオフ
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

## キー操作（トラッカー）
| キー | 動作 |
|---|---|
| Z〜M / Q〜P | ノート入力（2段鍵盤）、`1` ノートオフ、Del 消去 |
| 0-9 A-F | 数値列（16進）、Fx 列は `0 1 2 3 4 A V F S G Q R` |
| Space / Enter / Esc | パターン再生・停止 / 曲再生 / 停止 |
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
