# Changelog

## [0.32.2] - 2026-11-26

### Changed
- ピアノロールのチャンネル切替を、トランスポートと同じアイコンボタンに（Pulse / Triangle / Noise の波形アイコン＋名前、選ぶとチャンネルの色）。幅 20 px の文字ボタンで窮屈だったのを広げた

## [0.32.1] - 2026-11-26

### Fixed
- 埋め込み・ローカルのパッケージでデモを取り込めなかった（Package Manager に Samples の Import が出ない）。File > Import Demo Songs / 開始画面の Demo Songs… で `Assets/Samples/UniDAW/<version>/Demo` に直接コピーしてデモ曲を開く。取り込み済みなら足りないファイル（新しいデモ曲など）だけ足す

## [0.32.0] - 2026-11-26

### Added
- Master タブに再生中のラウドネスメーター：M（0.4 秒）/ S（3 秒）/ I（統合、ゲート付き）/ PEAK と、ショートタームの推移グラフ（マスターの前と後、目標の線）
  - オーディオスレッドで K 特性を掛けて 100 ms ごとに区切り、集計はエディタ側で行う（`LiveLoudness` / `LoudnessTrack`）
- **A/B**：マスターチェーンを外して聴く。チェーンで変わった音量を直近 8 秒の前後の差から補正し、同じ音量で聴き比べられる（`ChipEngine.AuditionBypass` / `AuditionGainDb`。チェーンは裏で動き続ける）

## [0.31.0] - 2026-11-26

### Added
- デモ曲を 2 曲追加（サンプル Demo に同梱）
  - **Sunset Lo-Fi**：84 BPM・ヘ長調。セブンスの分散和音、ノートディレイのスウィング、確率トリガーのレコードノイズ、リタルダンドのエンディング。マスターは -16 LUFS
  - **Iron Fortress**：168 BPM・7/8 拍子・ニ短調（ハーモニックマイナー）のボス戦。2+2+3 のグルーヴ、0xy の和音、ポリメーターのブレイク、後半は半音上へ転調。マスターは -11 LUFS

## [0.30.0] - 2026-11-26

### Added
- AI マスタリング：Assist の頼む内容に **マスタリング** を追加（Master タブの「AI に 3 案」からも頼める）
  - 曲をマスターチェーンなしで解析し、ラウドネス・ピーク・PLR・帯域・相関・チャンネルごとの音量とヒントを添えて Claude に送る（`ClaudeMaster`）
  - 返ってきた 3 案（EQ・幅・コンプ・リミッター）はそれぞれゲインを目標ラウドネスに合わせてから書き込む。候補ボタン / Ctrl+Alt+←→ で依頼前と切り替え、Ctrl+Z で戻せる
  - 解析とラウドネス合わせはバックグラウンドで動く

## [0.29.0] - 2026-11-26

### Added
- マスタリング支援：下ゾーンに **Master** タブを追加
  - マスターチェーン（`SongMaster`、曲に保存）：EQ（LO / MID / HI）→ ステレオ幅 → コンプ（スレッショルド・レシオ）→ ゲイン → 先読みリミッター（上限 CEIL）。ON / OFF で聴き比べ、再生中はゲインリダクションを表示。試聴・WAV 書き出し・`UniDAWPlayer` のすべてに効く（ステムには掛けない）
  - 解析：曲を 1 周レンダリングして統合ラウドネス（BS.1770 / EBU R128、LUFS）・ピーク・PLR・短時間最大・帯域のバランス（低 / 中 / 高）・左右の相関・チャンネルごとのラウドネスを測り、改善のヒントを出す（`LoudnessMeter` / `MasterAnalysis`）。バックグラウンドで動くので解析中も編集できる
  - 「ラウドネスを合わせる」：目標（-14 / -16 / -11 / -9 / -23 LUFS）に合うようにマスターのゲインを自動で決める
- CLI：`UniDAWCli.AnalyzeSong`（`-unidawAsset` の曲を解析してログに出す。`-unidawMatch` でラウドネス合わせ後の値）
- `ChipEngine.BypassMaster` / `MasterOverride` / `MasterReductionDb`、`OfflineRenderer.RenderSongStereo(song, rate, setup)`

## [0.28.0] - 2026-11-26

### Added
- トラッカーのセルとピアノロールのノートにマウスを乗せると、中身（ノート・長さ・楽器名・音量・エフェクトの意味・確率・条件）を表示。エフェクト列では書式の説明も出す。`CellDescription` / `ITooltipSource`
- ミュート / ソロ・パン / 音量 / マスターフェーダー・Send FX のフェーダー・楽器の New / ▶・Clear Cell / Clear Locks・Assist の中止 / API キー欄などにツールチップを追加

### Fixed
- ツールチップが出るまでの時間がまちまちだったり出なかったりした。ウィンドウ内のツールチップを自前（`TooltipOverlay`）で出し、一定の待ち時間（約 0.45 秒）で表示、続けて別の要素へ移ったときはすぐ切り替える。クリック・ホイール・ドラッグで消える

