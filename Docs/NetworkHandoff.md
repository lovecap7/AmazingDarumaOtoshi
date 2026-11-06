# ネット対戦 引き継ぎメモ

最終更新: 2026-11-04（Claude との作業セッション4回目: NetStages を AllStages から作り直し）
担当: 末原（suehara）

このメモだけで作業を再開できるように、方針・現状・既知の問題・次のタスクをまとめています。
タスクの一覧と優先度は別ファイル「ネット対戦_タスク一覧.xlsx」（ユーザーが保管）にもあります。
他の人のスクリプトに手を入れるときの決まりは `Docs/SharedScriptRules.md`（案。タスク A-01）。

---

## 1. 前提（ユーザーと決めた仕様）

- つなぎ方: **まず同じ LAN 内で完成 → 後でインターネット越し（Unity Relay / Lobby）**
- 対応範囲: **本編と同じ一式**（メニュー → キャラ選択 → ステージ・ルール選択 → 試合（ストック/タイム/ゴースト）→ リザルト）
- 人数: 2〜4人。**1台に複数人・CPU の参加も入れる**
- 期間: 2〜3か月（2026年10月末〜1月ごろ）

## 2. 方式

- Netcode for GameObjects 2.13.3、Unity Transport。NetworkManager は NetStages シーンにある（シーン管理 ON）。
  再生中は NetworkManager が DontDestroyOnLoad に移る。
- **ホスト権威**。ホストだけがゲームのスクリプト（DarumaMatch の試合の進行、DarumaCharacter, TumikiBlock, DarumaHammer の当たり判定、ギミックの押し出しなど）を動かす。
  クライアントではそれらを無効にし、NetworkTransform（Authority: Server）で位置を受け取って表示するだけ。
  クライアントの Rigidbody は NetworkRigidbody が Kinematic にする。
- 入力は持ち主の端末で読み、RPC でホストへ送る。ホストでは `IDarumaCommandSource` として読まれる。
  **入力は1人分（NetPlayer）に付いていて、キャラクターではない**。キャラ・幽霊を出し直しても同じ入力を DarumaMatch がつなぐ。
- **ローカル用とネット用のプレハブを分ける**。ローカル用（例: `Nakahira/Player/Prefabs/DarumaCharacter.prefab`）には
  ネット部品を付けない。付けると NetworkRigidbody が Kinematic にしてローカルプレイが動かなくなる（実際に起きた）。
  ネット用は Prefab Variant（`*_Net.prefab`）にしてネット部品を足す。

### 試合（DarumaMatch）のネット対応のしくみ

- DarumaMatch は、同じ GameObject に `INetMatchDriver`（実体は `NetMatchDriver`）があるときだけ「ネットの試合」になる。無ければ今までどおり。
- NetStages の Match は最初は非表示。接続すると `NetMatch` が NetMatchDriver を付けてから表示する（DarumaMatch.Awake がそれを見る）。
- **ホスト**: 試合はいつもどおり DarumaMatch が進める。
  - 出る人は接続した人（`AddNetPlayer` / 切断で `RemoveNetPlayer`）。ボタンを押した人の途中参加・セレクトの内容は使わない。
  - キャラ・幽霊は Instantiate の直後に `NetMatchDriver.OnBodySpawned` で Spawn して全員に配る。
  - 試合の開始は `BeginNetMatch`。それまでは「開始待ち」（`IsNetWaiting`）で、時間が進まず、やられても少しして復活し、試合は終わらない。
  - 試合が終わったら、ローカル（MatchResultRelay）と同じ流れ（11/03 から）:
    「しあい しゅうりょう！」を2秒見せる → 全員でリザルト画面（ResultTowerScene）→ ホストの決定（または30秒）で全員 NetStages に戻る（開始待ち）。
    シーンの切り替えは NGO の SceneManager でホストが全員分行う（`NetSceneFlow`）。試合のたびにシーンを読み直すので、試合中のやり直しはなくした。
    結果は全員の `ResultData` に入れる（クライアントは積み木を持っていないので、タワーの積み木の色はホストから送る）。
    戻ったときのステージは `StageSwitcher.RequestStage` で同じものにする。ルールの選択も覚えておく。
  - DarumaMatch に入れていた「その場でやり直す」分岐（UpdateResult / Restart の `BeginNetMatch`）は、要らなくなったので消した（11/03。中平さんに伝える）。
    試合が終わったら `NetMatch` が `DarumaMatch.AutoReturnToSelect = false` にして、DarumaMatch が自分でシーンを読み直さないようにする。
    ホストのステージ切り替え（左右キー）も、終わったあとは SwitchTo → Restart でシーンを読み直してしまうので、`NetMatch` が StageSwitcher を止める。
    MatchSettings の「リザルト（表彰）」を使わない設定でも、ネット対戦ではリザルト画面を通って戻る。
