# アメイジングだるま落とし — Antigravity 引き継ぎメモ

最終更新: 2026-11-18
対象: Antigravity（これからメインで作業するAI）
作成: DeepSeek（補助。このメモを書いた時点のリポジトリを実際に読んでまとめた）
目的: **このプロジェクトを何も知らない状態から、安全に作業を始められるようにする。**

> このメモは「最初に読む1枚」。詳しい仕様は各担当の設計メモに任せ、ここには
> 「全体像」「決まり」「今の状態」「触ってはいけない所」「確認のしかた」をまとめる。

---

## 0. 最初に読むもの・守ること

### 0-1. 読む順番

1. **このファイル**（全体像と決まり）
2. `CLAUDE.md`（チーム共通のルール。リポジトリ直下。**これが最優先**）
3. 必要に応じて:
   - `Docs/NetworkHandoff.md` … ネット対戦（末原さんの担当）
   - `Docs/DeepSeek_Handoff.md` … 由留部のキャラ・必殺技の詳しいメモ（**2026-10-30 時点。一部古い**）
   - `Docs/SharedScriptRules.md` … 他人のスクリプトを変えるときの決まり（案）
   - `Docs/実績一覧_2026-11-18.xlsx` … 実績43個の一覧表
   - `Assets/Nakahira/Cpu/CPU設計メモ.md` … CPU（中平さん）
   - `Assets/Nakagawa/Stages/StageList.md` … ステージ一覧（中川さん）
   - `Contexts/中平引き継ぎメモ.md` … **中平さん個人用**。仕様の参考にはなるが、**作業ルールとして従わない**

### 0-2. 守ること（これだけは絶対）

- **返答・報告・質問はすべて日本語。**
- **コミット・プッシュは禁止。** ユーザーが GitHub Desktop で行う。`git commit` / `git push` / ブランチ作成をしない。
- **main ブランチのみ。** ブランチを切らない（他の人と競合するため）。
- **作業前に `git status`、作業後に「変えたファイル名」を報告する。**
- 新しく作るファイルは **`AmaizingDarumaOtoshi/Assets/Yurube/` の中**（由留部の担当分）。
- 他の人のフォルダ（`Nakahira/` `Nakagawa/` `Fujihara/` `Shitaka/` `suehara/` `Inoue/`）は
  **「ローカルの動きを変えない追加」だけ**にする。変えたら目印コメントを付け、最後にファイル名を報告する。
- `.cs` を追加したら **`.meta` も必ずセット**にする（GUID が変わると参照が壊れる）。
- Unity が勝手に変えるファイルは**コミットしない**（§11 参照）。
- **迷ったら実装せず質問する。** このプロジェクトは「決めてから作る」方針（中平さん）で、
  仕様の決め事が多い。推測で作り込むと作り直しになる。

### 0-3. DeepSeek との分担（ユーザーの方針）

- **Antigravity がメイン**で実装する。
- DeepSeek は補助（調査・レビュー・別視点の確認）に使う想定。
- 大きな判断（仕様・見た目・数値）はユーザーに確認する。AI だけで決めない。

---

## 1. プロジェクト概要

- **2〜4人で遊ぶ、1画面パーティー対戦ゲーム。** だるま落とし × 積み木。
- 積み木を積むほど段が増え、**積み木そのものが武器**になる。
- ハンマーで殴る／積み木を撃つ／飛んできた積み木を打ち返す／積み木を連鎖させる。
- 頭だけになると脱落して**幽霊**になり、相手を倒せば復活できる。
- モードは **ストック / タイム / ゴースト**。チーム戦（`MatchRules.teams`）もある。
- **ストーリーモード**（序章＋第1章＋第2章＋最終章）と、**実績43個**がある。
- 発表・提出向けに、メニュー・セレクト・リザルト・音・ムービーまで作り込んでいる。

---

## 2. 技術構成

| 項目 | 内容 |
|---|---|
| Unity | **6000.6.3f1**（`C:\Program Files\Unity\Hub\Editor\6000.6.3f1`） |
| レンダリング | URP 17.6.0 |
| 入力 | Input System 1.20.0（`Assets/Nakahira/Player/Input/DarumaControls.inputactions`） |
| ネット | Netcode for GameObjects 2.13.3 / Multiplayer Services 2.3.3 / Multiplayer Play Mode 3.0.0 |
| その他 | Timeline 6.6.0 / AI Navigation / TextMesh Pro / MCP for Unity 10.3.0 |
| スクリプト構成 | **asmdef なし。すべて `Assembly-CSharp`**（1つにまとまっている） |
| リポジトリ | `https://github.com/lovecap7/AmazingDarumaOtoshi`（main） |
| Unity プロジェクト | リポジトリ直下の **`AmaizingDarumaOtoshi/`**（`Amazing` ではなく **`Amaizing`**。綴りミスではない） |

