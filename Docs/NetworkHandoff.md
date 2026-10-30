# ネット対戦 引き継ぎメモ

最終更新: 2026-10-30（Claude との作業セッションから引き継ぎ）
担当: 末原（suehara）

このメモだけで作業を再開できるように、方針・現状・既知の問題・次のタスクをまとめています。
タスクの一覧と優先度は別ファイル「ネット対戦_タスク一覧.xlsx」（ユーザーが保管）にもあります。

---

## 1. 前提（ユーザーと決めた仕様）

- つなぎ方: **まず同じ LAN 内で完成 → 後でインターネット越し（Unity Relay / Lobby）**
- 対応範囲: **本編と同じ一式**（メニュー → キャラ選択 → ステージ・ルール選択 → 試合（ストック/タイム/ゴースト）→ リザルト）
- 人数: 2〜4人。**1台に複数人・CPU の参加も入れる**
- 期間: 2〜3か月（2026年10月末〜1月ごろ）

## 2. 方式

- Netcode for GameObjects 2.x、Unity Transport。NetworkManager は NetStages シーンにある（シーン管理 ON）。
- **ホスト権威**。ホストだけがゲームのスクリプト（DarumaCharacter, TumikiBlock, DarumaHammer の当たり判定など）を動かす。
  クライアントではそれらを無効にし、NetworkTransform（Authority: Server）で位置を受け取って表示するだけ。
  クライアントの Rigidbody は NetworkRigidbody が Kinematic にする。
- 入力は持ち主の端末で読み、RPC でホストへ送る。ホストでは `IDarumaCommandSource` として DarumaCharacter が読む
  （元々の入力の抽象 `DarumaCommand` / `IDarumaCommandSource` をそのまま使っている）。
- **ローカル用とネット用のプレハブを分ける**。ローカル用（例: `Nakahira/Player/Prefabs/DarumaCharacter.prefab`）には
  ネット部品を付けない。付けると NetworkRigidbody が Kinematic にしてローカルプレイが動かなくなる（実際に起きた）。
  ネット用は Prefab Variant（`*_Net.prefab`）にしてネット部品を足す。

## 3. 作ったもの・変えたもの

### Assets/suehara/Network/（ネット専用）
| ファイル | 役割 |
|---|---|
| `DarumaCharacter_Net.prefab` | キャラのネット用 Variant。NetworkObject, NetworkTransform, NetworkRigidbody, NetworkCommandSource, NetworkDarumaCharacter |
| `Tumiki_Net.prefab` | 積み木のネット用 Variant。NetworkObject（AutoObjectParentSync=OFF）, NetworkTransform（スケール同期なし）, NetworkRigidbody, NetworkTumiki |
| `NetworkCommandSource.cs` | 持ち主が `NetPadKeyboardInput` で入力を読み、変化したとき・ボタンを押したときに `SubmitRpc` でホストへ送る。ボタンは読まれるまで溜める |
| `NetPadKeyboardInput.cs` | キーボード＋ゲームパッド（`Gamepad.current`）。割り当ては DarumaControls と同じ（左スティック/十字: 移動, A: ジャンプ, X: 薙ぎ払い, B: ショット / WASD, Space, J, K）。**`Application.isFocused` のときだけ読む**（同じPCで複数ウィンドウを動かすとパッドの入力が裏の画面にも届くため） |
| `NetworkDarumaCharactar.cs`（ファイル名の綴りミス。クラス名は `NetworkDarumaCharacter`） | クライアントで DarumaCharacter を無効化、ハンマーを `ViewOnly`、DarumaStack を無効化して代わりに頭の高さを段数（NetworkVariable）で動かす。ホストはハンマーの振り始めを検出して `PlayHammerRpc` を送る |
| `NetworkTumiki.cs` | ホストでは `Start` で自分を Spawn（生成側は普通に Instantiate しているだけなので）。色と状態フラグ（攻撃判定あり=炎 / 積まれている）を NetworkVariable で配る。クライアントでは TumikiBlock を無効化し、煙・炎のエフェクトを自分で切り替える。ネット開始前にできた積み木は `CreatedOffline` |
| `NetStageSpawner.cs` | 接続した人ごとにキャラを出す（出現位置の番号を人ごとに固定）。ステージ切り替えの開始でキャラを Despawn、表示後に出し直す |
| `NetStageSync.cs` | シーンに置いた NetworkObject。ステージ番号を NetworkVariable で配る。クライアントでは StageSwitcher と全 TumikiSpawner を無効化（積み木はホストから届く） |

