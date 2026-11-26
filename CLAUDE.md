# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 概要
Unity エディタ内で動くチップチューン DAW（NES 風 4ch：Pulse1 / Pulse2 / Triangle / Noise）。
トラッカー・ステップ・ピアノロールの 3 表示が同じ `Song` データを編集する（どの機能もトラッカーとピアノロールの両方で使える前提で作る）。

- 本体は UPM パッケージ `Packages/com.morilab.unidaw`（埋め込みパッケージ）。ルートの Unity プロジェクトは開発用
- Unity 6000.6.7f1、UI は UI Toolkit
- 名前空間：`UniDAW`（Runtime）、`UniDAW.Editor`、`UniDAW.Midi`
- `Assets/UniDAW/` 以下は生成物・作業用の曲で、パッケージには含まれない

## テスト
EditMode テストのみ（`Packages/com.morilab.unidaw/Tests/Editor`、asmdef `UniDAW.Tests`）。

```sh
"/c/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile test.log
```

- 単体実行は `-testFilter "UniDAW.Tests.MixerTests.Automation_EqFxOverridesMixerUntilStop"` を追加
- コンパイルエラーは `grep "error CS" test.log`、結果は results.xml の `<test-run ... passed= failed=>`
- エディタを GUI で開いている間はプロジェクトがロックされ、batchmode は失敗する
- テストではUndo グループが結合されるため、Undo 回数のアサーションは不安定
- バッチ用エントリ：`UniDAW.Editor.UniDAWCli`（`-executeMethod`。RenderMidi / Stats など）

## コードの層構成
依存は上から下への一方向のみ（詳細はパッケージ README「コードの層構成」）。

| 層 | フォルダ | 役割 |
|---|---|---|
| Model | `Runtime/Model` | Song / Pattern / Cell / Instrument / ChannelMix、記法（`Notation`・`FxText`） |
| Audio | `Runtime/Audio` | `ChipEngine`（シーケンサー）・`ChipChannel`・`ChipMixer`・`ChipEq`・`OfflineRenderer`・WAV |
| Midi | `Runtime/Midi` | SMF 読み書き、Song ⇔ MIDI 変換、MIDI 入力（WinMM） |
| Theory | `Runtime/Theory` | スケール・コード・伴奏・メロディ生成（Assist 用） |
| Unity | `Runtime/Unity` | `UniDAWPlayer` / `UniDAWVisualizer`（ゲーム組み込み用コンポーネント） |
| Services | `Editor/Services` | アセット入出力・MIDI インポーター・CLI・プレビュー音声・Claude クライアント |
| Composer | `Editor/Composer` | エディタの状態と編集操作。UIElements 非依存 |
| UI | `Editor/UI` | `UniDAWWindow`（配置と配線のみ）、パネル、`Views/`、`Controls/` |

- Model / Audio / Midi は UnityEngine の実行時 API を使わない（オーディオスレッド・テスト・CLI から使うため）
- 編集操作は Composer に足し、UI からは呼ぶだけ。Undo と読み取り専用チェックは `ComposerSession.ModifySong` が一括で行う
- 新しい表示は `IPatternView` を実装して `UniDAWWindow.BuildCenter` に並べ、`PatternViewKind` に値を**追記**する
- トラッカーエフェクトは `Cell` の `Fx`（`FxType`）。ミキサー系エフェクト（`Lxx` `Mxx` `Hxx` `Dxx`）は `Fx.TryGetMix` → `ChipMixer` のオートメーションで再生中だけ上書きし、停止でミキサー値に戻る
- Claude 提案（右ゾーンの Assist タブ）：`ClaudeAssist` が対象（`AiTarget`：メロディ等 / 曲構成 / ミックス）ごとにシステムプロンプトと JSON スキーマ（`ClaudePrompt` / `ClaudeStructure` / `ClaudeMix`）を選び、`ClaudeClient`（API キー）か `ClaudeCodeClient`（`claude -p`）で送る。返答は `ClaudeReply` で解析し、3 案を ←→ で切り替える

## リリース作業
機能ごとに以下を揃えてコミットする。

- `Packages/com.morilab.unidaw/CHANGELOG.md` の先頭に `## [x.y.0] - YYYY-MM-DD`
- `package.json` の version を上げる
- パッケージ README（機能説明・キー操作）を更新

`ProjectSettings/PackageManagerSettings.asset` と一時的な検証用フォルダ（`Assets/_Smoke` など）はコミットしない。
