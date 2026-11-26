# UniDAW — Unity エディタ内チップチューン DAW（プロトタイプ）

## インストール
Package Manager の「Add package from git URL」で次を指定します：
`<リポジトリURL>.git?path=Packages/com.morilab.unidaw`

アセンブリ：`UniDAW.Runtime`（モデル・音源・MIDI・再生コンポーネント）／`UniDAW.Editor`（エディタウィンドウ・MIDI インポーター）

Unity をエディタとして使う音楽制作向けの DAW です。ステップシーケンサーとトラッカーのよいところを組み合わせ、曲データはアセットとしてシーン・映像表現から直接参照できます。

## 構成
- 4ch：Pulse1 / Pulse2 / Triangle / Noise（NES 風）
- **Window > UniDAW > Composer** でエディタを開く
  - 上部のトランスポートバー：再生（PATTERN / SONG / STOP / REC）、再生位置・BPM・キーの表示（BPM / KEY をクリックすると右パネルの Song 欄を開く）、チャンネルごとのレベルメーター
  - エディタ見出し：表示切替（Tracker / Step / Piano）、表示ごとの道具（ピアノロールのチャンネル切替＝波形アイコン付きのボタンで、選ぶとチャンネルの色になる・ツール）、OCT / STEP（ドラッグ・ホイールで変更、ダブルクリックで直接入力）・FOLLOW、編集中のパターン（◀▶・名前・LEN・New / Clone）
  - 右パネルの Song 欄：BPM / Rows/Beat / Speed とキー・スケール
  - トラッカー／ステップ／ピアノロールの 3 表示（同じデータを編集、エディタ見出しの Tracker / Step / Piano で切替）。ピアノロールは左クリックで置く・消す、右クリックでノートオフ
  - パラメータロック（ステップごとの音量・エフェクト・確率・条件 A:B）
  - チャンネルごとのトラック長（ポリメーター）
  - トラッカーエフェクト：`0xy` アルペジオ / `1xx` `2xx` スライド / `3xx` ポルタメント / `4xy` ビブラート / `Axy` 音量スライド / `Vxx` デューティ / `Fxx` スピード・BPM / `Sxx` ノートカット / `Gxx` ノートディレイ / `Qxy` `Rxy` ノートスライド（速度 x・y 半音）/ `Lxx` `Mxx` `Hxx` EQ ロー・ミッド・ハイ（00-18 → -12〜+12 dB）/ `Dxx` サイドチェインの深さ。エフェクト列にカーソルを置くとステータスバーに説明を表示
  - 順序表のトランスポーズ（`Tr −` `Tr +` `+12`）：同じパターンを移調して使い回す（ノイズは対象外）
  - 下ゾーン（F3 / Ctrl+Alt+E で開閉、境界ドラッグで高さ変更）
  - 左ゾーン（パターン一覧、Ctrl+Alt+L）・右ゾーン（Ctrl+Alt+R）も境界ドラッグで幅を変えられ、ダブルクリックで閉じる。ツールバー右端の LEFT / ARRANGE / LOWER / RIGHT でも開閉
  - 右ゾーンは上にカーソル位置の要約（チャンネル・行・音・楽器）を常に出し、その下を **Inspector**（セル・楽器・Song 設定）／ **Assist**（入力支援）のタブで切り替える
  - ステータスバーは左に操作の結果（数秒で消える）、右にカーソル位置でできる操作の説明。キー操作の一覧は F1 かツールバー右端の ? で重ねて表示（Esc で閉じる）。いまの表示の区画を先頭に枠付きで出し、検索欄で絞り込める（空白区切りで AND）
  - Song を開いていないときは中央に開始画面（New Song / Open… / Import MIDI… / Demo Songs… と、プロジェクト内の Song 一覧）。デモ曲を勝手に開くことはしない
  - 編集エリアの上にアレンジ帯（Ctrl+Alt+A で開閉、境界ドラッグで高さ変更。ブロックは高さに合わせて伸縮）
  - 左ゾーンはパターン一覧：番号・名前・行数・曲中の使用回数（未使用は薄く表示）。クリックで編集、ダブルクリックで曲中のその位置から再生（選択中のエントリ優先、曲に無ければパターンをループ）、アレンジ帯へドラッグでその位置に並べる、New / Clone / → Song（選択中のエントリの後ろに並べる）
  - ウィンドウが狭いと編集エリアの幅（トラッカー 700px、他 360px）と高さ（160px）を残すよう、右 → 左、下ゾーンの順に自動で畳む
    - **ミキサー**：チャンネルごとの音量フェーダー（-∞〜+6 dB）・パン・ミュート / ソロ（Ctrl+クリックで単独ソロ）・メーター、マスター。設定は曲に保存され、再生・WAV 書き出しに反映（ドラッグ 1 回で Undo 1 回）。フェーダーはダブルクリック / Ctrl+クリックで既定値、Shift ドラッグで微調整
    - **Master**：マスターチェーン（EQ → 幅 → コンプ → ゲイン → リミッター）と曲の解析（ラウドネス・ピーク・帯域・チャンネルごとの音量・ヒント）、目標ラウドネスへの自動調整。詳しくは「マスタリング」
    - **楽器エディタ**：楽器一覧（クリックでカーソルのチャンネルに割り当て、ダブルクリックで試聴）と、Volume / Arp / Pitch / Duty マクロのバーグラフ編集（ドラッグで描画、右の空き列で延長、下段レーンでループ / リリース、右クリックでメニュー）。テキストでも編集可（`|` ループ、`/` リリース）
    - **プリセット**：楽器一覧の上の「Presets」で内蔵（Lead / Bass / Chord / Pad / Drum / FX の 22 種）と保存したものの一覧。クリックで曲に入れずに試聴（分類に合う音源で鳴る：リードは矩形波、ドラムは Noise）、ダブルクリックか Load で選択中の楽器を置き換え、Add で新しい楽器として追加。「Song」側の Save で今の楽器を保存（Unity の設定フォルダに置くのでプロジェクトをまたいで使える）
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
| Space | 再生／停止の切替（ウィンドウ内のどこにフォーカスがあっても効く。文字入力欄では入力を優先） |
| Enter（テンキーも） | 再生開始 |
| Num 0 / Esc | 停止 |
| Num . | 先頭へ |
| Num / | サイクル（ループ）切替：オンでパターン、オフで曲を再生 |
| Num * | 録音切替 |
| Num - / Num + | 前／次の順序 |
| F1 | キー操作の一覧（検索・いまの表示を先頭に。Esc で閉じる） |
| F3 | ミキサーの表示切替 |
| Alt+C | メトロノーム切替 |
| Ctrl+Alt+E | 下ゾーン（ミキサー / 楽器エディタ / マスター）の開閉 |
| Ctrl+Alt+A | アレンジ帯の開閉 |
| Ctrl+Alt+L / Ctrl+Alt+R | 左ゾーン（パターン一覧）／右ゾーン（Inspector / Assist）の開閉 |