- **Git 管理に `Temp/`（リポジトリ直下）や過去のビルド成果物が入っている。** 触らない・新しく追加しない。
- `AmaizingDarumaOtoshi/.gitignore` は Unity 用（`Library/` `Temp/` `Build/` などを無視）。
  ただし**リポジトリ直下の `Temp/` は別物で、Git に入っている**（17ファイル）。

### ビルドに入っているシーン（順番どおり）

`Assets/Fujihara/Scene/` の Title → Menu → Result → ResultTower → Select → MatchCheck、
`Assets/Nakagawa/Stages/AllStages.unity`、
`Assets/suehara/` の `net test.unity` → `NetStages.unity` → `NetConnectScene.unity`。

---

## 3. チームと担当フォルダ

| フォルダ | 担当 | 中身 |
|---|---|---|
| `Assets/Nakahira/` | 中平 | プレイヤー・試合・CPU・必殺技ゲージ・デバッグ |
| `Assets/Nakagawa/` | 中川 | ステージ・ギミック・カメラ・HUD・メニューの飾り（`AchievementBook` もここ） |
| `Assets/Fujihara/` | 藤原 | メニュー・セレクト・リザルト・音・キャラデータ・`SelectRoster` |
| `Assets/Yurube/` | **由留部（このメモの持ち主）** | キャラ7体・パッシブ・必殺技・実績・ストーリー・デバッグラボ |
| `Assets/Shitaka/` | 志鷹 | 特殊ブロック（`BlockController` など） |
| `Assets/suehara/` | 末原 | ネット対戦 |
| `Assets/Inoue/` | 井上 | 初期の試作（触らない） |
| `Assets/Scenes/` `TutorialInfo/` `TextMesh Pro/` | — | Unity の初期・標準 |

- 担当は**フォルダ名で決まる**。他の人のフォルダは原則触らない。
- ただし**由留部の作業は、他メンバーのファイルに「追加だけ」している箇所が多い**（§8）。
  過去に了承を得て入れたもの。Antigravity が触る場合は**さらに追加するだけ**にして、壊さない。

---

## 4. このPCの作業環境（重要）

- **`git` が PATH に通っていない。** GitHub Desktop 同梱の git を使う（読むだけ）:

```powershell
$git = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" |
       Select-Object -Last 1
& $git.FullName -C "C:\AmazingDarumaOtoshi" status --porcelain
& $git.FullName -C "C:\AmazingDarumaOtoshi" log --oneline -10
```

- Unity エディタ: `C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe`
- `dotnet` あり: `C:\Program Files\dotnet\dotnet.exe`（Unity を開かないコンパイル確認に使える。§10 の 4）
- `Library/ScriptAssemblies/Assembly-CSharp.dll` がある（前回ビルド済み。コンパイル確認に使う）。
- **PowerShell の既定は cp932。** 日本語の `.cs`・JSON を `Get-Content` すると文字化けする。
  ファイルを読むときは AI のファイル読み込み機能（UTF-8）を使う。PowerShell で読むなら `-Encoding utf8`。
- Unity のウィンドウが裏にあると、Windows 版は**コンパイルが始まらない**ことがある。編集後は Unity をクリックしてもらう。

---

## 5. 起動から試合までの流れ

1. `TitleScene` → `MenuScene`
2. メニュー項目: **たたかう / ものがたり / せってい / ずかん / じっせき / やめる**
   （`Assets/Fujihara/Script/Menu/MainMenu.cs`）
3. たたかう → `SelectScene`（キャラ・ステージ・ルール）→ `MatchCheckScene` → `AllStages`
4. 試合（`DarumaMatch` が進行）→ リザルト（`ResultTowerScene` / `ResultScene`）
5. ネット対戦は メニュー → たたかう → オンライン → `NetConnectScene` → `NetStages`（末原さん）

- 試合だけ確認したいときは **`Assets/Nakagawa/Stages/AllStages.unity` を直接開いて再生**してよい。
- 入力: ゲームパッド（左スティック移動 / A ジャンプ / X 薙ぎ払い / B ショット / L・Y 必殺技 / Start ポーズ）。
  メニューはキーボードでも操作できる。

---

## 6. ゲームルールの要点（AIが間違えやすい所）

コードは `Assets/Nakahira/Player/Scripts/`。詳細は `Contexts/中平引き継ぎメモ.md` §4（仕様の参考として読む）。