- **クライアント**: DarumaMatch は試合を進めない（Update/LateUpdate で何もしない）。
  届いた人・時間・結果を `NetClient〜` メソッドで入れ、同じお知らせ（Started, Finished, PlayerKO など）を出す。
  HUD（MatchHud）・カウントダウン（StartCountdownView）・カメラ（MatchCamera）・名札（MatchDebugView）は、いつもどおり DarumaMatch を読むだけで動く。
- 1人分の中身（状態・ストック・点数・記録・積み木数・順位・いまの体）は `NetMatchPlayer` が毎フレームまとめて送る（変わったときだけ）。
  クライアントは積み木を持っていないので、積み木の数は `MatchPlayer.NetStackCount` で受け取る。
- 時間・カウントダウンは `NetMatch` が 0.05 秒刻みで送り、クライアントは間を自分で進めて、0.15 秒以上ずれたら合わせる。

## 3. 作ったもの・変えたもの

### Assets/suehara/Network/（ネット専用）
| ファイル | 役割 |
|---|---|
| `NetMatch.cs` | シーンの NetworkObject（NetMatch）。接続した人ごとに NetPlayer を出す（定員4人。それ以上は見るだけ）。ルール・時間・試合の始まりと終わり・お知らせを配る。ホストは `StartMatch(rules)` で開始 |
| `NetMatchDriver.cs` / `INetMatchDriver.cs` | Match に付けてネットの試合にする部品。出したキャラ・幽霊を Spawn する |
| `NetMatchPlayer.cs` / `NetPlayer.prefab` | 接続した人ごとの1人分（PlayerObject）。NetworkCommandSource（入力）と NetMatchPlayer（中身の同期）。MatchPlayer は実行時に付ける |
| `NetEffects.cs` | NetMatch と同じ GameObject。ホストの生成予告を見た目だけの予告で出す / 割れた積み木の破片を出す |
| `NetLobbyUI.cs` | NetStages の接続まわり。つないでいないときの中央の接続パネル（NetStages を直接開いたときの試験用。クリック操作）、切断したときの戻り（接続画面から来たときは接続画面へ）、NetSessionMenu を自動で付ける。リザルトから戻ったとき、シーンに入っている2つ目の NetworkManager を消す |
| `NetSessionMenu.cs` | NetStages でつないでいる間の表示とメニュー（2026-11-04。クリックなし）。右上にいまの試合の設定・へやばんごう・人数。Start（+）/ Tab でメニュー（見た目は中川さんの PauseMenu と同じ）: ホストの開始待ちは「試合を はじめる / モード / ストック / 時間 / へやを かいさん / とじる」、試合中とさんかは「かいさん（ぬける）/ とじる」。ホストが変えたルールは `NetMatch.SetRules` ですぐ全員に配る。開いている間はキャラの入力（NetPadKeyboardInput）と、OnGUI で描く開発用の表示（StageSwitcher・TumikiImpactModeToggle・SpawnPointDebugView・StageCameraTuner・CharacterLabUI。OnGUI はどの UI よりも上に出るため）を止める。**Yurube さんの必殺技のゲージ・カットインも OnGUI なのでメニューの上に出る**（止めると必殺技が止まるのでそのまま）。ネット対戦は時間を止められないので、試合中に開いても試合は進む |
| `NetSceneFlow.cs` | ネットのシーン遷移（自動で1個作られ、シーンをまたいで残る）。ホストの `Load(scene)` で全員を暗くしてから NGO で切り替え、読み終わったら明るくする。リザルトで切断したら NetStages に戻す |
| `NetResultFlow.cs` | 接続中にリザルト画面が読み込まれたら自動で付く。演出が終わったら画面自身のセレクト行きを止め（`done` / `leaving` をリフレクションで読み書き）、ホストの決定で NetStages に戻す。**ResultTowerScreen / ResultScreen のフィールド名が変わったら直す** |
| `DarumaCharacter_Net.prefab` | キャラのネット用 Variant。NetworkObject, NetworkTransform, NetworkRigidbody, NetworkDarumaCharacter, NetworkHammerView |
| `DarumaGhost_Net.prefab` | 幽霊のネット用 Variant。NetworkObject, NetworkTransform（スケール同期なし）, NetworkDarumaGhost, NetworkHammerView |
| `Tumiki_Net.prefab` | 積み木のネット用 Variant。NetworkObject（AutoObjectParentSync=OFF）, NetworkTransform（スケール同期なし）, NetworkRigidbody, NetworkTumiki |
| `NetworkCommandSource.cs` | 持ち主が `NetPadKeyboardInput` で入力を読み、変化したとき・ボタンを押したときに `SubmitRpc` でホストへ送る。ボタンは読まれるまで溜める |
| `NetPadKeyboardInput.cs` | キーボード＋ゲームパッド（`Gamepad.current`）。**`Application.isFocused` のときだけ読む** |
| `NetworkDarumaCharactar.cs`（ファイル名の綴りミス。クラス名は `NetworkDarumaCharacter`） | クライアントで DarumaCharacter と DarumaStack を止め、頭の高さを段数（NetworkVariable）で動かす |
| `NetworkHammerView.cs` | ハンマーの振りの見た目。ホストで振り始めたらクライアントで同じ振りを再生（クライアントのハンマーは ViewOnly） |
| `NetworkDarumaGhost.cs` | クライアントで DarumaGhost を止める |
| `NetworkTumiki.cs` | ホストでは `Start` で自分を Spawn。色と状態（炎 / 積まれている / ポンと出る途中）を配る。クライアントで煙・炎・ポンと出る演出。割れて消えたら NetEffects に知らせる |
| `NetStageSync.cs` | ステージ番号を配る。クライアントでは StageSwitcher と全 TumikiSpawner を止める |
| `NetConnectScreen.cs`（`suehara/NetConnectScene.unity`） | **本番の入り口**（2026-11-04）。メニューの「たたかう → オンライン」から来る。見た目はセレクト画面と同じ（同じ GameObject に藤原さんの `TitleClubRoomDressing` を付けて部室の背景）。「ホスト」→ へやばんごう・IP・集まった人（1P〜4P）を出し、A / Start で全員 NetStages へ（`NetSceneFlow.Load`）。「さんか」→ へやばんごう（4けた）か IP アドレスをキーボードで入れてつなぐ。NetworkManager はプレハブ（`Network/NetworkManager.prefab`。NetStages のものと同じ設定）から作る。NetStages で切断したら（NetLobbyUI・NetSceneFlow）ここに戻ってくる |
| `NetRoom.cs` | へやばんごうで LAN の中のホストを見つける。ホストは UDP 47777（使われていれば 47778〜47780）で問い合わせを待ち、番号が合えばゲームのポートを返す（シーンをまたいで答え続け、ホストをやめたら消える）。さんかはブロードキャストとこの PC に問い合わせる。**LAN の中だけ。インターネット越しにするならここを Unity Relay の参加コードに置き換える** |

