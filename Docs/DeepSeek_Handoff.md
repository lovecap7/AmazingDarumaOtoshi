# DeepSeek 引き継ぎメモ（Yurube / アメイジングだるま落とし）

最終更新: 2026-10-30
このファイルは、別のPC・別ワークスペースの DeepSeek に作業内容を引き継ぐためのメモです。
作業を始めるときは、このファイルを読ませる（または貼り付ける）ところから始めてください。

---

## 0. まずAIに伝えること（重要）

- プロジェクト: Unity 6（**6000.6.3f1**）の3D対戦パーティーゲーム「アメイジングだるま落とし」
- リポジトリ: `https://github.com/lovecap7/AmazingDarumaOtoshi`
- Unityプロジェクトのフォルダ名は **`AmaizingDarumaOtoshi`**（スペルミスではない。正式名）
- 自分の作業フォルダ: `AmaizingDarumaOtoshi/Assets/Yurube/`
- Git運用:
  - **main ブランチのみ**。ブランチは切らない（自分と競合するため）
  - **コミット・プッシュは本人が行う。AIは絶対にコミット・プッシュしない**
  - 作業前に必ず `git status` を確認する
- 共有ファイル（`Assets/Nakahira/`、`Assets/Fujihara/`、`Assets/Nakagawa/` など他人のフォルダ）は原則変更しない。
  必要な場合は「なぜ必要か」を説明し、変更は最小限（1〜2行）に留める。
- 変更したら必ずコンパイル確認（下記 8.）を行い、コミット対象ファイルを提示する。

---

## 1. プロジェクト概要

- 4人対戦。積み木を積むほど体力（段数）が増え、積み木そのものが武器になる。
- キャラクターは「CharacterData（ScriptableObject）＋ プレハブ ＋ パッシブスクリプト」の3点セット。
- キャラクター選択の一覧は `Assets/Fujihara/Data/SelectRoster.asset` の `characters` に並ぶ。
- 自分の担当は **プレイヤーの派生キャラクター**（コスト表で「由留部」の行）。
- 企画書とコスト表はリポジトリ外（Downloads など）にある。ゲーム内には入っていない。

---

## 2. 実装済みキャラクター（2026-10-30 時点・すべてコミット済み）

| キャラ | パッシブ | 主なスクリプト |
|---|---|---|
| あかだる | 赤い積み木の数だけ弾速UP（+1.0 m/s / 個、上限60） | `AkadaruPassive.cs` |
| あかだる（演出） | 弾速に応じた残光トレイル＋マズルフラッシュ／衝撃波 | `AkadaruShotTrail.cs` / `AkadaruShotFlash.cs` |
| あかだる（必殺技） | 終焉の軌跡（旧トロン）: 通常射撃のトレイルに沿って分割判定。壁反射も追従。触れた積み木は壊れ、キャラクターは0.4秒間隔で積み木を1個ずつ失う（0個なら即死）。発動直後0.6秒は自分無敵。F3／ラボで判定表示。**通った地面は一定時間焼け焦げる**（自作シェーダー、約6秒で消える）。発動時はゲームを止める短い3Dカットイン（発動瞬間に実際の頭へ大胆にアップ／赤黒い集中線／衝撃波3回／画面揺れ／フラッシュ／画面下中央に大型「終焉の軌跡」、約1.3秒、旧・積み木／ハンマー／レーザー／破片演出は削除、終了時に複製を明示破棄） | `AkadaruSpecial.cs` / `TronProjectile.cs` / `TronTrailHit.cs` / `TronHitboxView.cs` / `TronScorchTrail.cs` / `Shaders/TronScorch.shader` / `SpecialCutIn.cs` |
| あおだる | 青い積み木の数だけ移動速度UP（+1.0 m/s / 個、上限12） | `AodaruPassive.cs` |
| あおだる（演出） | 青い風＋青い残像 | `AodaruSpeedEffect.cs` / `AodaruGhost.cs` |
| あおだる（必殺技） | 六腕: ハンマーのモデルを6本出し（阿修羅風）、発動時の向きを基準にした3方向から近づく積み木を、操作に関係なくはじき返す（4秒間） | `AodaruSpecial.cs` / `AodaruSixArms.cs` |
| みだる | 自分が飛ばした緑の積み木が相手をホーミング | `MidaruPassive.cs` / `TumikiHoming.cs` |
| みだる（必殺技） | ファンネル: ボタンを押すとその場で自分の積み木を撃ち、「積んでいる数」に分裂して近くの敵へ手分けしてホーミング | `MidaruSpecial.cs` / `TumikiHoming.cs` |
| いわだる | 飛ばした積み木は積み木を壊し、壁で壊れる（岩の破片表示つき） | `IwaPassive.cs` / `IwaBlock.cs`（`IwaImpact` 含む） |
| きだる | 黄色い積み木を積むと、同じ黄色がもう1個増える（上限10段） | `KidaruPassive.cs` |
| きだる（必殺技） | 黄金化: 無敵＋移動速度UP。発動時の積み木の数だけ延長（基本3秒＋1秒/個、速度1.5倍）。点滅せず虹色にモデルが光る。頭を一番下にして積み木を上に乗せ、頭が車輪のように回転。終了で元に戻る | `KidaruSpecial.cs` / `KidaruWheelVisual.cs` |
| へびだる | 股抜きショットで飛ばした自分の積み木が左右に蛇行（ハンマーで飛ばした積み木は対象外） | `HebiPassive.cs` / `TumikiZigzag.cs` |
| だるきんぐ | 積み木は2個まで。3個目を積むと一番上（古い段）が落ちる | `DaruKingPassive.cs` |