- **拾う**: 空中で落下中に触れた積み木は、状態・速さを問わず拾える。積み木は接地判定から除外。
- **積む**: 落ちている積み木に着地 → 一番下に追加。最大10段。超えたら一番上（古い段）が落ちる。高いほど遅い。
- **股抜きショット**: 後ろからハンマーで最下段を前へ打ち出す。まっすぐ滑り、壁で反射、奈落へ。
- **飛んでいる積み木**: 減速する。速さがしきい値以上だと攻撃判定あり（`TumikiBlock.IsHarmful`）。
- **打ち返し**: ハンマーの先端で捉えると左へ、根元だと右へ。弾速は 10 × 1.25^回数。打ち返した側は 0.5 秒無敵。
- **連鎖**: 飛んでいる積み木が積み木に当たると、当たられた側がビリヤードのように飛ぶ。当てた側は壊れる。
- **脱落**: 頭だけの状態で弾・ハンマーに当たる、または奈落 → 幽霊。幽霊は浮く・壁は通れない・当たり判定なし。
  幽霊が生存者を倒すと、遠い出現地点で頭だけ復活＋1.5秒無敵。
- **終了**: 2人以上いて生存者が1人以下になったら終了（復活より優先）。
- **ヒットストップ**: 全体の `timeScale` ではなく、**当たった人だけ**スロー（`Nakahira.HitStop`）。
- **時間を止める演出は `Nakahira.TimeStop` を使う**（`Assets/Nakahira/Player/Scripts/TimeStop.cs`）。
  必殺技のカットイン7つと `IwaImpactHitStop` はこれを使っている。`Time.timeScale` を直接触らない。
- **必殺技ゲージはポイント制**（2026-11-12 に中平さんが作り直した）。
  最大100（キャラごとの `SpecialGaugeParams`）、画面下に % 表示、満タンで「READY!」。
  **「積み木5個で満タン」という古いメモの記述は間違い。** 詳しくは §7-2。

---

## 7. 由留部の担当（今の姿）

### 7-1. キャラクター（7体＋報酬枠）

`Assets/Yurube/Script/` にパッシブと必殺技、`Assets/Yurube/Prefabs/` にプレハブ、
`Assets/Fujihara/Data/Characters/*.asset` にキャラデータ、`Assets/Fujihara/Data/SelectRoster.asset` に並び順。

| キャラ | id | パッシブ | 必殺技 |
|---|---|---|---|
| あかだる | `akadaru` | 赤い積み木の数だけ弾速UP | 終焉の軌跡（ビーム） |
| あおだる | `aodaru` | 青い積み木の数だけ移動速度UP | 六腕（阿修羅ハンマー） |
| みだる | `midaru` | 飛ばした緑の積み木がホーミング | ファンネル（積み木が分裂追尾） |
| いわだる | `iwadaru` | 積み木が岩になり、積み木を壊す／壁で砕ける | いわだる崩し（隕石） |
| きだる | `kidaru` | 黄色い積み木を積むと同じ黄色が1個増える | 黄金化（無敵＋加速、頭が車輪） |
| へびだる | `hebidaru` | 股抜きショットが蛇行 | 反転毒沼（移動低下＋入力反転） |
| だるきんぐ | `daruking` | 積み木は2段まで。3個目で古い段が落ちる | エクスカリバー（直線即死） |
| だるまおとし | `darumaotoshi` | （未実装。全実績解除の報酬枠） | （未実装） |

- **由留部のキャラは `NetworkObject` を持っていない**（ローカル専用）。ネット用は末原さんが
  `Assets/suehara/Network/Characters/*_Net.prefab`（Prefab Variant）を作っている。
  **ローカル用プレハブにネット部品を付けない**（ローカル対戦が壊れる）。
- キャラを増やす手順は `Docs/DeepSeek_Handoff.md` §4 が詳しい。ただし**今はゲージ（`SpecialGauge`）を
  プレハブに付ける必要がある**（2026-11-12 以降。§7-2）。
- **だるまおとしは作りかけ**。`darumaotoshi.asset` はあるが `prefab` も `model` も未設定、
  パッシブ名・必殺技名も `～～～` の仮。全実績解除でセレクトに並ぶため、
  実装するのか・誰がやるのかを**藤原さんと確認してから**触る。

### 7-2. 必殺技とゲージ（2026-11-12 に中平さんが仕様変更。戻さない）

- 共通の入口: `ISpecialAbility`（`Assets/Yurube/Script/ISpecialAbility.cs`）、
  入力は `SpecialInput`（キーボード L／パッド Y）、カットインは `SpecialCutIn` と各キャラの `*CutIn`。
- **ゲージは `SpecialGauge`（`Assets/Yurube/Script/SpecialGauge.cs`）**。中平さんが作り直した。
  - `Points` / `MaxPoints` / `Percent` / `Normalized`。**`Blocks` や `BlocksToFill` はもう無い。**
  - 最大は `SpecialGaugeParams`（`Assets/Nakahira/Special/Params/`）。共通 `SpecialGauge_Default` は最大100。
  - 7体のプレハブすべてに `SpecialGauge` が付いている（実行時に付ける方式ではない）。
  - 表示は `Assets/Nakahira/Special/Prefabs/SpecialGaugeHud.prefab`（Canvas）＋ `SpecialGaugeBar.prefab`。
    `Assets/Yurube/Script/SpecialGaugeHUD.cs` は**自分では描かない**（状態の文字を渡すだけ）。
  - 溜めない時間: カウントダウン中・`timeScale` 0・ポーズ中・試合終了後・必殺技の効果中（`IsActive`）。
  - やられたら最大の15%を足して次の体へ引き継ぐ（`DeathBonusRatio`）。
