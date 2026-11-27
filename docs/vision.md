# UniDAW の展望：Unity ならではの使い道

調査日：2026-11-26

## 前提：UniDAW の強み
既存の Unity 向け音楽同期ツール（Koreographer など）は、完成した音源に**後から**タイミング情報を付けて同期させる。
UniDAW は曲そのものが譜面なので、ノート・拍・小節・セクションがすべて最初からわかっていて、サンプル単位で正確に取れる。

すでにある土台（`UniDAWPlayer`）：

- イベント：`onNote` / `onBeat` / `onBar` / `onSection`（C# イベント `NoteTriggered` / `BeatTriggered` も）
- 曲の差し替え：`QueueSection(order or name, atNextBar)`
- パートの重ね掛け：`SetLayer(channel, on)`
- レベル：`GetLevel(channel)`
- 表示：`UniDAWVisualizer`（チャンネルごとにオブジェクトを拡大縮小）

## 方向性（優先順）

### 1. リズムゲームの譜面を曲から自動生成
- 指定チャンネルのノートをそのまま判定ノーツにする（チャンネル・音域・楽器でフィルタ）
- 音楽時間 API：拍単位の現在位置（細分化つき）を返し、Animator などを拍で動かせるようにする
- 判定は DSP 時刻基準で行い、オーディオの遅延補正を設定できるようにする
- 差別化：譜面データを別に作る必要がない。曲を直せば譜面も直る

### 2. Timeline 連携と MV 書き出し
- UniDAW トラック / クリップ：Timeline 上で試聴し、エディタで再生位置を動かして（スクラブ）確認できる
- ノート・セクションを Timeline シグナルとして発行し、アニメーション・カメラ・ライトに渡す
- Unity Recorder と組み合わせて、曲からミュージックビデオ（MP4）まで Unity 内で作る
- DAW 単体ではできない「曲と映像を同じ場所で作る」体験

### 3. アダプティブミュージックの仕上げ
- 緊張度などのパラメータ（0〜1）を、ミキサー・EQ / DUCK のオートメーション・`SetLayer`・`QueueSection` にまとめて対応付ける
- `PlayOnBeat`：効果音やジングルを次の拍 / 小節に合わせて鳴らす
- スティンガー（短いフレーズの割り込み）、パターンの末尾で切り替えるトランジション
- 曲の差し替え（horizontal re-sequencing）とパートの重ね掛け（vertical remixing）は MusicEngine などで定番の手法。UniDAW に欠けているのは、パラメータでまとめて操作する層

### 4. シェーダー・VFX Graph 連動
- チャンネルごとのレベル・ピッチ・エンベロープ・拍の位相を Shader のグローバル値として毎フレーム渡す
- VFX Graph / ポストエフェクト / ライトを曲で動かす。ライブでの VJ 用途（MIDI 入力は既にある）
- 今の `UniDAWVisualizer`（拡大縮小だけ）を一般化する

### 5. 空間・物理を使う作曲（実験的）
- 4 チャンネルを 3D の AudioSource として配置し、歩き回りながらミックスを聴く
- 衝突・ビー玉・セルオートマトンをトリガーにするシーケンサー
- 3D 空間に展開したピアノロール / アレンジビュー、VR での作曲
- 先行例：PatchWorld（VR のモジュラー作曲）、MultiSequencer-Unity。DAW としての主軸からは離れるので後回し

### 6. ゲーム内 DAW（ウィンドウを Runtime と共通にする）
ここに書く案は 2026-11-27 時点のもの。

- **ねらい**
  - エディタの作曲ウィンドウを、ゲームの中でもそのまま動かす。
  - 使い道は、プレイヤーが曲を作るゲーム、ゲーム内の作曲ツール、実機でのライブ演奏など。
  - エディタでは今までどおり EditorWindow として使える。
- **できる見込み**：できると見ている。
  - UI Toolkit はランタイムでも使える。`UIDocument` と `PanelSettings` を使う。
  - Model / Audio / Midi / Theory はもともと UnityEngine の実行時 API に依存しない。
  - Composer 層は UIElements にも依存しない。