> 再生・テンキー・F1 / F3・Alt+C・Ctrl+Alt+E / A / L / R は Unity の Shortcut Manager に登録しており、Edit > Shortcuts の「UniDAW」で割り当てを変えられます（Enter は Unity の制約で変更不可）。
>
> ボタンやフェーダーにマウスを乗せると説明が出ます。トラッカーのセルとピアノロールのノートでは、そのセルの中身（楽器名・エフェクトの意味など）を表示します。

### 順序表（どのビューでも共通）
| キー | 動作 |
|---|---|
| Alt+Insert | 現在のパターンを選択中の順序の後ろに追加 |
| Alt+Delete | 選択中の順序を外す（パターンは残る） |
| Alt+PageUp / PageDown | 選択中の順序を前／後ろへ移動 |
| Alt+[ / ] | 選択中の順序のパターン番号 ±1 |
| Ctrl+Alt+↑↓ | 選択中の順序を移調（Shift で 1 オクターブ） |
| Ctrl+Alt+←→ | Claude の提案候補（と依頼前）を切り替え |

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

下のレーン（Vol / Fx）はトラッカーの音量列・エフェクト列と同じ値。ドラッグで描き込み、Shift+ドラッグで直線（ランプ）、右ドラッグか Alt で消す（1 回のドラッグが 1 回の Undo）。

**オートメーション**：音量列・エフェクト列の行ごとの値がそのままオートメーション（音符の無い行でも効く）。
トラッカーでは両端に値を書き、間（または始点）にカーソルを置いて **Ctrl+I** で直線補間（フェードイン / アウト、`Fxx` のテンポ変化、`Vxx` のデューティ変化など）。
ピアノロールのレーンの Shift+ドラッグと同じ列に書くので、どちらで作っても両方で編集できる。
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
- **アレンジ帯**（編集エリアの上）：順序表を時間軸に並べ、各ブロックに 4 チャンネルの音符を縮小表示。クリックで選択、ダブルクリックでそこから再生、
  ドラッグで並べ替え、右クリックで挿入・複製・削除・固有化（パターンを複製してここだけ別に編集）・前後のパターンへ差し替え・移調。Ctrl+ホイールでズーム。
  帯の Ins / Dup / Del / Unique / ◀ ▶ でも同じ操作ができる