- **ネット対戦**: `Assets/Yurube/Script/SpecialNet.cs` と `INetSpecial`。
  - ホストだけが勝負に関わる処理を行い、クライアントは `SpecialNet.IsClient` で止める。
  - 入力は `NetworkCommandSource` から `SpecialNet.NetSpecialPressed` / `NetAnyActionPressed` で読む。
  - 区切りで `SpecialNet.Send`、クライアントは `NetReceive` で同じ見た目を出す。
  - ゲージの割合は `NetworkDarumaCharacter` が配る（`SpecialGauge.NetDriven`）。
- **触るときの注意**:
  - `SpecialGauge` の仕様は中平さんが決めた。由留部のファイルだが**勝手に旧仕様へ戻さない**。
  - だるきんぐの詠唱中、画面下中央のゲージと「エクスカリバー 詠唱」の枠が重なる（既知。由留部側で直せる）。
  - 新しい必殺技を足すときは `IsActive` を実装する（ゲージが溜まる／溜まらないに関係する）。

### 7-3. 実績（Achievements）

`Assets/Yurube/Achievements/`。**43個**。カテゴリは 試合 / 攻撃 / 連鎖 / 積み木 / 挑戦 / ストーリー。

| ファイル | 役割 |
|---|---|
| `AchievementCatalog.cs` | 実績の定義一覧（**増やすときは `CreateDefaults()` に1行足すだけ**） |
| `AchievementDefinition.cs` | `AchievementCategory` / `AchievementMode` / `AchievementMetric` の定義 |
| `AchievementManager.cs` | 判定・進捗・解除・セーブの中心。`AchievementEvents` を受ける |
| `AchievementEvents.cs` | ゲーム→実績の「お知らせ」の入口（本編もラボも同じ道を通る） |
| `AchievementTracker.cs` | 本編の出来事を `AchievementEvents` へ流す係。**シーンに置かなくても自動で1つ作られる** |
| `AchievementData.cs` | 1件分のセーブデータ（解除日時・進捗） |
| `AchievementRarity.cs` / `AchievementRarityIcons.cs` | レアリティ（銅・銀・金・プラチナ・ダイヤ）とアイコン |
| `AchievementRewards.cs` | 全解除の報酬キャラ（`darumaotoshi`）の解放判定 |
| `AchievementSettings.cs` | 設定（`Resources/AchievementSettings.asset`）。通知・リザルト表示・ロスター |
| `AchievementNotification.cs` | 解除通知（ゲーム中／リザルトでまとめて） |
| `AchievementResultPresenter.cs` | リザルト画面の実績カード（藤原さんの Result から呼ばれる） |
| `AchievementMenuBridge.cs` | メニュー「じっせき」へ一覧を流し込む（`AchievementBook` は中川さん） |
| `AchievementHints.cs` / `AchievementVisuals.cs` / `AchievementRevealStyle.cs` | ヒント・画像生成・演出 |

- **セーブ**: `PlayerPrefs` の `Achievements.Save.v1`（ラボは `Achievements.LabSave.v1` で別）。
- **実績IDは一度出したら変えない**（変えると解除済みが消える）。
- 解除条件は `AchievementMetric` ごとに `AchievementManager` が共通で判定する。増やすときに個別コードは不要。
- **増やし方**: `CreateDefaults()` に1行足す。引数は「ID / 名前 / ポエム / カテゴリ / 解除条件の文 /
  `AchievementMetric` / 目標数 / レアリティ」。`special`（レアリティ集計から除く）・`secret`（未解除は伏せる）・
  `mode`（対戦のみ／ストーリーのみ）・`storyChapter` は任意引数。**`AchievementMetric` を増やすときは必ず末尾に足す**
  （セーブの数値が変わらないように）。
- **ネット対戦の実績は未対応**（ホストでしか解除が起きない）。
- 一覧表: `Docs/実績一覧_2026-11-18.xlsx`（`AchievementCatalog.cs` から生成した共有用）。
- 確認: メニュー **`Yurube/実績ラボを開く`** → `Assets/Yurube/Debug/AchievementLab.unity`。
  `Yurube/実績ヒントの確認（デバッグ）` もある。ラボは本編のセーブを壊さない。

### 7-4. ストーリー（Story）

`Assets/Yurube/Story/`。**序章＋第1章＋第2章＋最終章の4章**（2026-11-16 に最終章まで実装）。

- 台本は **JSON**（コードを変えずに台詞を足せる）。場所:
  `Assets/Yurube/Story/Resources/Story/prologue.json` / `chapter1.json` / `chapter2.json` / `chapter3.json`