消したもの: `NetStageSpawner.cs`（NetMatch に置き換え）、`suehara/NetTestUI.cs`（NetLobbyUI に置き換え）

### Assets/suehara/（テスト用）
- `NetStages.unity`: AllStages の複製（2026-11-04 に作り直し。ステージ89個）。Match（DarumaMatch）は非表示で置いておき、接続したら NetMatch が表示する。
  Match のキャラ・幽霊・積み木は `*_Net` を設定済み。TumikiSpawner（全90個）の積み木も `Tumiki_Net`。
  UnityTransport の切断判定は 10 秒（DisconnectTimeoutMS）。
  **AllStages にステージや設定が足されたら、メニューの「Tools → ネット対戦 → NetStages を AllStages から作り直す」で取り込む**
  （`suehara/Editor/NetStagesBuilder.cs`）。中身を AllStages で置き換えてから、今の NetStages にあるネット用のルート
  （NetworkManager・NetLobbyUI・NetStageSync・NetMatch。AllStages に無い名前のルート）を写し、Match を非表示にし、
  積み木・キャラ・幽霊のプレハブへの参照を `*_Net` に差し替える。シーンの GUID・ビルド設定はそのまま。
  NetStages だけに入れた、ネット用のルート以外の変更は消えるので、そういう変更は AllStages か、ネット用のルートの中に入れる。
