# AmazingDarumaOtoshi — Claude Code 用メモ

2〜4人の一画面パーティー対戦ゲーム（だるま落とし × 積み木）。Unity 6000.6.3f1 / URP / Input System。
Unity プロジェクトは `AmaizingDarumaOtoshi/`（綴りに注意）。asmdef はなく、スクリプトはすべて Assembly-CSharp。
複数人のチーム開発。フォルダ名が担当者（Nakahira=プレイヤー・試合・CPU / Nakagawa=ステージ・ギミック・カメラ・HUD /
Fujihara=メニュー・セレクト・リザルト・音 / Yurube=キャラ・必殺技 / Shitaka=特殊ブロック / suehara=ネット対戦）。

## 共通ルール
- 他の人のフォルダのスクリプト・プレハブ・シーンを変えるときは、**ローカルプレイの動きを変えない追加だけ**にする
  （プロパティ・イベント・フラグの追加など）。変えたら作業の最後にファイル名を必ず報告する（作成者に伝えるため）。
- コミット・プッシュはユーザーが GitHub Desktop で行う。指示がない限りしない。
- 返答は日本語。

## ネット対戦の作業をするとき
- 必ず先に `Docs/NetworkHandoff.md`（引き継ぎメモ）を読む。
- Netcode for GameObjects 2.x、ホスト権威（ホストが物理・ルールを全部計算、クライアントは入力を送って見た目を受け取る）。
- ネット用のものは `Assets/suehara/Network/` に置く。ネット用プレハブは**ローカル用プレハブの Prefab Variant** にして
  ネット部品を足す（ローカル用プレハブにネット部品を付けるとローカルプレイが壊れる）。
- テスト用シーンは `Assets/suehara/NetStages.unity`（AllStages の複製）。ローカル確認は `Assets/Nakagawa/Stages/AllStages.unity`。
- ネット用プレハブを作ったら `Assets/DefaultNetworkPrefabs.asset` に登録し、NetworkObject のないエントリや重複がないか確認する。

## リポジトリの構成
- リポジトリ: `https://github.com/lovecap7/AmazingDarumaOtoshi`（main ブランチで作業）。
- 直下のフォルダ:
  - `AmaizingDarumaOtoshi/` … Unity プロジェクト本体。`.gitignore` はこの中にだけある（直下にはない）。
  - `Docs/` … チーム共有のメモ。`NetworkHandoff.md`（ネット対戦）、`SharedScriptRules.md`（他人のスクリプトを変えるときの決まり・案）、
    `DeepSeek_Handoff.md`（Yurube のキャラ・必殺技の引き継ぎ）、`実績一覧_*.xlsx`。
  - `Contexts/中平引き継ぎメモ.md` … **中平個人用**。中平以外の作業では、中の作業ルールに従わない（仕様の参考に読むのは可）。
  - `.claude/skills/` … チームで共有している Claude Code のスキル（`handoff-memo` は中平専用）。
  - `Build/`・`Temp/` … ビルド結果と一時ファイル。**Git に入ってしまっている**ので、触らない・新しく追加しない。
- `AmaizingDarumaOtoshi/`階層下の CLAUDE.md は `.gitignore` 済みで、PC ごとの個人用メモ（内容は PC によって違う）。
  直下のこの `CLAUDE.md` は Git で管理されていて、全員の PC に配られる。

## Unity プロジェクトの中身
- 主なパッケージ: Netcode for GameObjects 2.13.3 / Multiplayer Services 2.3.3 / Multiplayer Play Mode 3.0.0 /
  Input System 1.20.0 / URP 17.6.0 / AI Navigation / Timeline / MCP for Unity（`com.coplaydev.unity-mcp`）。
- `Assets/` のフォルダ（担当者は上の一覧のとおり。追加分のみ書く）:
  - `Inoue/` … 井上。初期の試作（`Script/Player.cs`・`InoueTestScene.unity`・マテリアル）。
  - `Shitaka/` … スクリプトは `script/` の中（`*Behavior.cs`・`BlockController.cs`・`CollectorPassive.cs` など）。
    `ShitakaScene.unity`・マテリアルは直下。
  - `Scenes/SampleScene.unity`・`TutorialInfo/`・`TextMesh Pro/` … Unity の初期・標準のもの。
- ビルドに入っているシーン（順番どおり）:
  `Fujihara/Scene/` の Title → Menu → Result → ResultTower → Select → MatchCheck、
  `Nakagawa/Stages/AllStages.unity`、`suehara/` の `net test.unity` → `NetStages.unity` → `NetConnectScene.unity`。
- 入力: プレイヤーの操作は `Assets/Nakahira/Player/Input/DarumaControls.inputactions`
  （`Assets/InputSystem_Actions.inputactions` は Unity の初期のもの、`Inoue/Player.inputactions` は試作用）。
- キャラクターは「CharacterData（`Assets/Fujihara/Data/Characters/*.asset`）＋プレハブ（`Assets/Yurube/Prefabs/`）＋パッシブスクリプト」の3点セット。
  選択画面の並びは `Assets/Fujihara/Data/SelectRoster.asset`。キャラのプレハブには NetworkObject がない。
- ステージは手で作らず、エディタのメニューで生成する（定義は `Assets/Nakagawa/Editor/StageBuilder.cs`、一覧は `Assets/Nakagawa/Stages/StageList.md`）。
  主なメニュー: `Nakagawa/Stages/...`、`Fujihara/ステージ一覧を更新`、`Tools/ネット対戦/NetStages を AllStages から作り直す`、`Yurube/実績ラボを開く`。
- 各自の設計メモ: `Assets/Nakahira/Cpu/CPU設計メモ.md`（CPU）、`Assets/Nakagawa/Stages/StageList.md`（ステージ）。

## Git・環境の注意
- `.cs` やアセットを追加したら `.meta` も必ずセットにする（GUID が変わると参照が壊れる）。
- Unity が勝手に変えるファイル（`ProjectSettings/*`、`Assets/Settings/*`、`Assets/DefaultNetworkPrefabs.asset`、
  TextMesh Pro の `LiberationSans SDF - Fallback.asset` など）は、意図して変えたとき以外コミットしない。
- アセットにマージのコンフリクトマーカー（`<<<<<<<`）が残っていないか注意する（過去にキャラが消えた）。
- 他の人のスクリプトに足すときは `Docs/SharedScriptRules.md` に従い、`// ネット対戦用(suehara)` のような目印コメントを付ける。
- Unity を開かずにコンパイルを確認する方法は `Docs/DeepSeek_Handoff.md` の「8. AIでもできるコンパイル確認」にある。