- 章データの主な項目（`StoryData.cs` の `StoryChapter`）:
  `id` `title` `hiddenTitle` `order` `requiredChapter` `stageId` `sceneName` `playerCharacter`
  `rules`（mode / stocks / timeLimit / cpuSpecial / awakening / gaugeFromStart / cpuBlockLimit）
  `cpu[]` `speakers[]` `before` `after` `defeat` `tutorial` `cutscene` `newspaper` `epilogue`
  ほか、初回だけの掛け合い（`reflectFirst` `chainFirst` `yellowExtra` `cpuTaunts` `startLines` `outLines` など）。
- 会話1行（`StoryLine`）の項目: `speaker`（空ならナレーション）/ `text` / `direction` / `eventId` / `portrait` / `scene`、
  **2026-11-18 に追加** `expression`（表情）/ `shot`（ショットID）/ `se`（効果音）。
  `shot` は `StoryMovie` の `ShotDefinition`（対象の `Transform` 基準）でカメラを切り替える（`S_ROOM_WIDE` / `S_WHITEBOARD` / `S_KIDARU` / `S_AKA` / `S_AO` / `S_MI` / `S_DOLL_FAR` / `S_DOLL_EYE` / `S_EMPTY_ROOM`）。`expression` と `se` はまだ未使用。JSON に書かなければ空になり、直前のショットを維持する。
- 会話の入力: `DialogueRunner` は**1回の押下を1回の操作にだけ使う**（同じフレームの A を二重に使わない）。
  以前は A 1回で2行進み、全章で台詞が1行おきに抜けていた（2026-11-18 修正）。
- 章選択: `StoryChapterSelect.cs`。メニューの「ものがたり」から開く。
  `Resources/Story/*.json` を `order` 順に積み木の段として並べる（**JSON を足すだけで増える**）。
- 開始: `StoryLauncher.Start(chapterId)` が `MatchSetup` を作り、`Nakagawa.StageSwitcher.RequestStage` で
  ステージを指定して `AllStages` を読み込む。**対戦の開始は既存の `DarumaMatch` をそのまま使う。**
- 進行: `StoryProgress`（`PlayerPrefs` の `Story.Clear.<章ID>`）、
  リトライ用の状態 `StorySession`（static。会話（前）を飛ばす・チュートリアルの続きから）。
- 対戦中の演出: `StoryMatchDirector.cs`（会話・チュートリアル・覚醒・必殺技イベント・新聞・エピローグ・
  敗北→リトライ）。`TutorialController.cs` が操作チュートリアル、`DialogueRunner.cs` が会話UI、
  `StoryMovie.cs` が序章の部室ムービー、`StoryNewspaperArt.cs` が新聞の1枚絵。
- 調整値: `Assets/Yurube/Story/Resources/StorySettings.asset`（文字送り速度・小窓の秒数など）。
- デバッグ: メニュー **`Yurube/ストーリー/...`**（進行リセット・各章をクリア済みにする）。
- **章を足す手順**: JSON を1枚足す（`id` / `order` / `requiredChapter` / `stageId` を決める）→
  章選択とセーブは自動。コード変更は不要。ステージIDは `Assets/Nakagawa/Stages/StageList.md` を見る。
- 小さな不整合: `StorySession.HasStoryInProgress()` のコメントは「最終章がまだ実装されていない間は…」だが、
  今は最終章（`chapter3`）まで実装済み。`PlannedChapterSlots = 4` のままで動きに問題はないが、
  コメントが古い（低優先度）。

### 7-5. 特殊ブロックの複製（`Assets/Yurube/Script/Blocks/`）

- 志鷹さんの `Assets/Shitaka/script/` を `Yurube.Blocks` 名前空間に複製したもの（名前衝突を避けるため）。
- **今はどこからも参照されていない**（プレハブ・シーンから参照なし）。本編の正は `Assets/Shitaka/`。
- 触る前に「何のために残しているか」をユーザーに確認する。勝手に消さない。

### 7-6. ラボ・デバッグ

| 場所 | 開き方 | 用途 |
|---|---|---|
| `Assets/Yurube/Debug/CharacterLab.unity` | シーンを開いて再生 | キャラのパッシブ・必殺技の確認。F3 で当たり判定表示 |
| `Assets/Yurube/Debug/AchievementLab.unity` | メニュー `Yurube/実績ラボを開く` | 実績の解除・進捗・リザルト表示の確認（別セーブ） |
| 実績ヒント | メニュー `Yurube/実績ヒントの確認（デバッグ）` | ヒント文の確認 |
| ストーリー進行 | メニュー `Yurube/ストーリー/...` | 章の解放を試す |

- `CharacterLabUI` は `NetSessionMenu` の開発表示リストにも入っている（ネット対戦時に隠れる）。

---

## 8. 他の人のファイルにある、由留部のしくみが関わる箇所