- `net test.unity`, `Net Cube.prefab`, `NetCubeMover.cs`: 最初の接続テストの名残。

### 他の人のスクリプトに足したもの（どれも `ネット対戦用(suehara)` の目印付き。ローカルの動きは変わらない）
| ファイル（作成者） | 追加 |
|---|---|
| `Nakahira/Player/Scripts/DarumaMatch.cs`（中平さん） | ネットの試合の分岐（上の「しくみ」）。`IsNetworked` / `IsNetClient` / `IsNetWaiting`、`SetNetRules`、`AddNetPlayer` / `RemoveNetPlayer` / `BeginNetMatch`、`NetClient〜`。試合中に相手が抜けて1人になったら残った人の勝ち（ストック・ゴースト）。その場でやり直す分岐は 11/03 に消した |
| `Fujihara/Script/Game/MatchPlayer.cs`（藤原さん） | `NetStackCount`、`ResetForMatch`（再戦）、`NetApply` / `NetSetBody`（クライアントで値を入れる） |
| `Nakahira/Player/Scripts/DarumaHammer.cs`（中平さん） | `ViewOnly`, `PlayVisual(bool shot)`（前回） |
| `Nakagawa/Scripts/Stage/StageSwitcher.cs`（中川さん） | `SwitchStarting` / `StageApplied(int)` / `StageCount` / `ShowStage(int)`（前回） |
| `Nakagawa/Scripts/Gimmicks/Common/GimmickClock.cs`（新規）（中川さんのフォルダ） | 共有の時計。接続中はサーバー時間に寄せる。`IsNetClient` を追加 |
| `Nakagawa/Scripts/GimmickUtil.cs`（中川さん） | ネットのクライアントでは影響を受けるものを見つけない（ギミックがクライアントの積み木・キャラを動かさない。ワープがクライアントのキャラの Kinematic を外す不具合の対策） |
| `RisingWallGimmick` / `TurntableGimmick` / `FanGimmick` / `MovingFloorGimmick`（中川さん） | GimmickClock で時間を進め、高さ・角度・位置を時間から決める。時計を合わせて一度に動いたときは乗っているものを運ばない |
| `Nakagawa/Scripts/Spawn/TumikiSpawnWarning.cs`（中川さん） | 見た目だけの予告 `CreateView`（時間が来ても生成しない） |
| `Fujihara/Scene/MenuScene.unity`（藤原さん。了承済み） | MainMenu の `onlineSceneName` に `NetConnectScene` を入れた（この1か所だけ） |
| `ProjectSettings/EditorBuildSettings.asset` | `suehara/NetConnectScene.unity` を追加 |
| `Assets/DefaultNetworkPrefabs.asset` | Net Cube, Tumiki_Net, DarumaCharacter_Net, DarumaGhost_Net, NetPlayer の5件 |

## 4. 動作確認の状況