- **エディタ専用 API への依存**（Editor/Composer と Editor/UI の箇所数）
  - GenericMenu：18 箇所。ランタイムで動くポップアップメニュー部品に置き換える。
  - AssetDatabase とファイルダイアログ：約 20 箇所。保存先・読み込み元を `ISongStorage` のような口に分ける。エディタではアセット、ランタイムでは JSON（persistentDataPath）。
  - Undo（`Undo.RecordObject` / `undoRedoPerformed`）：`ComposerSession.ModifySong` に集まっている。`IUndoBackend` に分け、エディタは Unity の Undo、ランタイムは曲のスナップショットを積む自前のスタックにする。
  - EditorPrefs：PlayerPrefs に置き換える。
  - `EditorApplication.update` / `timeSinceStartup`：MonoBehaviour の Update と `Time.realtimeSinceStartup` に置き換える。
  - プレビュー音声（`Transport`）：ランタイムでは `UniDAWPlayer` と同じ `OnAudioFilterRead` の経路で鳴らす。
  - Claude 連携：API キー方式だけにする（`claude -p` はエディタ専用）。キーの扱いはゲームごとに決める。
- **進捗**
  - 2026-11-28（0.35.2）：段階 1 済み。時計・設定・Undo・メニューを `IDawPlatform`（`DawPlatform.Current`）、曲ファイルの操作を `ISongFiles` に分けた。GenericMenu は `DawMenu` に置き換え。エディタ実装は `Editor/UI/Host/`（`EditorDawPlatform`・`SongFileCommands`）。
  - 2026-11-28（0.36.0）：段階 2 済み。Composer と UI を `UniDAW.Composer`（`Composer/`、Editor 限定でない asmdef）へ移した。名前空間も `UniDAW.Composer`。Claude 送信・楽器プリセット・MIDI 入力の橋渡しも移し、毎フレームの呼び出し・ユーザーデータの置き場所・読み取り専用の判定・音の出口（`IAudioHost`）は `IDawPlatform` から受け取る。`UniDAWCli.CompilePlayer` でプレイヤー用のコンパイルが通ることを確かめた。
  - 2026-11-28（0.37.0）：段階 3 済み。画面一式を `ComposerRoot`（`UniDAW.Composer` の VisualElement）にまとめ、`UniDAWWindow` は載せるだけの殻になった。ゲーム内の殻 `UniDAWComposer`（UIDocument）・`RuntimeDawPlatform`（PlayerPrefs・スナップショット Undo・`GenericDropdownMenu`）・`RuntimeSongFiles`（persistentDataPath の JSON、自動保存）を作り、`UniDAWPlayer` を音の出口にした。サンプルシーンの代わりに GameObject メニューで置ける。
  - 残り：実機での見た目の確認（フォント・テーマ）、タッチ操作、IME、WebGL（オーディオスレッド・WinMM が無い）、配布ゲームでの Claude の API キーの扱い。
- **進め方**
  1. Composer と UI を新しい asmdef（例：`UniDAW.Composer`、Editor 限定にしない）へ移す。
  2. 上のエディタ専用 API を、インターフェース越しに呼ぶ形に変える。
  3. `UniDAWWindow` は、ホスト（EditorWindow / ランタイムの `UIDocument`）に `VisualElement` を載せるだけの薄い殻にする。
- **注意点**
  - USS のエディタ専用変数（`--unity-colors-*`）と、エディタ用フォントへの依存をなくす。
  - ランタイムのキー入力：IME と、Ctrl のキーをどう割り当てるか。
  - タッチ操作：ピアノロールのドラッグとホイール操作を、タッチではどうするか。
  - WebGL：オーディオスレッドと MIDI 入力（WinMM）が使えない。

## 進め方の案
1 → 2 を先に作る。どちらも曲の価値を外に見せやすく、トラッカーでもピアノロールでも同じように作れる。
3 は既存 API の延長なので、その後に足しやすい。

## 参考
- [Koreographer — Sonic Bloom](https://www.koreographer.com/koreographer)
- [Koreographer 紹介スレッド（Unity Discussions）](https://forum.unity.com/threads/koreographer-audio-driven-events-animation-and-gameplay.355243/)
- [Rhythm Timeline (Dypsloom)](https://dypsloom.itch.io/rhythm-timeline)
- [geekdrums/MusicEngine](https://github.com/geekdrums/MusicEngine)
- [Reactional Music for Unity](https://reactionalmusic.com/news/reactional-music-brings-interactive-music-to-unity/)
- [Game Audio Tools Index](https://gameaudiotools.dev/browse/?category=authoring)
- [MultiSequencer-Unity](https://github.com/gdean725706/MultiSequencer-Unity)
- [PatchWorld](https://patchxr.com/)
- [Virtuoso (SideQuest)](https://sidequestvr.com/app/1295/virtuoso)
- [procedural audio engine (itch.io)](https://enragedavian.itch.io/procedural-audio-engine)
- [Game Audio Development Guide 2026](https://generalistprogrammer.com/game-audio-development)
