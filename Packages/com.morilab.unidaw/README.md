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
  - 下ゾーン（F3 / Ctrl+Alt+E で開閉、境界ドラッグで高さ変更）
    - **ミキサー**：チャンネルごとの音量フェーダー（-∞〜+6 dB）・パン・ミュート / ソロ（Ctrl+クリックで単独ソロ）・メーター、マスター。設定は曲に保存され、再生・WAV 書き出しに反映（ドラッグ 1 回で Undo 1 回）。フェーダーはダブルクリック / Ctrl+クリックで既定値、Shift ドラッグで微調整
    - **楽器エディタ**：楽器一覧（クリックでカーソルのチャンネルに割り当て、ダブルクリックで試聴）と、Volume / Arp / Pitch / Duty マクロのバーグラフ編集（ドラッグで描画、右の空き列で延長、下段レーンでループ / リリース、右クリックでメニュー）。テキストでも編集可（`|` ループ、`/` リリース）
    - **プリセット**：楽器一覧の上の「Presets」で内蔵（Lead / Bass / Chord / Pad / Drum / FX の 22 種）と保存したものの一覧。クリックで曲に入れずに試聴、ダブルクリックか Load で選択中の楽器を置き換え、Add で新しい楽器として追加。「Song」側の Save で今の楽器を保存（Unity の設定フォルダに置くのでプロジェクトをまたいで使える）
    - Pitch マクロは 1/16 半音単位で tick ごとに積み上がる（ピッチエンベロープ・キック等）
  - WAV 書き出しはステレオ（パン反映）
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
| F3 | ミキサーの表示切替 |
| Alt+C | メトロノーム切替 |
| Ctrl+Alt+E | 下ゾーン（ミキサー / 楽器エディタ / Assist / Arrange）の開閉 |

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

下のレーン（Vol / Fx）はトラッカーの音量列・エフェクト列と同じ値。ドラッグで描き込み、右ドラッグか Alt で消す（1 回のドラッグが 1 回の Undo）。
Fx はエフェクトの種類（V デューティ、4 ビブラートなど）を選んで、その種類の値を描く。

## キー操作（トラッカー）
| キー | 動作 |
|---|---|
| Z〜M / Q〜P | ノート入力（2段鍵盤）、`1` ノートオフ、Del 消去 |
| 0-9 A-F | 数値列（16進）、Fx 列は `0 1 2 3 4 A V F S G Q R` |
| `[` `]` | オクターブ、Tab 次チャンネル |
| Ctrl+↑↓（+Shift） | 半音（1オクターブ）移調 |
| Ctrl+C / Ctrl+V | トラックのコピー / ペースト |

## 録音・アレンジ・エフェクト
- **メトロノーム / プリカウント**：トランスポートバーの CLICK（Alt+C）で拍ごとにクリック（小節頭は高い音）。
  PRE をオンにすると、録音（REC）中の再生開始前に 1 小節（4 拍）カウントし、その間の MIDI 入力は記録しません。クリックは WAV 書き出しには入りません
- **Arrange タブ**：順序表を時間軸に並べ、各ブロックに 4 チャンネルの音符を縮小表示。クリックで選択、ダブルクリックでそこから再生、
  ドラッグで並べ替え、右クリックで複製・削除・固有化（パターンを複製してここだけ別に編集）・パターン差し替え・移調。Ctrl+ホイールでズーム
- **センドエフェクト**：ミキサーの各チャンネルの DLY / REV で、テンポ同期ディレイ（1/16〜1/2、ピンポン、フィードバック）と
  リバーブ（サイズ・ダンプ）へ送ります。設定は曲に保存され、ゲーム内再生・WAV 書き出しにも効きます。送りがなければ処理しません

## 入力支援（Assist タブ、ネット不要）
下ゾーンの **Assist** タブから使います。どれも Ctrl+Z で戻せます。
- **Key / Scale**：曲のキー（主音 + スケール）を設定。ピアノロールはスケール外の段を暗く、主音の段を明るく表示。
  「スケールに合わせる」で選択（なければカーソルのチャンネル）、「パターン全体」で全チャンネルのスケール外の音を最寄りの音へ補正
- **Chords → 伴奏**：パターンごとのコード進行（`C G Am F`、`I V vi IV`、`Dm7 G7 Cmaj7 %`、`C/E` など）を均等に割り付け、
  Pulse 2（和音：0xy アルペジオ / 分散）・Triangle（ベース）・Noise（ドラム）を生成。ピアノロールにコードの構成音を色付け
- **Melody**：曲の中のフレーズ（音程の動き・リズム）から学ぶ簡単なマルコフモデルで、選択の直後（なければカーソル行）から続きを提案。
  キー・コードに沿い、強拍はコードトーン寄り。「別案」で同じ区間を作り直し

## Claude による提案（Assist タブ、Claude Code または API キー）
「サビっぽく」「切なく、ゆったり」などの言葉と曲の内容（キー・コード・各チャンネルの音符・前のパターン）を Claude に送り、
**メロディ**（カーソルのチャンネル。選択があればその範囲、なければカーソルから Rows 行）・**コード進行**・**両方**を提案させます。
カーソルが Triangle なら**ベース**（コードのルートを軸にキックとリズムを揃える）、Noise なら**ドラム**（キック・スネア・ハイハット・タム・クラッシュを GM ドラム番号で受け取り、ノイズの音と楽器に置き換え）として頼みます。
トラッカーでもピアノロールでも、カーソルのあるチャンネルが対象です。
提案はそのまま書き込まれ、Ctrl+Z で戻せます。モデルは Opus 5.5（既定・高品質）か Sonnet 5.5（速い）。

- 「接続」チップで経路を選べます。**自動**（既定）は API キーがあれば API、なければローカルの Claude Code を使います
- **Claude Code**：インストール済みの `claude` コマンドを `claude -p` で呼び出します。API キーは不要で、Claude Code にログインしているアカウント（サブスクリプション等）を使います。
  ツール・設定・MCP を読み込まない一回きりの呼び出しで、作業フォルダは空の一時フォルダです
- **API**：API キーは Assist タブの「API キー…」で入力（この PC の EditorPrefs に保存）するか、環境変数 `ANTHROPIC_API_KEY` に設定。プロジェクトのファイルには書きません
- 送るのは曲の音符・コード・テンポ・頼みごとの文章だけです。料金は API なら Anthropic の API 利用料、Claude Code ならそのアカウントの利用枠として扱われます
- 断られたとき・途中で切れたとき・通信エラーはステータスに表示し、曲は変更しません

## ゲームへの組み込み（UniDAWPlayer）
- `onBeat(bar, beatInBar)` / `onBar(bar)` / `onSection(order)`（UnityEvent）と `BeatTriggered`（C# event）で拍・小節・区間の頭を受け取れる（4/4 として数える）
- `QueueSection(order)` / `QueueSection("B")`：次の小節の頭で別の区間へ移る。`atNextBar: false` なら今のパターンの終わりで移る。`-1` で取り消し
- `SetLayer(channel, on)`：パートの抜き差し（曲のミキサー設定はそのまま）

```csharp
player.onBar.AddListener(bar => Debug.Log($"bar {bar}"));
player.QueueSection("Boss");   // 戦闘に入ったら次の小節から Boss 区間へ
player.SetLayer(3, false);     // ノイズを抜く
```

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