- ホスト側（Unity MCP で確認、エラーなし）:
  - 接続 → キャラが出る / ルールを選んで試合開始 → カウントダウン → やられてストックが減る → 時間切れで終了 → 自動でやり直し
  - ゴーストで幽霊が出る（入力もつながる）/ ステージ切り替えで全員出し直し
  - 回転台・扇風機・移動床がローカルでも今までどおり動く
  - 「ホストをやめる」でシーンを読み直して最初の画面に戻る / 届かない IP に参加すると約10秒で理由を出して戻る
- ローカル（AllStages）: エラーなし、今までどおり（試合のネット対応を入れたあとに確認）。
- クライアント側（2026-11-03、Multiplayer Play Mode の Player 2）:
  - 接続 → 試合開始 → 時間切れ → 自動でやり直しまで通った
  - 不具合1: やり直しても「タイムアップ！/しょうしゃ」が残る（ホストも同じ）。その場でやり直していたのが原因
    → ローカルと同じ「試合 → リザルト → 戻る（シーンを読み直す）」の流れにして解決。
    `MatchDebugView`（藤原さん）に入れていた追加（名札の二重防止・結果を消す）は要らなくなったので元に戻した（今は藤原さんのオリジナルのまま）
  - 試合 → リザルト → NetStages → もう一度試合、の流れを Player 2 と確認済み（11/03）
  - 直した不具合2: クライアントに、見えるのに乗れない・触れない積み木ができる。
    接続前にクライアントのステージが出していた生成予告が、接続後にクライアントだけで積み木を作っていた
    → `NetStageSync` で接続時に予告も消す。`NetworkTumiki` はクライアントで作られた積み木を消して警告を出す（他の出どころの検出も兼ねる）
  - 残りの項目（HUD のストック・積み木数、カメラ、やられて復活、幽霊、ギミック、切断）も問題なし（ユーザーが目で確認）
- ホストのまま再生を止めると、NGO の終了処理で NullReferenceException が2件出る（パッケージ内。先に Shutdown してから止めると出ない）。
- リザルト画面で、テクスチャが消えて水色になる部分が出たことが1回ある（どちらの画面かは不明。次の確認では出なかった）。また出たら画面と場所を記録する。
- 2026-11-04: NetStages を作り直したあと、ホストで接続 → 試合開始 → 積み木の生成（ネットワークに乗っている）→ 新しく入ったステージ（DebugStage50）への切り替えまでエラーなし。
  クライアント側は作り直したあと未確認（シーン内の NetworkObject の識別番号は計算し直してある）。
- 2026-11-04: 接続画面（NetConnectScene）。メニュー → オンライン → ホスト / さんか（へやばんごう・IP アドレス）→ 全員で NetStages へ →
  ホストをやめる・切断で接続画面に戻る、まで問題なし（ユーザーが確認）。移る途中でホストが止まったときも接続画面に戻る。
- **PC 2台の LAN テストは未実施**。

### テストのしかた
本番の流れ: MenuScene から再生 →「たたかう → オンライン」→ ホストは「ホスト」、参加する人は「さんか」でホストの画面のへやばんごうを入れる → ホストが A / Start で試合のシーンへ →
ホストが Start（+ / Tab）のメニューでルールを選んで「試合を はじめる」（かいさん・ぬけるもこのメニュー）。

NetStages を直接開いて試すとき:
1. NetStages を開いて再生 → 画面中央の「ホストになる」
2. Multiplayer Play Mode の Player 2 で、IP に `127.0.0.1` を入れて「参加」
   （別の PC からはホストの左上に出る IP を入れる。初回は Windows のファイアウォールの許可が出る）
3. ホストが Start（+ / Tab）でメニューを開き、モード・ストック・時間を選んで「試合を はじめる」（開始待ちのときだけ。試合が終わったらリザルトのあと、ホストの画面で決定（Enter など）すると戻ってくる）
4. 操作する側のウィンドウをクリックしてから操作（入力はフォーカス中の画面だけ）
5. ステージ切り替えはホストの左右キー
6. ビルドでは `-nethost` でホスト、`-netclient <IP>` で参加して始められる