> トロンの当たり判定と焦げは `AkadaruShotTrail.m_trail`（通常射撃トレイル）基準。
> 見た目の `TronLaserOverlay`（太いグロー＋白い芯、2秒）は別トレイルで、判定より太く長い。

### 2-1. 共通システム

- 必殺技ゲージ: `Assets/Yurube/Script/SpecialGauge.cs`
  - 全キャラ共通。積み木5個で満タン。
  - あかだるはプレハブに付与済み。あおだる・みだる・きだるは各パッシブが実行時に追加する。
- 検証用キャラクターラボ: `Assets/Yurube/Debug/`
  - `CharacterLab.cs` / `CharacterLabBlocks.cs` / `CharacterLabDummy.cs` / `CharacterLabUI.cs`
  - ラボの項目から各キャラのパッシブ・必殺技を再現して確認できる
    （例: 「きだる: 黄色1個」「きだる: 必殺技」「へびだる: 積んでショット」「あかだる: 必殺技」「あおだる: 必殺技」「みだる: 必殺技」など）

### 2-2. アセットの場所

- キャラクターデータ: `Assets/Fujihara/Data/Characters/{akadaru,aodaru,midaru,iwadaru,kidaru,hebidaru,daruking}.asset`
- プレハブ: `Assets/Yurube/Prefabs/{Akadaru,Aodaru,Midaru,Iwa,Kidaru,Hebi,DaruKing}.prefab`
- 頭マテリアル: `Assets/Yurube/Materials/{AodaruHead,MidaruHead,IwaHead,KidaruHead,HebiHead,DaruKingHead}.mat`
- トロンの焦げ: `Assets/Yurube/Resources/TronScorch.mat` / `Assets/Yurube/Shaders/TronScorch.shader`

---

## 3. 共通の実装パターン（新キャラを作るときはこれに合わせる）

### 3-1. キャラクター側のパッシブ

```csharp
[RequireComponent(typeof(DarumaCharacter))]
public class XxxPassive : MonoBehaviour
```

- 速度系は `DarumaCharacter.RequestSpeedScale()` を使う。
  - `[DefaultExecutionOrder(-100)]` を付け、`FixedUpdate` で **1固定ステップに1回だけ**掛ける
    （毎フレーム掛けると倍率が累積して速度が爆発する）
- 弾速系は `AkadaruPassive` と同様に `HammerParams` を `Instantiate` してキャラ専用に書き換える
  （元のアセットは触らない）

