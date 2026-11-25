# Changelog

## [0.12.0] - 2026-11-26
- Claude 提案がベースとドラムに対応：カーソルが Triangle ならベース、Noise ならドラムとして頼む（チップの表示も切り替わる）。
  ドラムは GM ドラム番号で受け取り `DrumMap` でノイズのピッチと Kick / Snare / Hat / Crash 楽器に置き換え、既存の打点は楽器名で伝える。
  チャンネルの役割（リード／対旋律／ベース／ドラム）ごとの書き方をプロンプトに追加

## [0.11.0] - 2026-11-26
- 楽器プリセット：内蔵 22 種（`InstrumentLibrary`）と、ユーザー保存（`InstrumentPresets`、Unity の設定フォルダの JSON）。
  楽器エディタの「Presets」一覧でクリック試聴（曲に入れずに鳴らす `ChipEngine.PreviewSlot`）、Load で置き換え、Add で追加、Save で保存

## [0.10.0] - 2026-11-26
- ピアノロールの下に音量／エフェクトのレーン。トラッカーの音量列・エフェクト列と同じセルを棒グラフで描き、ドラッグで描き込める
  （右ドラッグか Alt で消す。ドラッグ 1 回が Undo 1 回）。エフェクトは種類を選んで、その種類が入っている列か空いている列へ書く

## [0.9.0] - 2026-11-26
- ゲーム連携：`UniDAWPlayer` に拍・小節・区間の頭の通知（`onBeat` / `onBar` / `onSection` / `BeatTriggered`）、
  次の小節の頭（またはパターンの終わり）で区間を切り替える予約 `QueueSection`、パートの抜き差し `SetLayer` を追加。
  エンジン側は `ChipEngine.BeatEvents` / `ChipEngine.Queue`

## [0.8.0] - 2026-11-26
- ステム書き出し（File > Export Stems）：チャンネルごとの WAV。フェーダー・パン・センドは乗り、曲のソロ設定に関係なく各チャンネルを書き出す。
  `OfflineRenderer.RenderChannelStereo` / `UniDAWAssets.ExportStems`
- Claude 提案が 3 案を返すように。Assist タブの「元 / 案1 / 案2 / 案3」で切り替え（それぞれ Ctrl+Z で戻せる）、「▶ 試聴」で範囲の頭から鳴らす。
  書き込むのは曲のデータなので、トラッカーでもピアノロールでも同じに見える。以前の 1 案だけの返答も読める

## [0.7.0] - 2026-11-25
- Claude 提案をローカルの Claude Code（`claude -p`）でも使えるように。API キー不要でログイン済みアカウントを使う。
  Assist タブの「接続」で 自動 / Claude Code / API を切り替え（自動は API キー優先、なければ Claude Code）
- 修正：選択が古くなって中のノートが消えていると Claude 提案が例外になっていたのを、カーソル範囲へ戻すように
- 修正：スクリプト再読み込み時に実行中の Claude Code プロセスを止める
- 順序表の Move は範囲外を丸め、最後の 1 つは削除しない
- 内部整理：順序表の編集を `OrderCommands` に、メトロノーム音を `ClickVoice` に、返答の読み取りを `ClaudeReply` に分離。
  下ゾーンを `LowerZone`、ピアノロールの描画を partial（`PianoRollView.Draw.cs`）に分け、Painter2D の描画ヘルパー `PaintUtil` を共通化

## [0.6.0] - 2026-11-25
- メトロノーム（CLICK / Alt+C）と録音時の 1 小節プリカウント（PRE）。`ChipEngine.Metronome` / `CountingIn`、`Play` / `PlayPattern` の `countInBeats`
- アレンジ表示（下ゾーンの Arrange タブ）：順序表をブロックで表示し、ドラッグで並べ替え、複製・削除・固有化・移調、ここから再生
- センドエフェクト：チャンネルごとのディレイ / リバーブ送り（`ChannelMix.delaySend` / `reverbSend`）と曲のエフェクト設定（`Song.effects`）。
  テンポ同期のステレオ（ピンポン）ディレイと軽量リバーブ。送りのない曲は従来と同じ音・同じ負荷

