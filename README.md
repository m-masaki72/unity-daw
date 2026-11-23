# ChipDaw — Unity エディタ内チップチューン DAW（プロトタイプ）

Unity をエディタとして使う音楽制作向けの DAW です。ステップシーケンサーとトラッカーのよいところを組み合わせ、曲データはアセットとしてシーン・映像表現から直接参照できます。

## 構成
- 4ch：Pulse1 / Pulse2 / Triangle / Noise（NES 風）
- **Window > ChipDaw > Composer** でエディタを開く
  - ステップ表示とトラッカー表示（同じデータを編集）
  - パラメータロック（ステップごとの音量・エフェクト・確率・条件 A:B）
  - チャンネルごとのトラック長（ポリメーター）
  - トラッカーエフェクト：`0xy` アルペジオ / `1xx` `2xx` スライド / `3xx` ポルタメント / `4xy` ビブラート / `Axy` 音量スライド / `Vxx` デューティ / `Fxx` スピード・BPM / `Sxx` ノートカット
  - インストゥルメント：音量・アルペジオ・デューティのシーケンス（`|` ループ、`/` リリース）
- MIDI
  - `.mid` を Assets に置くと読み取り専用の Song として自動インポート（Inspector で割り当て方式などを変更可）
  - File > Import MIDI（Auto / By Track / Split By Pitch）、Export MIDI / WAV
  - MIDI 入力（Windows / WinMM）：ツールバーの MIDI In からデバイスを選ぶ。● Rec がオンのとき、停止中はステップ入力、再生中はリアルタイム録音
- 映像連携：`ChipDawPlayer`（AudioSource）＋`ChipDawVisualizer`、`onNote` UnityEvent

## キー操作（トラッカー）
| キー | 動作 |
|---|---|
| Z〜M / Q〜P | ノート入力（2段鍵盤）、`1` ノートオフ、Del 消去 |
| 0-9 A-F | 数値列（16進）、Fx 列は `0 1 2 3 4 A V F S` |
| Space / Enter / Esc | パターン再生・停止 / 曲再生 / 停止 |
| `[` `]` | オクターブ、Tab 次チャンネル |
| Ctrl+↑↓（+Shift） | 半音（1オクターブ）移調 |
| Ctrl+C / Ctrl+V | トラックのコピー / ペースト |

## デモ
File > Create Demo Assets、または次のコマンド：

```
Unity.exe -batchmode -projectPath . -executeMethod ChipDaw.Editor.ChipDawCli.CreateDemo
Unity.exe -batchmode -projectPath . -executeMethod ChipDaw.Editor.ChipDawCli.RenderMidi -chipdawMidi in.mid -chipdawWav out.wav
```

## テスト
`Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`

## 今後（フェーズ2）
- Scriptable Audio Pipeline / Burst によるオーディオバックエンド
- VFX Graph / Timeline 連携
- macOS の MIDI 入力