### Assets/suehara/（テスト用）
- `NetStages.unity`: AllStages の複製。Match（DarumaMatch）は無効のまま。TumikiSpawner（全50個）の積み木プレハブは `Tumiki_Net` に差し替え済み。
- `NetTestUI.cs`: Host / Client / 切断ボタンと接続状態（左上のステージ名表示の下）。
- `net test.unity`, `Net Cube.prefab`, `NetCubeMover.cs`: 最初の接続テストの名残。

### 他の人のスクリプトに足したもの（ローカルの動きは変わらない）
| ファイル | 追加 |
|---|---|
| `Nakahira/Player/Scripts/DarumaHammer.cs` | `ViewOnly`（当たり判定と弾の発射をしない）, `PlayVisual(bool shot)`（振りの見た目を最初から再生） |
| `Nakagawa/Scripts/Stage/StageSwitcher.cs` | `SwitchStarting` / `StageApplied(int)` イベント, `StageCount`, `ShowStage(int)`（片付けせず表示だけ切り替え） |
| `Nakagawa/Scripts/Gimmicks/Common/GimmickClock.cs`（新規）＋ `RisingWallGimmick.cs` 2行 | 共有の時計。接続中はサーバー時間に寄せ、オフラインはローカル時間 |
| `Assets/DefaultNetworkPrefabs.asset` | Net Cube, DarumaCharacter_Net, Tumiki_Net の3件 |

## 4. 動作確認の状況

- ホスト側（Unity MCP で確認）: キャラの出現・移動、積み木の生成と Spawn、積み上げ、ハンマー、ステージ切り替え（キャラの出し直し・番号の同期）OK。エラーなし。
- ローカル（AllStages / ローカル用プレハブ）: 影響なし。
- クライアント側（ユーザーが Multiplayer Play Mode で確認）:
  - 修正済み: 積み木の炎が出っぱなし / クライアントのキャラが積み木に埋まる（頭の高さ）/ ホストの操作でクライアントも動く
  - **修正後のクライアント側の見え方は未確認**（次の作業の前に確認してもらう）

### テストのしかた
1. NetStages を開いて再生 → メイン画面で Host
2. Multiplayer Play Mode の Player 2 で Client
3. 操作する側のウィンドウをクリックしてから操作（入力はフォーカス中の画面だけ）
4. ステージ切り替えはホストの左右キー

### 注意
- Unity のウィンドウが裏にあると、Windows 版ではコンパイルが始まらないことがある（MCP から編集した後はユーザーに Unity をクリックしてもらう）。
- `DefaultNetworkPrefabs.asset` に NetworkObject のない `DarumaCharacter.prefab` のエントリが紛れ込んだことが2回あった。再生時に「missing a NetworkObject」の警告が出たら外す。

## 5. まだ同期していないもの（既知の穴）

- 試合のルール全体（DarumaMatch は NetStages で無効）: 開始カウントダウン・ストック・点数・脱落と復活・幽霊・勝敗・リザルト
- HUD（MatchHud）・カメラの追従（MatchCamera）
- RisingWall 以外のギミックの時間（回転台・扇風機・動く床・シューター・ワープ・バンパー・加速床・低重力）
- 積み木が壊れる演出、生成予告（TumikiSpawnWarning）、ポンと出る演出
- ヒットストップの揺れ・カメラの揺れ・効果音
- `Time.timeScale` を使う演出（EliminationCinematic・試合終了）: ホストの物理まで止まるので要検討
- 新キャラ7体（Yurube/Prefabs: Akadaru, Aodaru, DaruKing, Hebi, Iwa, Kidaru, Midaru）は Variant ではない独立プレハブ。ネット用がない
- **必殺技は各キャラの Special スクリプトが `Keyboard.current`（L）/ `Gamepad.current`（Y）を直接読んでいる**。
  DarumaCommand を通らないので、クライアントから出せない＆ホストの入力で他人の必殺技が出る恐れ。
  → DarumaCommand に Special を足して命令経由にするのが本筋（Yurube さんと相談）
- 特殊ブロック（Shitaka/, Yurube/Script/Blocks）、きいろだるのパッシブ・ファンネルの積み木生成
- 1台に複数人、CPU、LAN 接続画面、切断処理、Relay/Lobby

## 6. 次にやること（優先度順・タスク一覧の P0 から）

1. 修正版のクライアント確認（ユーザー）
2. 共有スクリプトを変更するときのルールをチームで決める
3. **DarumaMatch のネット対応**（最大の山。中平さんと相談）→ MatchPlayer の同期 → カウントダウン/時間 → HUD → 幽霊
4. 残りギミックの GimmickClock 対応
5. 積み木の演出・ヒット演出の同期、カメラ
6. LAN 接続画面（IP 表示・入力）、切断処理、PC 2台での LAN テスト
7. その後 P1: 新キャラのネット用 Variant、必殺技ボタンを DarumaCommand に追加、メニュー→ロビー→キャラ選択→リザルトのネット化