### 3-2. 弾・積み木側の効果（共有スクリプトを変更しない方法）

飛び始めた積み木はキャラクターの正面 約1m にいるので、`FixedUpdate` で周囲3mを
`Physics.OverlapSphereNonAlloc` して、次の条件の積み木にコンポーネントを付けます。

- `block.CurrentState == TumikiBlock.State.Flying`
- `ReferenceEquals(block.Owner, m_character)`（自分が飛ばしたもの）
- まだ付けていなければ `AddComponent<...>()` して `Begin(...)`

同じ積み木を何度も拾わないよう `ConsumedLaunch` フラグを持ち、
`block.StateChanged` で Flying 以外になったらフラグを戻す（＝投げ直せばまた付く）。

### 3-3. 積み木の挙動を変える

- 当たったときの挙動: `TumikiBlock.ImpactBehavior` に `ITumikiImpactBehavior` 実装を差し替え（いわだる）
- 飛んでいる向き: `TumikiBlock.SetFlyingVelocity(dir * speed)`（みだるのホーミング／へびだるの蛇行）
- 見た目: `ParticleSystem` は `Assets/Nakahira/Player/Effects/FireParticle.mat` を流用
  （URP Particles/Unlit・加算合成。青などは `startColor` / `_BaseColor` で着色）

---

## 4. 新キャラクター追加の手順（チェックリスト）

1. `Assets/Yurube/Script/<Name>Passive.cs` を作成（＋ `.meta` を新規GUIDで）
2. `Assets/Yurube/Materials/<Name>Head.mat` を作成（`Nakahira/Player/Materials/DarumaHead.mat` の色違い＋ `.meta`）
3. `Assets/Yurube/Prefabs/Akadaru.prefab` を複製して `<Name>.prefab` を作成（＋ `.meta`）
   - root の `m_Name` を変更
   - 頭の `m_Materials` の GUID を新しいマテリアルへ
   - パッシブの `m_Script` の GUID と `m_EditorClassIdentifier` を差し替え
   - root の fileID `9069936943766670842` は CharacterData から参照されるので変えない
4. `Assets/Fujihara/Data/Characters/<name>.asset` を作成（＋ `.meta`）
   - `m_Script` は CharacterData（guid `c8a751e747833b74c9fdf9b27cff5e69`）
   - `prefab:` に 3 の prefab GUID と fileID `9069936943766670842`
   - `color`（アイコン色）、`passiveColor`（TumikiColor: 0=赤,1=青,2=緑,3=黄）、
     `passiveCount`（0にすると選択画面で「いつも」表示）、`passiveName/Text`、`hammerPower/Speed/Reach`
5. `Assets/Fujihara/Data/SelectRoster.asset` の `characters:` に1行追加
6. Unity で開いて確認 → 本人がコミット

---

## 5. Git・コミットの注意

- コミット対象は基本 `Assets/Yurube/` と、追加したキャラの `Assets/Fujihara/Data/...` のみ。
- Unity が勝手に変更するファイルはコミットしない:
  - `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`
  - `ProjectSettings/*`、`Assets/Settings/*`、`Assets/DefaultNetworkPrefabs.asset` などの自動変更
- `.cs` を追加したら **必ず `.meta` もセットで**コミットする（GUIDが変わると参照が壊れる）。
- プレハブ・アセットを追加したときは GUID の重複がないか確認する。

---

## 6. これまでに直した不具合（再発防止）

1. **マージコンフリクトマーカーがコミットされていた**
   - `Assets/Fujihara/Data/Characters/akadaru.asset` に `<<<<<<<` が残り、Unityが読み込めずキャラが消えた。
   - 対処: 両方の変更（prefab参照＋passive項目）を残して解消。
2. **NetworkPrefabs の宙ぶらりん参照**
   - `DefaultNetworkPrefabs.asset` が削除済み prefab の GUID を指していたため削除。