## [0.5.0] - 2026-11-25
- Claude による提案（Assist タブ）：言葉での依頼と曲の文脈から、メロディ・コード進行・両方を提案して Undo 付きで書き込み
  - Messages API（構造化出力、Opus 5.5 / Sonnet 5.5、拒否時はサーバー側フォールバック）。API キーは EditorPrefs か環境変数 `ANTHROPIC_API_KEY`
  - 提案は範囲・音域・単音に収めてから書き込み。考え中の中止、返答までにパターンやチャンネルが変わったときは書き込まない

## [0.4.0] - 2026-11-25
- 入力支援（下ゾーンの Assist タブ、ネット不要）
  - 曲のキー（`Song.keyRoot` / `scaleMode`）とスケール補正。ピアノロールでスケール外の段を暗く表示
  - パターンごとのコード進行（`Pattern.chords`、音名・ローマ数字・分数コード）から和音・ベース・ドラムを生成
  - 曲から学習するマルコフモデルによるメロディの続きの提案と「別案」
- Runtime に `Scale` / `Chord` / `ChordProgression` / `Accompaniment` / `MelodyModel`（UnityEngine 非依存）
- MIDI 読み込みの堅牢化：分解能 0・不正なヘッダ長を拒否、途中で切れたデータはトラックの終わりとして扱う、極端に長い時刻は上限（`SmfReader.MaxTick` / `MidiToSong.MaxImportRows`）で打ち切り

## [0.3.0] - 2026-11-24
- ミキサー（下ゾーン、F3）：チャンネルごとの音量・パン・ミュート / ソロとマスター音量を曲に保存。再生・書き出しに反映し Undo 可能。ゲーム側の `ChipEngine.Mute` / `Solo` は従来どおり上書きとして有効
- ステレオ出力：`ChipEngine.Render` が 2ch 以上で L/R を出力（センターはモノラルと同じ音量）。`ChipEngine.OutputLevels`、`RenderSongStereo`、`WavWriter.Write16` を追加。WAV 書き出しはステレオに
- 楽器エディタ：楽器一覧とマクロのバーグラフ編集（Volume / Arp / Pitch / Duty、ループ / リリースのレーン）
- Pitch マクロ（`Instrument.pitch`、1/16 半音単位で積算）
- 旧データは読み込み時に自動移行（ミキサーは 0 dB・センター）。ミュート / ソロはウィンドウの一時状態から曲の設定へ移動

## [0.2.0] - 2026-11-24
- エフェクト `Gxx` ノートディレイ、`Qxy` / `Rxy` ノートスライドを追加
- 順序表エントリごとのトランスポーズ（`Song.orderTranspose`）。再生・MIDI 書き出しに反映
- ピアノロール表示（トラッカー・ステップと同じデータの第3ビュー）
- エフェクト列のヘルプをステータスバーに表示
- 内部構成を層ごとに整理（Editor: Services / Composer / UI、Runtime/Audio: シーケンサー・チャンネル・ミキサー・オフラインレンダラー）。`UniDAWWindow` は配置と配線のみに。公開 API の互換は維持（`OfflineRenderer` を追加、`ChipEngine.RenderSong` はそのまま利用可）
- コンポーザーのツールバーを自作部品のトランスポートバーに刷新（大きな再生ボタン、液晶風の位置表示、ドラッグで変える数値欄、表示切替、レベルメーター）。ウィンドウ全体のボタン・入力欄も暗めのテーマに統一
- キー操作を Cubase / Studio One に準拠（Space で再生／停止、テンキーのトランスポート、プリセット切替）。トランスポートバーに先頭へ・サイクルを追加
- ピアノロールで直接編集（ドラッグ移動・長さ変更・範囲選択・ダブルクリック作成／削除・複製・コピー＆ペースト・ツール切替）
- コンポーザーウィンドウの保存状態の形式が変わったため、更新直後に一度だけカーソル位置などが初期化されます

## [0.1.0] - 2026-11-24
- 初回プロトタイプ（4ch チップ音源、ステップ／トラッカー編集、MIDI 入出力）
