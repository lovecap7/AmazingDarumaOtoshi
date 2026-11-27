---
name: hero-cutin
description: アメイジングだるま落としのキャラの必殺技カットインを、イナズマイレブン（英雄たちのヴィクトリーロード）風に作る・作り直す。「〇〇の必殺技カットインを作って」「カットインをイナイレ風にして」「カットインをかっこよくして」と言われたときに使う。お手本は だるきんぐ の DaruKingHeroCutIn。
---

# 必殺技カットイン（イナイレ風）の作り方

お手本：`AmaizingDarumaOtoshi/Assets/Fujihara/Script/Game/Vfx/DaruKingHeroCutIn.cs`（だるきんぐ「エクスカリバー」）。
新しく作るときは、まずこのファイルを全部読み、同じ作りで `<キャラ>HeroCutIn.cs` を Fujihara/Script/Game/Vfx に作る。

## 最低限の要件（ユーザーが決めたもの。必ず満たす）
1. 今のカットインと同じように、ゲームが一時停止してからカットインに入る（`Nakahira.TimeStop.Begin(this, 0f)` / `End(this)`）
2. アニメのイナズマイレブンのように、効果音と一緒に技名が1文字ずつ描画される
3. カットイン中、そのキャラ＋その必殺技に合ったアニメーション・モーションをする
4. 背景も技の雰囲気に合わせて変わる
5. とにかくかっこよく

## 参考動画から分かった「型」（ヴィクトリーロードの必殺技集を分析したもの）
- 技名は**最初から最後まで画面下に出しっぱなし**。太い筆文字・少し斜め・1文字ずつ大きさ／角度をずらす・二重の縁取り（色＋白＋焦げ茶）。技名の色で属性が分かる（火＝橙、水・風＝青、林＝緑、闇＝紫）
- **集中線がほぼ全カットに入る**
- 0.5〜1秒ごとにカメラ（あおり・俯瞰・正面アップ）と背景（試合 ↔ 技専用の抽象背景）が切り替わる
- 背後に**巨大なもの**（化身・巨大な手・動物・神様）が半透明／光で出る
- 発動前に顔のアップや決めポーズ。最後は衝撃・白飛びで試合へ戻る
- だるま落としでは試合のテンポのため **約3〜3.5秒** に詰める

## 作り方（お手本の構成）
### つなぎ方（他人のファイルは「差し替え口を足すだけ」）
- カットインは Yurube さんの `Yurube/Script/<キャラ>CutIn.cs`。中身は変えず、`DaruKingCutIn` と同じく
  `public static System.Func<…, bool> Override;` と `public static System.Func<bool> OverridePlaying;` を足し、
  `Show` の先頭で `if (Override != null && Override(...)) return;`、`IsPlaying` に `|| (OverridePlaying != null && OverridePlaying())` を足す。
  目印コメント `// 差し替え用(Fujihara)` を付け、作業の最後に変えたファイル名をユーザーに報告する（CLAUDE.md の決まり）
- 自分のクラスで `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` で Override を登録。`MatchVfxSettings` のフラグ（例 `heroCutIn`）がオフなら false を返して元のカットインに戻す
- `<キャラ>Special` は `IsPlaying` が false になるまで次へ進まないので、ここで長さを返せば試合の流れは壊れない
- Show の引数が GameObject（頭）のキャラは `GetComponentInParent<DarumaCharacter>()`、DarumaCharacter のキャラはそのまま使う。
  カットインの呼び出し元は `Grep "CutIn.Show("` で調べる。`SpecialCutIn`（汎用）を使う技は、どの技か確認してから差し替え口を考える

### 舞台（3D）
- 遠く（20000,20000,20000）に舞台を作り、専用カメラ（cullingMask = layer 30）で RenderTexture(1920x1080) に描く
- カメラは `renderPostProcessing = true`・`volumeLayerMask = 1 << 30` にして、**舞台専用の Volume（Bloom・Vignette）だけ**を効かせる（試合の Volume／ExcaliburVfx の暗くする色調整が混ざらないように）
- キャラは試合中の `Character/Visual` を複製（頭・マント・鉢巻・武器）。手順：
  1. **止めた入れ物（SetActive(false) の親）の中で Instantiate** し、`MonoBehaviour` を DestroyImmediate してから舞台へ移す（そのまま複製すると CapeSway などが Awake で例外を出す）
  2. 元の Renderer の MaterialPropertyBlock を写す（頭は試合で白の PropertyBlock を付けている）。Renderer は全部 enabled（無敵の点滅で消えていることがある）
  3. **頭の高さを積み木なしの位置（`Head.localPosition = (0,0.45,0)`）にそろえる**（積んだ数で頭が上がっている）。積み木は複製しない
  4. 180°回して顔（+Z）をカメラ（-Z 側）へ向ける
