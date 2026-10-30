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