- **センドエフェクト**：ミキサーの各チャンネルの DLY / REV で、テンポ同期ディレイ（1/16〜1/2、ピンポン、フィードバック）と
  リバーブ（サイズ・ダンプ）へ送ります。設定は曲に保存され、ゲーム内再生・WAV 書き出しにも効きます。送りがなければ処理しません
- **EQ**：各チャンネルの LO（120 Hz シェルフ）/ MID（1 kHz）/ HI（6 kHz シェルフ）を ±12 dB。0 dB のバンドは素通し
- **サイドチェイン**：Send FX の Sidechain でトリガー元（既定 Noise）を選び、各チャンネルの DUCK で下げる深さ、REL で戻る速さを決めます。
  検出はトリガー元のフェーダー前の音をローパスに通すので、ハイハットよりキックに強く反応し、トリガー元をミュートしても効きます（ゴーストキック）。
  ミキサーの設定なので、トラッカーでもピアノロールでも同じように鳴ります
- **オートメーション**：エフェクト `Lxx` `Mxx` `Hxx`（EQ、0C = 0 dB）と `Dxx`（DUCK）で、再生中に EQ とダッキングを動かせます。
  ピアノロール下のレーンでも同じ値を描け、Ctrl+I で補間できます。止めるとミキサーの値に戻ります

## マスタリング（下ゾーンの Master タブ）
- **マスターチェーン**：マスター音量の後に EQ（LO 120 Hz / MID 1 kHz / HI 6 kHz、±12 dB）→ WIDTH（0% モノラル〜200%）→ コンプ（THR・RATIO、L/R 連動）→ GAIN → リミッター（約 1.5 ms 先読み、CEIL を超えない）の順に掛かります。
  ON を外すと素通しなので聴き比べに使えます。設定は曲に保存され、試聴・WAV 書き出し・ゲーム内再生に効きます（ステム書き出しには掛けません）。GR は再生中にコンプ・リミッターが下げている量
- **ラウドネスメーター**（再生中）：マスターの後の M（モーメンタリー 0.4 秒）・S（ショートターム 3 秒）・I（再生を始めてからの統合、ゲート付き）・PEAK を表示し、
  その下にショートタームの推移をグラフにします（青 = マスターの後、灰 = マスターの前、橙 = 目標）。再生し直すと測り直します
- **A/B**：マスターチェーンを外して聴きます。チェーンで変わった音量（直近 8 秒の前後の差）を自動で補正するので、大きい方が良く聴こえる錯覚なしに音色・ダイナミクスだけで比べられます（曲には保存しない）
- **解析**：曲を 1 周レンダリングして、統合ラウドネス（LUFS、EBU R128 のゲート付き）・サンプルピーク・PLR（ピークと平均の差）・短時間（3 秒）最大・
  帯域の割合（低 < 200 Hz / 高 > 4 kHz）・左右の相関・チャンネルごとのラウドネスを表示し、音割れ・目標との差・こもり / 耳に痛い高域・ほぼモノラル・埋もれたパートなどのヒントを出します。
  バックグラウンドで動くので解析中も編集できます
- **ラウドネスを合わせる**：目標（-14 配信 / -16 / -11 ゲーム BGM / -9 / -23 放送）に合うように GAIN を決めます（マスターとリミッターも ON になる。Ctrl+Z で戻せる）
- **AI に 3 案**：Claude に解析結果を見せてチェーンを 3 案作ってもらいます（Assist の「マスタリング」と同じ。詳しくは「Claude に提案してもらう」）

## 入力支援（Assist タブ、ネット不要）
右ゾーンの **Assist** タブから使います。どれも Ctrl+Z で戻せます。
- **Scale**：曲のキー（主音 + スケール）は Inspector タブの Song で設定。ピアノロールはスケール外の段を暗く、主音の段を明るく表示。
  Conform で選択（なければカーソルのチャンネル）、Whole Pattern で全チャンネルのスケール外の音を最寄りの音へ補正
- **Chords → 伴奏**：パターンごとのコード進行（`C G Am F`、`I V vi IV`、`Dm7 G7 Cmaj7 %`、`C/E` など）を均等に割り付け、
  Pulse 2（和音：0xy アルペジオ / 分散）・Triangle（ベース）・Noise（ドラム）を生成。ピアノロールにコードの構成音を色付け