### 注意
- Unity のウィンドウが裏にあると、Windows 版ではコンパイルが始まらないことがある（MCP から編集した後はユーザーに Unity をクリックしてもらう）。
- `DefaultNetworkPrefabs.asset` に NetworkObject のない `DarumaCharacter.prefab` のエントリが紛れ込んだことが2回あった。再生時に「missing a NetworkObject」の警告が出たら外す。
- **PC 2台でエディター同士だとつながらない（ビルドの exe ならつながる）とき**: ホストにする PC の Windows ファイアウォールで、Unity エディター（例: 「Unity 6000.6.3f1 Editor」）の受信が「パブリック」でブロックになっている（2026-11-06 に確認。ネットワークの種類がパブリックのとき）。
  exe は別の規則で許可されているのでつながる。「Windows Defender ファイアウォールを介したアプリまたは機能を許可」で Unity エディターの「パブリック」にチェックを入れる（管理者の権限が必要。参加する側の PC は変えなくてよい）。変えられない PC では、ホストは exe で立てる。
- エディタで再生すると `TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset` が変わることがある。コミットに入れずに元に戻す。
- MCP の撮影（screenshot）のあと、エディタが一時停止したままになることがあった（エラーではない）。止まっていたら再生を続ける。
- **MCP からビルドすると、その間 Unity と MCP が止まる**。さらに URP の設定・ProjectSettings・TextMesh Pro のフォント・`Assets/Resources/PerformanceTestRun*.json` が変わるので、コミットに入れずに元に戻す。

## 5. まだ同期していないもの（既知の穴）

- 試合
  - 接続してきた人は試合中でもすぐ参加する（ストックは満タンで出る）。次の試合から参加にするかは要検討
  - ルールの選択はホストのテスト用画面だけ（本番はセレクト画面から。B-08）
  - クライアントでは無敵中の点滅、名札・HUD の「頭の高さ」（積み木の高さ）が出ない
- ギミック
  - シューターの矢印・加速床の輪などの飾りの点滅はホストとずれる（見た目だけ）
  - バンパーが弾いたときの膨らみ、シューター・加速床・ワープ・低重力の演出はクライアントで出ない（ホストの結果の位置は届く）
  - ワープの瞬間移動は、クライアントでは NetworkTransform の補間ですべるように見える
- ヒットストップの揺れ・カメラの揺れ・効果音（A-09, C-06。MatchAudio は Match を後から表示するので付かない）
- `Time.timeScale` を使う演出（EliminationCinematic）: ホストだけスローになり、ホストの物理も遅くなる（B-11）
- 新キャラ7体（Yurube/Prefabs: Akadaru, Aodaru, DaruKing, Hebi, Iwa, Kidaru, Midaru）は Variant ではない独立プレハブ。ネット用がない
- **必殺技は各キャラの Special スクリプトが `Keyboard.current`（L）/ `Gamepad.current`（Y）を直接読んでいる**。
  DarumaCommand を通らないので、クライアントから出せない＆ホストの入力で他人の必殺技が出る恐れ。
  → DarumaCommand に Special を足して命令経由にするのが本筋（Yurube さんと相談。B-03）
- 特殊ブロック（Shitaka/, Yurube/Script/Blocks）、きいろだるのパッシブ・ファンネルの積み木生成
- 1台に複数人、CPU、Relay/Lobby

## 6. 次にやること（優先度順・タスク一覧から）

1. ~~クライアント側の確認~~（2026-11-03 済み。上の「動作確認の状況」）
2. 共有スクリプトのルール案（`Docs/SharedScriptRules.md`）をチームで決める。今回触ったファイルを作成者に伝える（A-01）
3. PC 2台で LAN テスト（A-13。フェーズ1の完了条件）
4. その後 P1: ヒット演出の同期（A-09）、新キャラのネット用 Variant（B-01）、必殺技ボタンを DarumaCommand に追加（B-03）、
   メニュー→ロビー→キャラ選択→リザルトのネット化（B-06〜B-10）、スロー演出（B-11）
   （B-09 シーン遷移・B-10 リザルトは「試合 → リザルト → NetStages」の形で一部済み。セレクト画面のネット化（B-07）ができたら、戻り先をそこにする）