`// ストーリー用(Yurube)` `// 実績用(Yurube)` の目印が付いた追加と、
藤原さん側が由留部の API（`AchievementRewards` など）を呼んでいるだけの箇所が混在している。
**触る前に、そのファイルの担当者に確認**し、由留部の追加部分を壊さないこと。

| ファイル（担当） | 関わっている由留部のしくみ |
|---|---|
| `Fujihara/Script/Menu/MainMenu.cs`（藤原） | メニュー「ものがたり」「じっせき」の入口、物語が途中なら初期カーソルを「ものがたり」に、`AchievementBook` の生成、カメラ設定の持ち越し防止、報酬キャラの抽選 |
| `Fujihara/Script/Select/MatchSetup.cs`（藤原） | `StoryNames`（名札をだるまの名前に）、`displayNameOverride`（章ごとの別名） |
| `Fujihara/Script/Select/MatchSelect.cs`（藤原） | 報酬キャラ（`darumaotoshi`）を全解除まで一覧に出さない・ランダムから除く |
| `Fujihara/Script/Game/MatchDebugView.cs`（藤原） | ストーリー中はリザルト表示・セレクト案内を出さない |
| `Fujihara/Script/Result/ResultScreen.cs` / `ResultTowerScreen.cs`（藤原） | `AchievementResultPresenter.Play()` の呼び出し |
| `Nakahira/Player/Scripts/DarumaCharacter.cs`（中平） | 操作命令を受け取った通知（チュートリアル判定用。挙動は変えない） |
| `Nakahira/Player/Scripts/DarumaStack.cs`（中平） | チュートリアルの CPU だけ積み木の上限を下げる |
| `Nakahira/Cpu/Scripts/CpuSpecial.cs`（中平） | 章の設定で CPU の必殺技を止める |
| `Nakahira/Special/Scripts/SpecialGaugeHud.cs`（中平） | 序章の覚醒前はゲージを出さない（通常対戦は常に表示） |
| `Nakagawa/Scripts/Spawn/TumikiSpawner.cs`（中川） | 連鎖・必殺技の補助で積み木を出す（ローカルの挙動は変えない） |
| `Nakagawa/Scripts/UI/PauseMenu.cs`（中川） | 会話中はポーズを出さない・ストーリー中は「中断」に変える |
| `Nakagawa/Scripts/UI/StartCountdownView.cs`（中川） | 会話中はカウントダウンを隠す |
| `Nakagawa/Scripts/Tumiki/TumikiImpactModeToggle.cs`（中川） | デバッグ表示をエディタ／Development Build だけに |
| `Nakagawa/Scripts/Menu/AchievementBook.cs`（中川・新規） | メニュー「じっせき」の画面。`AchievementMenuBridge` が中身を入れる |

- **これらを変更するときは、まず由留部の追加部分を壊さないこと。** 仕様を変えるならユーザーに確認。

---

## 9. 今の状態（2026-11-18 時点）

### 直近のコミット

| コミット | 日付 | 内容 |
|---|---|---|
| `9cfea50` | 11-18 | 氷のステージにCPUが対応（中平） |
| `86c7f57` | 11-18 | だるきんぐ3段目溢れを修正（中平） |
| `aec20d5` | 11-17 | Merge |
| `e32df5c` | 11-17 | ものがたり選択メニュー修正（由留部） |
| `30511fc` | 11-16 | 最終章実装（由留部） |

### 未コミット（このメモを書いた時点）

- `AmaizingDarumaOtoshi/Assets/Yurube/Achievements/AchievementCatalog.cs`
  … コメントの実績数を 40 → 43 に直しただけ（2行）。
- `Docs/実績一覧_2026-11-08.xlsx` を削除、`Docs/実績一覧_2026-11-18.xlsx` を追加
  … 実績43個に更新した共有用の一覧表。

→ **ユーザーが GitHub Desktop でコミットする**（AI はコミットしない）。

### できている

- キャラ7体＋パッシブ＋必殺技＋カットイン。ゲージは中平さん方式（ポイント制）に移行済み。
- 実績43個（判定・セーブ・通知・リザルト・メニュー・ラボ）。
- ストーリー4章（序章・第1章・第2章・最終章）。会話・カットシーン・チュートリアル・新聞・エピローグ・リトライ。
- ネット対戦の必殺技・パッシブ同期（末原さんの担当。クライアント側も確認済み）。

### 未対応・要確認

- **だるまおとし（報酬キャラ）の実体**（プレハブ・モデル・パッシブ・必殺技が未設定）。
- 実績のネット対戦対応（今はホストだけ）。
- だるきんぐの調整値（中平さんが変更。`DaruKing.prefab` の `m_strikeDelay`・`m_chargeTurnSpeed` など）を遊んで確認する。
- ゲージと「エクスカリバー 詠唱」枠の重なり（見た目）。
- `Yurube/Script/Blocks/`（未使用の複製）を残すか消すか。
- `StorySession.HasStoryInProgress()` の古いコメント（動作に影響なし）。