- 武器は `HammerPivot` を回して動かす（+Z が武器の向き、だるまから見て +X が右）。向きは `Quaternion.LookRotation(向き, up)` で決めると分かりやすい
- 巨大なもの：同じ Visual をもう1つ複製し、全マテリアルを `VfxKit.AddMat(Texture2D.whiteTexture, 0.7f)`（光る半透明）にして 5〜6 倍で背後に置く。明るくしすぎると白い塊になるので、背景は暗めの色にする
- 背景：カメラの子に大きな板を2枚（縦グラデーション＋回る光線）。カットごとに色を変える
- 光の粒・風・火花は `VfxKit.CreateSystem(..., unscaled: true)`（時間が止まっていても動く）。layer を 30 にする

### 画面（2D、OnGUI・GUI.depth = -2000）
- RenderTexture を全画面に（揺れはここでずらす）→ 集中線（`VfxKit.SpeedLines` を回して拡大）→ 上下の黒帯 → 左上の名札 → 技名 → 斬撃などの閃光 → カット切替・終わりの白フラッシュ
- 技名：フォントは `MatchVfxSettings.cutInFont`（Fujihara/Font/YujiSyuku-Regular）。1文字ずつ `ScaleAroundPivot` で 2.6 倍 → 0.92 → 1 と叩きつける。縁取りは 16 方向に Label をずらして描く（外 16px 焦げ茶・内 8px 白）。グラデーションは GUI.BeginGroup で上側だけ切り抜いて別の色で重ねる
- 1文字出るごとに効果音（太鼓の「ドンッ」、少しずつ高く）＋小さく揺らす。そろったら「ジャーン」
- 効果音は MatchVfxSettings に AudioClip の欄を作り、空ならプログラムで作った仮の音（AudioClip.Create）。`AudioSource.ignoreListenerPause = true`、音量は `GameSettings.SeVolume`

### タイムライン（お手本：合計 3.5 秒）
| 時間 | カット | 動き | 背景 |
|---|---|---|---|
| 0〜0.55 | 構え（下からあおり、キャラを追う） | 縮んで跳ねて構える | 紺＋青い光線 |
| 0.55〜1.75 | 技の力（周りを回るカメラ） | 武器を掲げる・力がたまって震える・粒が集まる | 技の色の光線 |
| 1.75〜2.7 | 見せ場（真下から見上げる） | 巨大なもの・光の柱 | 暗めの空＋光 |
| 2.7〜3.5 | 決め（正面） | 振り下ろす・閃光・衝撃波 → 白く飛んで試合へ | 斬った瞬間に白く光る |
技名は 0.62 秒から 0.1 秒ごとに1文字。カットごとにキャラの技に合わせて中身を変える（例：岩なら地面が割れる、蛇なら巨大な蛇がうねる）。
**攻撃の向きと画面の閃光の向きをそろえる**（だるきんぐは正面の縦斬りにした。斜めだとユーザーに直された）。

## 時間の扱い（重要）
- `Time.unscaledTime - 開始` で数えると、**最初の1フレームの準備（シェーダーの読み込み）で1秒以上飛ぶ**。
  時間は `m_age += Mathf.Min(Time.unscaledDeltaTime, 0.05f)` のように1フレームずつ足して数える
- 撮影確認用に `public static float DebugFreezeAt = -1f;`（0 以上ならその時刻で止める）を用意しておく

## 確認のしかた（Unity MCP）
1. コンパイル（refresh_unity）→ `read_console` でエラーなし。**再生中にコンパイルしない**（止めてから）
2. 試合を用意：`MatchSetup.Set(entries, roster.stages[0])`（**コンパイルのたびに static が消えるので毎回設定し直す**）、
   `EditorSceneManager.playModeStartScene = AllStages.unity`、`Application.runInBackground = true` で再生
3. 見た目：`DebugFreezeAt` を 0.3 / 1.3 / 2.3 / 2.8 / 2.9 / 3.1 などにして `<キャラ>CutIn.Show(...)` を直接呼び、`manage_camera screenshot`
4. 本番の流れ：ゲージを満タン→`TryActivate()` で発動し、`IsPlaying` の長さ（≒3.5 秒）・`Time.timeScale == 0`・終わったあと必殺技が続くことを確かめる。2回続けて発動してもエラーが出ないこと
5. **後片付け（必ず）**：再生を止める、`playModeStartScene = null`、`Application.runInBackground = false`、`PlayerSettings.runInBackground = false`、`Time.timeScale = 1`、`MatchSetup.Rules.stocks` を戻す、`DebugFreezeAt = -1`、`Assets/Screenshots` を消す。
   `ProjectSettings/*`（runInBackground など）が変わっていたら戻す

## 報告
- 要件 1〜5 それぞれをどう満たしたか、タイムライン、調整できる設定、変えたファイル（他人のファイルは別に書く）、`.meta` も一緒にコミットすること
- 効果音は私（Claude）には聞けないので、仮の音であることと「聞いてみて違和感があれば教えて」を書く