- **Melody**：曲の中のフレーズ（音程の動き・リズム）から学ぶ簡単なマルコフモデルで、選択の直後（なければカーソル行）から続きを提案。
  キー・コードに沿い、強拍はコードトーン寄り。「別案」で同じ区間を作り直し

## Claude による提案（Assist タブ、Claude Code または API キー）
「サビっぽく」「切なく、ゆったり」などの言葉と曲の内容（キー・コード・各チャンネルの音符・前のパターン）を Claude に送り、
**メロディ**（カーソルのチャンネル。選択があればその範囲、なければカーソルから Rows 行）・**コード進行**・**両方**を提案させます。
カーソルが Triangle なら**ベース**（コードのルートを軸にキックとリズムを揃える）、Noise なら**ドラム**（キック・スネア・ハイハット・タム・クラッシュを GM ドラム番号で受け取り、ノイズの音と楽器に置き換え）として頼みます。
トラッカーでもピアノロールでも、カーソルのあるチャンネルが対象です。
**曲構成**を選ぶと、既存のパターンを見て Intro・Aメロ・Bメロ・サビ・間奏・Outro などのセクションと順序表（転調つき）を提案します。
新しいセクションは近い既存パターンを複製して小節数とコード進行を設定し、既存パターンはそのまま再利用されます。
候補ボタンか Ctrl+Alt+←→ で案と依頼前を切り替えられ、できたセクションはメロディ／ベース／ドラムの提案で埋めていけます。
**ミックス**を選ぶと、各チャンネルが鳴らす内容と今のミキサーを見て、音量・パン・EQ・DUCK・ディレイ / リバーブの送りとサイドチェインの設定を 3 案提案します。
候補ボタンか Ctrl+Alt+←→ で聴き比べられ、ミュート / ソロはそのまま残ります。
**マスタリング**を選ぶ（または Master タブの「AI に 3 案」）と、まず曲をマスターチェーンなしで解析し、ラウドネス・ピーク・帯域・相関・チャンネルごとの音量と気づいた点を添えて、
マスターチェーン（EQ・幅・コンプ・リミッター）を 3 案提案します。返ってきた案はそれぞれゲインを目標ラウドネスに合わせてから書き込むので、音量差に惑わされず音色で聴き比べられます。
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
デモはパッケージ本体に含めず、UPM のサンプルとして同梱しています。Package Manager で UniDAW を選び、Samples の **Demo** を Import すると `Assets/Samples/UniDAW/<version>/Demo/` に入ります。File > Import Demo Songs / 開始画面の Demo Songs… でも同じ場所に取り込んでデモ曲を開きます（埋め込み・ローカルのパッケージで Package Manager に Import ボタンが出ない場合もこちらで入ります。取り込み済みなら足りないファイルだけ足します）。

- `DemoSong.asset`：機能紹介用のデモ曲（楽器マクロ・エフェクト・確率／条件トリガー・トラック長・ミキサーのオートメーション・転調を一通り使う）
- `SunsetLoFi.asset`：84 BPM のゆったりした曲（セブンスの分散和音、Gxx のスウィング、確率で鳴るレコードのノイズ、Fxx でテンポを落とすエンディング、こもらせたミックスと -16 LUFS のマスター）
- `IronFortress.asset`：168 BPM・7/8 拍子（14 行で 1 小節）のボス戦（2+2+3 のベースとドラム、0xy の和音、10 行で回るポリメーターのブレイク、順序表の半音転調、密度を上げた -11 LUFS のマスター）
- `Demo.mid` / `DemoFromMidi.asset`：デモ MIDI と、それを読み込んだ Song
- `UniDAWDemo.unity`：`UniDAWPlayer` + `UniDAWVisualizer` で再生と映像連携をするシーン

## CLI

```
Unity.exe -batchmode -projectPath . -executeMethod UniDAW.Editor.UniDAWCli.RenderMidi -unidawMidi in.mid -unidawWav out.wav
Unity.exe -batchmode -projectPath . -executeMethod UniDAW.Editor.UniDAWCli.AnalyzeSong -unidawAsset Assets/x.asset [-unidawMatch]
```

## テスト
`Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`（利用側プロジェクトでは manifest.json の `testables` に `com.morilab.unidaw` を追加）

## 今後（フェーズ2）
- Scriptable Audio Pipeline / Burst によるオーディオバックエンド
- VFX Graph / Timeline 連携
- macOS の MIDI 入力