## [0.27.0] - 2026-11-26

### Changed
- 再生・テンキーのトランスポート・F1 / F3・Alt+C・Ctrl+Alt+E / A / L / R を Unity の Shortcut Manager に登録。ウィンドウ内のどこにフォーカスがあっても効き、Edit > Shortcuts の「UniDAW」で割り当てを変えられる（文字入力欄にフォーカスがある間は入力を優先）。Enter（Return）は Shortcut Manager に割り当てられないため従来どおりキーマップで受ける
- ウィンドウ全体のキーの一覧を `ComposerKeymap.WindowCommands` / `IsWindowCommand` にまとめた

### Fixed
- フォーカスを持てない所をクリックしてもフォーカスがウィンドウの外へ抜けないよう、大枠（ツールバー・本体・ステータスバー等）がフォーカスを受け取るようにした。0.26.0 の「フォーカスが無くなったら編集面に戻す」処理は廃止
- フォーカスが残ったボタンが Space / Enter で押されないよう、文字入力中以外は Submit を止める

## [0.26.0] - 2026-11-26

### Added
- パターン一覧のダブルクリックで、曲中でそのパターンが出る位置から再生（選択中のエントリ優先。曲に並べていなければパターンをループ再生）。`ComposerSession.PlayFromPattern`

### Fixed
- アレンジ帯のブロックなどフォーカスを持てない所をクリックするとフォーカスが外れ、Space での停止などのキー操作が効かなくなっていた。フォーカスが無くなったら編集面に戻す

## [0.25.0] - 2026-11-26

### Fixed
- 楽器の試聴（Audition・プリセットのクリック・楽器のダブルクリック）がカーソルのチャンネルで鳴り、Noise にいると全部ノイズになっていた。曲で使っているチャンネル、無ければ楽器の分類・名前に合う音源（リードは矩形波、ドラムは Noise、三角波ベースは Triangle）で鳴らす
- ボタンにフォーカスが残っていると Space がそのボタンの Submit まで押してしまい、再生が無反応に見えた。スライダー・ドロップダウンを触った直後も Space で再生/停止できるように

### Added
- `AuditionTarget`（試聴チャンネルの決定）、`InstrumentPreset.Drum` / `Bass`（分類名の定数）

### Changed
- `ComposerSession.AuditionPreset` は `InstrumentPreset` を受け取る（分類も試聴チャンネルの推定に使う）
- デモ曲を作り込み直し：イントロ → ヴァース A/B → ビルド → サビ → ブレイクダウン → 転調サビ → アウトロの 52 秒。PWM・ディレイ風エコー・遅れビブラート・スライド／ベンド・コードスタブ（0xy）・ゴーストノートと確率・条件トリガー・シャッフル（Fxx 交互）・トラック長の違うポリリズム・ミキサーのオートメーション（Lxx / Hxx / Dxx）・リタルダンドなどを一通り使う

## [0.24.0] - 2026-11-26

### Changed
- デモ（デモ曲・デモ MIDI・MIDI 変換結果・デモシーン）を UPM サンプル `Samples~/Demo` に分離。Package Manager の Samples から Import する
- File メニューと開始画面の Demo は Package Manager を開くボタンに

### Removed
- `UniDAWAssets.CreateDemo` / `CreateDemoScene` / `Demo*Path`、`UniDAWCli.CreateDemo`、`SongFileCommands.CreateDemo`
- Runtime の `DemoSongs`（テスト用にテストアセンブリへ移動）

## [0.23.0] - 2026-11-26

### Added
- キー操作の一覧を F1 / ツールバーの ? で重ねて表示（Esc・背景クリックで閉じる）
- Song が無いときの開始画面（New Song / Open… / Import MIDI… / Demo Song、プロジェクト内の Song 一覧）
- `SongFileCommands.OpenSong`、`ComposerSession.SetKeyHelp`

### Changed
- ステータスバーを「結果メッセージ（5 秒で消える）」と「カーソル位置の操作説明」だけに。再生位置・オクターブ等は液晶と編集見出しに一本化
- 通知はウィンドウ中央のポップアップではなくステータスバーに出す

### Removed
- 起動時にデモ曲を自動で開く処理
- 右パネル下のキー一覧（F1 の一覧へ）

## [0.22.0] - 2026-11-26

### Changed
- 右ゾーンを Inspector / Assist のタブに。上にカーソル位置の要約（チャンネル・行・音・楽器）を常に表示
- Assist を下ゾーンから右ゾーンへ移動し、セクションを縦に並べた。下ゾーンは Mixer / Instrument のみ（以前に保存された Assist タブは Mixer で開く）
- Assist の Key / Scale からキー選択を外し（Inspector の Song に一本化）、スケール補正ボタンを Conform / Whole Pattern に

### Added
- `ComposerSession.ShowRightTab` / `RightTab`

## [0.21.0] - 2026-11-26

### Added
- 左ゾーンをパターン一覧に（行数・使用回数を表示、クリックで編集、アレンジ帯へドラッグして並べる、右クリックで末尾に並べる・複製・新規）
- `OrderCommands.InsertPattern(slot, pattern)`