3. **ParticleSystem の duration エラー**
   - 実行時に追加した ParticleSystem が再生中のまま `main.duration` を設定してエラー。
   - 対処: GameObject を非アクティブで作って設定 → アクティブ化 → `Play()`。`Stop(true, StopEmittingAndClear)` も併用。
4. **Particle Velocity curves must all be in the same mode**
   - `velocityOverLifetime` の x/y/z/radial のカーブモードを揃える（すべて `MinMaxCurve(0,0)` など同じモードに）。
5. **あおだるの残像がモデルと重なって点滅**
   - 残像を「今の姿勢」ではなく **0.1秒前の姿勢**から生成し、現在位置と 0.6m 未満なら出さない。
   - 残像は 0.95 倍スケール・不透明度 0.30〜0.55。

---

## 7. 既知の仕様・注意点

- あかだるの弾速UPは **色に関係なく全弾**に乗る（`HammerParams.ShotSpeed` は1つしかないため）。
  赤の枚数は「撃ち始めた瞬間」で固定。打ち返し（sweep）は対象外。
- あかだるの弾の演出中は、その積み木の既存の煙・炎（`Tumiki.prefab` の SmokeTrail / FireAura）を
  インスタンス単位で止めている（共有プレハブは変更していない）。
- 各キャラのプレハブは **NetworkObject を持っていない**ため、通信対戦では使えない（現状はオフライン想定）。
- パッシブ演出（あかだるの弾トレイル、あおだるの速度エフェクトなど）は実行時にコンポーネントを追加する方式。
- 必殺技は、あかだるがプレハブに `SpecialGauge` / `AkadaruSpecial` を追加済み。あおだる・みだる・きだるは
  各パッシブが実行時に `SpecialGauge` と必殺技コンポーネントを追加する。

---

## 8. AIでもできるコンパイル確認（Unityを開かずに）

Unity の Roslyn と、前回ビルド済みの `Assembly-CSharp.dll` を使って構文・API を確認できます。

- Unity: `C:\Program Files\Unity\Hub\Editor\6000.6.3f1\...`
- csc: `...\Editor\Data\DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll`
- 参照: `AmaizingDarumaOtoshi/Library/ScriptAssemblies/Assembly-CSharp.dll` と `.../Managed/UnityEngine/*.dll`
- 実行: `dotnet <csc.dll> @response.rsp`（`/reference:` を並べた応答ファイルを使う。コマンドラインが長くなるため）

※ これはあくまでコンパイル確認。見た目・挙動の最終確認は Unity で行う。
※ 大学のPCに `dotnet` が無い場合は、Unity を開いて Console を見るだけでも可。

---

## 9. 未対応・今後の候補

- あかだるの「赤い積み木のときだけ弾速UP」への変更検討（現状は全色）
- 通信対戦対応（NetworkObject 付与）※共有プレハブに関わるため要相談
- chara3〜6 の実装（`prefab: {fileID: 0}` のまま）
- 各キャラの数値・演出のブラッシュアップ（必要に応じて）

---

## 10. 学校でAIに最初に貼るプロンプト（コピペ用）

```
このリポジトリでUnity（6000.6.3f1）のゲームを開発しています。
まず `Docs/DeepSeek_Handoff.md` を読んで、これまでの作業内容とルールを把握してください。

守ってほしいこと:
- 自分の作業フォルダは AmaizingDarumaOtoshi/Assets/Yurube/
- main ブランチのみ。ブランチは切らない
- コミット・プッシュは自分で行うので、AIは絶対にコミット・プッシュしない
- 変更前に git status を確認し、他人のフォルダ（Nakahira/Fujihara/Nakagawa）は原則触らない
- 変更したらコンパイル確認を行い、コミット対象ファイルを提示する

まずは現在の `git status` と `git log --oneline -5` を確認して、状況を報告してください。
```

---

## 11. メモの更新ルール

- 大きな実装をしたら、このファイルの「2. 実装済みキャラクター」と「9. 未対応」を更新する。
- 不具合を直したら「6. これまでに直した不具合」に1行追記する。
- 更新したメモもコミット対象に含める（`Docs/` は Unity の Assets 外なので .meta は付かない）。