---

## 10. 動作確認のしかた

1. **Unity で再生**（`TitleScene` から。試合だけなら `AllStages` を直接開いてもよい）。
2. `Console` にエラーが出ていないか確認。**エラーを残したまま終わらない。**
3. 変更した機能に合わせて:
   - キャラ → `CharacterLab`、または `AllStages` で実際に対戦
   - 実績 → `Yurube/実績ラボを開く`、リザルト画面、メニュー「じっせき」
   - ストーリー → メニュー「ものがたり」→ 章を選ぶ。進行リセットは `Yurube/ストーリー/...`
4. **Unity を開かずにコンパイル確認**（構文・API のチェックだけ。見た目は分からない）:

```powershell
# 応答ファイル（/reference: を並べる）を作って csc を実行する方式。
# 詳しい手順と参照.dllの並べ方は Docs/DeepSeek_Handoff.md §8。
$csc = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll"
dotnet $csc @response.rsp
```

- 参照: `AmaizingDarumaOtoshi/Library/ScriptAssemblies/Assembly-CSharp.dll` と
  `.../Editor/Data/Managed/UnityEngine/*.dll`。
- **最終確認は必ず Unity で。** コンパイルが通っても見た目・挙動は分からない。
- MCP for Unity（`com.coplaydev.unity-mcp` 10.3.0）がパッケージとして入っている（Claude 用に設定済み）。
  Antigravity から使うには MCP の接続設定が別に必要。使う場合も**MCP からビルドしない**
  （URP 設定・ProjectSettings・TextMesh Pro・PerformanceTestRun が変わる）。
  スクリーンショット後にエディタが一時停止することがある（エラーではない）。

---

## 11. 壊しやすい所・落とし穴

1. **`.meta` を付け忘れる／GUID が重複する。** `.cs`・`.prefab`・`.asset` を追加したら必ず `.meta` もセット。
   既存ファイルをコピーして作ると GUID が重複しやすい。**新規 GUID にする。**
2. **マージコンフリクトマーカー（`<<<<<<<`）が残ったままコミットされたことがある**（過去にキャラが消えた）。
   アセット（`.asset` `.prefab` `.unity`）にマーカーが残っていないか確認する。
3. **ローカル用プレハブにネット部品を付けない。** `NetworkRigidbody` が Kinematic にしてローカル対戦が動かなくなる
   （実際に起きた）。ネット用は `*_Net.prefab`（Prefab Variant）。
4. **`Assets/DefaultNetworkPrefabs.asset` に NetworkObject のないプレハブを入れない**（警告が出る）。
   Unity が勝手に並べ替えることがあるので、意図した変更以外はコミットしない。
5. **`TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`** は再生中に勝手に変わる。
   コミットに入れず元に戻す。
6. **`ProjectSettings/*`・`Assets/Settings/*`・`Assets/Resources/PerformanceTestRun*.json`** も自動変更されやすい。
7. **`Time.timeScale` を直接触らない。** `Nakahira.TimeStop` を使う。触ったら必ず戻す（再生を止めても戻す）。
8. **`Resources` の使い方**: 中平さんのルールでは禁止だが、由留部の実績・ストーリーは `Resources` を使っている
   （`AchievementSettings` / `StorySettings` / 章JSON）。**新しく増やすときはユーザーに確認**（方針が分かれている）。
9. **キャラのプレハブと `CharacterData` の対応**: プレハブ root の fileID `9069936943766670842` は
   `CharacterData` から参照されるので変えない。`prefab` の GUID を差し替える。
10. **会話中・カットイン中**の `PauseMenu`／カウントダウン／ゲージの表示は、既存の分岐を壊さない。
11. **実績ID・章ID・セーブキーは変えない**（`PlayerPrefs` に残っていて、解除済み・クリア済みが消える）。
12. **`Assets/Nakagawa/Backup~/`・`Assets/_Recovery/`・`Temp/`（直下）** は触らない。
13. **日本語の文字化け**: PowerShell の既定 cp932 で `.cs` を読むと壊れて見える。ファイル読み込み機能か `-Encoding utf8` を使う。
14. **Unity が裏にあるとコンパイルされない**。編集後は Unity をクリックしてもらう。
15. **ビルドに入れるシーンを勝手に増減しない**（`ProjectSettings/EditorBuildSettings.asset` は共有設定）。
16. **`MenuInput.Submit()` などは「このフレームで押されたか」を返す**（何回呼んでも同じフレームなら true）。
    コルーチンで「押したら次へ」を続けて書くと、1回の押下で何段も進む。押下を使ったフレームを覚えて二重に使わない
    （`DialogueRunner` の `AdvancePressed` / `SkipPressed` が例）。

---

## 12. 次にやる候補（優先度はユーザーと決める）