### Removed
- 縦の順序表パネル（`OrderPanel`）。並びの編集はアレンジ帯と Alt 系のキーで行う

## [0.20.0] - 2026-11-26

### Added
- アレンジ帯：Arrange を下ゾーンのタブから編集エリアの上の帯へ移動（Ctrl+Alt+A・ツールバーの ARRANGE で開閉、境界ドラッグで高さ変更）
- 帯のボタン Ins / Dup / Del / Unique、右クリックメニューに「後ろに挿入」「前 / 次のパターン」

### Changed
- アレンジは並びが変わったときだけブロックを作り直し、選択は枠の色、音符は変わったパターンだけ描き直す（音符はキャッシュ）
- 以前の版で保存された下ゾーンの Arrange タブは Mixer として開く

## [0.19.0] - 2026-11-26

### Added
- エディタ見出し：表示切替・表示ごとの道具・OCT / STEP / FOLLOW・編集中のパターン操作を編集エリアの上にまとめた
- 右パネルに Song 欄（BPM / Rows/Beat / Speed・キー）。上部バーの液晶に BPM / KEY を表示し、クリックで Song 欄を開く

### Changed
- 上部バーは再生・位置表示・レベル・MIDI 入力・ゾーン開閉だけに整理。ピアノロールのチャンネル・ツールは見出しへ移動
- `PatternBar` を廃止（`EditorHeader` に統合）

## [0.18.0] - 2026-11-26

### Added
- 左・右・下ゾーンの境界をドラッグで可変に（離したときに大きさを保存、ダブルクリックで閉じる）
- Ctrl+Alt+L / Ctrl+Alt+R で左 / 右ゾーンを開閉、ツールバーに LEFT / LOWER / RIGHT
- ウィンドウが狭いと右 → 左 → 下ゾーンの順に自動で畳み、編集エリアの最小サイズを守る

### Changed
- 下ゾーンの分割を TwoPaneSplitView から独自の境界（`Splitter`）に置き換え。閉じているパネルは再描画しない

## [0.17.0] - 2026-11-26

### Added
- 順序表のキー操作（どのビューでも）：Alt+Insert 追加 / Alt+Delete 外す / Alt+PageUp・PageDown 移動 / Alt+[ ] パターン番号 / Ctrl+Alt+↑↓ 移調
- Claude の提案候補を Ctrl+Alt+←→ で切り替え（以前の「←→ で切り替え」という記述は誤りでボタンのみでした）
- `PanelLayout.Fit`：ウィンドウが狭いときに周辺パネルを畳む規則（UI 刷新の下準備。表示はまだ変わりません）

## [0.16.0] - 2026-11-26

### Added
- ミキサーのオートメーション：エフェクト `Lxx` `Mxx` `Hxx`（EQ ロー / ミッド / ハイ、00-18 → -12〜+12 dB）と `Dxx`（DUCK、00-FF → 0〜100%）。
  トラッカーでもピアノロールのレーンでも描け（Ctrl+I で補間）、停止・再生開始でミキサーの値に戻る
- Claude の「ミックス」提案：全チャンネルの音量・パン・EQ・DUCK・送りとサイドチェインのトリガー元・リリースを 3 案。
  ←→ で切り替え（ミュート / ソロは保持）、範囲外の値は丸める（`ClaudeMix`）

## [0.15.0] - 2026-11-26

### Added
- チャンネル EQ：ミキサーの各ストリップに LO / MID / HI（±12 dB、RBJ 双二次のシェルフ・ピーキング）。パン前のモノラル信号に掛け、0 dB のバンドは処理しない
- サイドチェイン・ダッキング：トリガー元チャンネル（`SongEffects.sidechainSource`、既定 Noise）のフェーダー前の音をローパス＋エンベロープで検出し、
  各チャンネルの DUCK（`ChannelMix.duck`）の深さで下げる。リリースは Send FX の REL（30〜600 ms）。試聴・ゲーム内再生・WAV 書き出しに効く
- `MixParam`（EQ 3 バンド・DUCK）と `MixerCommands.PreviewParam` / `CommitParam` / `SetSidechainSource`（ドラッグ 1 回で Undo 1 回）

## [0.14.0] - 2026-11-26

### Added
- Claude 提案に「曲構成」：既存パターンの要約と順序表を送り、セクション（名前・元パターン・小節数・コード・転調）の並びを複数案で受け取る。
  新しいセクションは元パターンを複製して長さとコードを設定、同じ名前は同じパターンを再利用し、順序表を書き換える（1 回の Undo）。
  ←→ で案／依頼前を切り替えると、前の案で足したパターンは外して元の順序表に戻す

## [0.13.0] - 2026-11-26
- オートメーションのランプ：トラッカーで Ctrl+I（音量列・Fx 列の上下の値の間を直線補間、Fx は同じ列に書く、1 回の Undo）、
  ピアノロールのレーンで Shift+ドラッグの直線。どちらも同じ列を読み書きする（`CellLane.Ramp`）

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