1. 未コミットの片付け（`AchievementCatalog.cs` のコメント、実績一覧 xlsx）。
2. **だるまおとし（報酬キャラ）の実装方針を決める**（藤原さんと役割分担。プレハブ・モデル・性能）。
3. **最終章の通し確認**: 章選択 → 会話 → 試合 → 新聞 → エピローグ → 章クリア保存 → リトライ。
4. **実績の実機確認**: 解除通知／リザルトのカード／メニュー「じっせき」／セーブの引き継ぎ。
5. ゲージと「エクスカリバー 詠唱」枠の重なりを直す（由留部側）。
6. 実績のネット対戦対応（末原さんと相談）。
7. `Yurube/Script/Blocks/`（未使用の複製）の扱いを決める。
8. 小さな整理: `StorySession.HasStoryInProgress()` のコメント、`DeepSeek_Handoff.md` の古い記述
   （「ゲージは積み木5個で満タン」など）を直す。

---

## 13. Antigravity に最初に貼るプロンプト（コピペ用）

```
このリポジトリは Unity 6000.6.3f1 の対戦ゲーム「アメイジングだるま落とし」です。
Unity プロジェクトは AmaizingDarumaOtoshi/（綴りに注意）。チーム開発で、私は由留部担当です。

まず以下を読んでください。
1. Docs/Antigravity_Handoff.md（引き継ぎメモ）
2. CLAUDE.md（チーム共通ルール）

守ってほしいこと:
- 返答は日本語
- main ブランチのみ。コミット・プッシュは絶対にしない（私が GitHub Desktop で行う）
- 新規ファイルは AmaizingDarumaOtoshi/Assets/Yurube/ の中
- 他の人のフォルダは「ローカルの動きを変えない追加」だけ。変えたらファイル名を報告
- .cs を追加したら .meta もセット
- 作業前に git status、作業後に変更ファイルを報告
- 迷ったら実装せず、質問してから進める

このPCは git に PATH が通っていないので、GitHub Desktop 同梱の git を使ってください:
$git = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" | Select-Object -Last 1
& $git.FullName -C "C:\AmazingDarumaOtoshi" status --porcelain

最初は変更せず、git status と直近のコミット、未コミットの内容を確認して、
「今の状態」と「次にやるとよさそうなこと」を日本語で報告してください。
```

---

## 14. このメモの更新ルール

- 大きな実装をしたら §7（担当）と §9（今の状態）を更新する。
- 仕様を決めたら §6 か §7 に追記する（決めた人・日付も書く）。
- 落とし穴を踏んだら §11 に1行足す。
- 担当や環境が変わったら §3・§4 を直す。
- 更新した日付を冒頭の「最終更新」に書く。

---

## 付録A. ファイル早見表

| 用途 | 場所 |
|---|---|
| チーム共通ルール | `CLAUDE.md`（リポジトリ直下） |
| ネット対戦 | `Docs/NetworkHandoff.md`、`Assets/suehara/Network/` |
| 由留部の旧メモ（キャラ追加手順） | `Docs/DeepSeek_Handoff.md` |
| 共有スクリプト変更の決まり | `Docs/SharedScriptRules.md` |
| 実績一覧表 | `Docs/実績一覧_2026-11-18.xlsx` |
| キャラデータ | `Assets/Fujihara/Data/Characters/*.asset` |
| キャラの並び | `Assets/Fujihara/Data/SelectRoster.asset` |
| 由留部のプレハブ | `Assets/Yurube/Prefabs/*.prefab` |
| 由留部のスクリプト | `Assets/Yurube/Script/` |
| 実績 | `Assets/Yurube/Achievements/`、`Assets/Yurube/Debug/AchievementLab.unity` |
| ストーリー | `Assets/Yurube/Story/`（台本は `Resources/Story/*.json`） |
| キャラ検証ラボ | `Assets/Yurube/Debug/CharacterLab.unity` |
| 試合の調整値 | `Assets/Nakahira/Player/Params/` |
| ゲージの調整値 | `Assets/Nakahira/Special/Params/` |
| ステージ一覧 | `Assets/Nakagawa/Stages/StageList.md` |
| ステージ生成 | メニュー `Nakagawa/Stages/...` |
| ネット用シーン作り直し | メニュー `Tools/ネット対戦/NetStages を AllStages から作り直す` |

## 付録B. 用語

- **プレイヤー** … 操作する人（`DarumaPlayer`）。**キャラクター** … ゲーム内の体（`DarumaCharacter`）と幽霊（`DarumaGhost`）。
- **積み木** … `TumikiBlock`。**タワー** … キャラに積まれた段（`DarumaStack`）。
- **ハンマー** … `DarumaHammer`（薙ぎ払い・ショット）。**幽霊** … 脱落後の姿。
- **試合** … `DarumaMatch` が進行。**リザルト** … 順位・積み木タワーの表彰。
- **じっせき** … 実績。**ずかん** … キャラ図鑑（`CharacterBook`）。**ものがたり** … ストーリーモード。
