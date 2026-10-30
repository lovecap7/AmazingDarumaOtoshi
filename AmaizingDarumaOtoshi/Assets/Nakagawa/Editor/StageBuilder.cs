using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // 対戦ステージ(本編 Stage01〜 / デバッグ DebugStage01〜 / 追加ギミックの確認 GimmickDebugStage01〜)を、ステージをまとめたシーン(Assets/Nakagawa/Stages/AllStages.unity)の中に生成する
    // 各ステージはこのファイルの定義(床・壁・ギミック・積み木の生成エリア・出現位置・説明)から作るので、数値を直して作り直せる
    // 床はすべて同じ高さ(段差なし)。床の穴・壁のない縁は奈落(落ちると脱落)
    // 開いているシーンには触れない(別のシーンを追加で開いて組み立て、保存して閉じる)
    public static class StageBuilder
    {
        const string kStageDir = "Assets/Nakagawa/Stages";
        const string kDocPath = kStageDir + "/StageList.md";
        const string kCombinedPath = kStageDir + "/AllStages.unity";
        const string kPrefabDir = "Assets/Nakagawa/Prefabs";
        const string kMaterialDir = "Assets/Nakagawa/Materials";
        const string kRandomPatternPath = "Assets/Nakagawa/SpawnPatterns/SpawnPattern_Random.asset";
        const string kEqualPatternPath = "Assets/Nakagawa/SpawnPatterns/SpawnPattern_Equal.asset";
        const string kBounceImpactPath = "Assets/Nakagawa/TumikiImpacts/TumikiImpact_Bounce.asset";
        const string kSeeThroughWallMaterial = kMaterialDir + "/WallSeeThrough.mat";
        const string kDurabilityImpactPath = "Assets/Nakagawa/TumikiImpacts/TumikiImpact_Durability.asset";
        const string kCrackMaterial = kMaterialDir + "/TumikiCrack.mat";
        const string kDamageMaterial = kMaterialDir + "/TumikiDamage.mat";
        const string kParticleMaterial = kMaterialDir + "/GimmickParticle.mat";

        const string kNakahira = "Assets/Nakahira/Player";
        const string kCharacterPrefab = kNakahira + "/Prefabs/DarumaCharacter.prefab";
        const string kGhostPrefab = kNakahira + "/Prefabs/DarumaGhost.prefab";
        const string kPlayerPrefab = kNakahira + "/Prefabs/DarumaPlayer.prefab";
        const string kTumikiPrefab = kNakahira + "/Prefabs/Tumiki.prefab";
        const string kStageMaterial = kNakahira + "/Materials/Stage.mat";
        const string kWallMaterial = kNakahira + "/Materials/Wall.mat";

        const float kWallHeight = 1.5f;
        // 1ステージの積み木の生成エリアの最低数と、エリアのうち積み木を置ける場所の割合の下限(検証用)
        const int kMinSpawnAreas = 6;
        const float kMinSpawnCapacity = 0.2f;
        const float kBumperBaseRadius = 0.8f;   // Bumper.prefab の円盤の半径(スケール1のとき)

        // ステージは1つずつのシーンを作らず、すべてまとめたシーン(AllStages)の中に組み立てる
        // (同じ id のステージは入れ替え、他のステージ・共有の物はそのまま残す)

        [MenuItem("Nakagawa/Stages/Build All Stages Into AllStages")]
        public static void BuildAll()
        {
            BuildIntoCombined(def => true);
        }

        // 本編のステージ(Stage01〜。背景のあるステージ)だけを作り直す。デバッグステージはそのまま
        [MenuItem("Nakagawa/Stages/Build Main Stages Into AllStages")]
        public static void BuildMainStages()
        {
            BuildIntoCombined(def => !IsDebugStage(def.Id));
        }

        // 追加ギミックを試すステージ(GimmickDebugStage01〜)だけを作り直す。他のステージはそのまま
        [MenuItem("Nakagawa/Stages/Build Gimmick Debug Stages Into AllStages")]
        public static void BuildGimmickDebugStages()
        {
            BuildIntoCombined(def => def.Id.StartsWith(kGimmickDebugPrefix));
        }

        // 追加ギミックと元からあるギミックを組み合わせたデバッグステージ(DebugStage21〜30)だけを作り直す。他のステージはそのまま
        [MenuItem("Nakagawa/Stages/Build Debug Stages 21-30 Into AllStages")]
        public static void BuildDebugStages21To30()
        {
            BuildIntoCombined(def => InNumberRange(def.Id, kDebugPrefix, 21, 30));
        }

        // id が「接頭辞 + 通し番号」で、番号が from〜to か
        static bool InNumberRange(string id, string prefix, int from, int to)
        {
            if (!id.StartsWith(prefix) || !int.TryParse(id.Substring(prefix.Length), out int n)) return false;
            return n >= from && n <= to;
        }

        // デバッグ用のステージか(DebugStage01〜 / GimmickDebugStage01〜)。並び順・作り直しの対象の判定に使う
        public static bool IsDebugStage(string id) => id.Contains(kDebugPrefix);

        // 条件に合うステージを一時的なシーンに組み立て、まとめたシーンへ移す(一時的なシーンは保存しない)
        static void BuildIntoCombined(Predicate<StageDef> filter)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (Load<GameObject>($"{kPrefabDir}/TumikiSpawnArea.prefab") == null || Load<TumikiSpawnPattern>(kRandomPatternPath) == null)
            {
                GimmickTestFieldBuilder.BuildSpawnAssets();
            }
            if (!AdditionalGimmickBuilder.PrefabsExist()) AdditionalGimmickBuilder.BuildAll();
            EnsureFolder(kStageDir);
            SetupTumikiPrefab();

            var defs = Definitions();
            var targets = defs.FindAll(filter);
            var report = new StringBuilder();
            StageCombiner.AddOrUpdateStages(targets.ConvertAll(d => d.Id), id =>
            {
                var def = targets.Find(d => d.Id == id);
                Scene scene = BuildStageScene(def, out string line);
                report.AppendLine(line);
                return scene;
            });

            WriteDocument(defs);
            AssetDatabase.Refresh();
            Debug.Log($"[StageBuilder] {targets.Count}ステージを {kCombinedPath} に作りました\n" + report);
        }

        //========================================
        // ステージの定義
        //========================================

        // ステージの id の接頭辞(デバッグステージ / 本編)
        public const string kDebugPrefix = "DebugStage";
        public const string kStagePrefix = "Stage";
        // 追加ギミックを試すステージ(名前に kDebugPrefix を含むので、デバッグステージとして扱われる)
        public const string kGimmickDebugPrefix = "Gimmick" + kDebugPrefix;

        // 背景の id(BackdropBuilder の定義)
        const string kChanomaBackdrop = "Chanoma";
        const string kKitchenBackdrop = "Kitchen";
        const string kDeskBackdrop = "Desk";
        const string kEngawaBackdrop = "Engawa";
        const string kBathBackdrop = "Bath";
        const string kClubroomBackdrop = "Clubroom";
        const string kGroundBackdrop = "Ground";
        const string kDojoBackdrop = "Dojo";
        const string kStadiumBackdrop = "Stadium";
        const string kSlopeBackdrop = "Slope";
        const string kCruiseBackdrop = "Cruise";
        const string kRuinsBackdrop = "Ruins";
        const string kSpaceBackdrop = "Space";

        class StageDef
        {
            public string Id, Title, Concept, Flow;
            public int MaxBlocks = 24;
            public Action<Ctx> Build;
            public string Layout; // 生成後に埋める
            // 背景の id(BackdropBuilder の定義)。なければ背景なし
            public string Backdrop;
        }

        static List<StageDef> Definitions()
        {
            var list = new List<StageDef>();
            // id は「接頭辞 + シリーズ内の通し番号」(DebugStage01, Stage01 ...)。BeginSeries で接頭辞と番号を切り替える
            string prefix = "Stage";
            int number = 0;
            void BeginSeries(string seriesPrefix)
            {
                prefix = seriesPrefix;
                number = 0;
            }
            StageDef Add(string title, int maxBlocks, string concept, string flow, Action<Ctx> build)
            {
                var def = new StageDef { Id = $"{prefix}{++number:00}", Title = title, MaxBlocks = maxBlocks, Concept = concept, Flow = flow, Build = build };
                list.Add(def);
                return def;
            }

            //----------------------------------------
            // デバッグステージ(DebugStage01〜20)。ギミックの組み合わせを試すための、背景のないステージ
            //----------------------------------------
            BeginSeries(kDebugPrefix);

            Add("基本の闘技場", 24,
                "ギミックなしの正方形の闘技場。全ステージの基準となる、操作と駆け引きの基本だけで戦う舞台。",
                "中央の補給所に積み木が湧くたびに全員が中央へ集まり、拾い合いと打ち返しの乱戦になる。四方の壁で弾が跳ね返るので、中央で戦いながら壁からの跳弾にも気を配る展開。積み木を多く抱えた人ほど遅くなるため、序盤に積みすぎた人が狙われやすい。",
                c =>
                {
                    c.Arena(0, 0, 24, 24, true, true, true, true);
                    c.AreaCircle(0, 0, 4);
                    // 壁際(壁にかぶせて置く。床の上の部分にだけ湧く)
                    c.AreaRect(0, 10.5f, 8, 4);
                    c.AreaRect(0, -10.5f, 8, 4);
                    c.AreaRect(10.5f, 0, 4, 8);
                    c.AreaRect(-10.5f, 0, 4, 8);
                    c.AreaCircle(5, -5, 1.5f);
                    c.AreaCircle(-5, 5, 1.5f);
                    c.Spawns(new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8));
                });

            Add("四隅の補給所", 28,
                "四隅に補給所がある闘技場。出現位置は各辺の中央で、どの補給所にも同じ距離。",
                "序盤は最寄りの補給所に分かれて積み木を集める静かな立ち上がり。補給所の中に立っているとそのエリアには湧かないので、居座りは損になり、自然と隣の補給所へ遠征する流れが生まれる。遠征中の背中を狙う撃ち合いが中盤の山場。",
                c =>
                {
                    c.Arena(0, 0, 26, 26, true, true, true, true);
                    // 四隅の補給所は壁の角にかぶせる
                    c.AreaRect(-11, -11, 5, 5);
                    c.AreaRect(11, -11, 5, 5);
                    c.AreaRect(-11, 11, 5, 5);
                    c.AreaRect(11, 11, 5, 5);
                    c.AreaCircle(0, 0, 3);
                    c.AreaCircle(5.5f, -5.5f, 1.5f);
                    c.AreaCircle(-5.5f, 5.5f, 1.5f);
                    c.Spawns(new Vector2(0, -10), new Vector2(0, 10), new Vector2(-10, 0), new Vector2(10, 0));
                });

            Add("奈落の舞台", 20,
                "壁のない小さな正方形の舞台。縁から落ちれば即脱落。",
                "打ち返された積み木に押し出されるより、避けた拍子に落ちる事故が怖いステージ。全員が中央に寄りたがるので、中央の補給所を巡る近距離戦になる。弾も場外へ消えていくので積み木は少なめで推移し、一つひとつの積み木の価値が高い。",
                c =>
                {
                    c.Arena(0, 0, 22, 22, false, false, false, false);
                    c.AreaCircle(0, 0, 4);
                    // 縁(崖の外へはみ出して置く。床の上の部分にだけ湧く)
                    c.AreaRect(0, 10.5f, 8, 3);
                    c.AreaRect(0, -10.5f, 8, 3);
                    c.AreaRect(10.5f, 0, 3, 8);
                    c.AreaRect(-10.5f, 0, 3, 8);
                    c.AreaCircle(4.5f, -4.5f, 1.2f);
                    c.AreaCircle(-4.5f, 4.5f, 1.2f);
                    c.Spawns(new Vector2(-7, -7), new Vector2(7, -7), new Vector2(-7, 7), new Vector2(7, 7));
                });

            Add("ピンボールホール", 30,
                "中央にバンパーを格子状に並べた縦長のホール。補給所は南北の端。",
                "中央を通る弾はバンパーで何度もはじかれ、予測不能な方向へ飛ぶ。補給所は両端にあるので、積み木を取りに行くには中央のバンパー地帯を抜けるか外側を回るかの選択になる。バンパーを盾にして撃ち合う陣地戦と、はじかれた流れ弾による事故が見どころ。",
                c =>
                {
                    c.Arena(0, 0, 26, 30, true, true, true, true);
                    foreach (var p in new[] { new Vector2(-5, -4), new Vector2(0, -4), new Vector2(5, -4), new Vector2(-2.5f, 0), new Vector2(2.5f, 0), new Vector2(-5, 4), new Vector2(0, 4), new Vector2(5, 4) })
                    {
                        c.Bumper(p.x, p.y, 1.5f);
                    }
                    c.AreaRect(0, -13.5f, 12, 4);
                    c.AreaRect(0, 13.5f, 12, 4);
                    c.AreaRect(-12.2f, 0, 3, 6);
                    c.AreaRect(12.2f, 0, 3, 6);
                    // バンパーにかぶせて置く(バンパーと重ならない所にだけ湧く)
                    c.AreaCircle(0, 0, 2.5f);
                    c.AreaCircle(0, 6.2f, 2);
                    c.AreaCircle(0, -6.2f, 2);
                    c.Spawns(new Vector2(-10, -7), new Vector2(10, -7), new Vector2(-10, 7), new Vector2(10, 7));
                });

            Add("回る中心", 24,
                "中央に回転台を埋め込んだ闘技場。補給所は回転台の上。",
                "積み木は回転台の上に湧き、回りながら位置を変えるので、拾いに行くタイミングが重要。回転台の上を通る弾は台の回転に合わせて曲がるため、台越しの射撃は狙いがずれる。台の外から曲がりを読んで当てる上級者が有利になる。",
                c =>
                {
                    c.Arena(0, 0, 26, 26, true, true, true, true);
                    c.Turntable(0, 0, 5, true, 40);
                    c.AreaCircle(0, 0, 4);
                    c.AreaCircle(6.5f, 0, 1.5f);
                    c.AreaCircle(-6.5f, 0, 1.5f);
                    c.AreaRect(0, 12, 8, 3);
                    c.AreaRect(0, -12, 8, 3);
                    c.AreaRect(12, 0, 3, 8);
                    c.AreaRect(-12, 0, 3, 8);
                    c.Spawns(new Vector2(-9, -9), new Vector2(9, -9), new Vector2(-9, 9), new Vector2(9, 9));
                });

            Add("風の回廊", 26,
                "東西に長い回廊。西の壁際を扇風機が往復し、東へ向かって風を送り続ける。",
                "風上(西)から撃った弾は加速して遠くまで届き、風下(東)から撃った弾は失速する。西側の補給所は風が強く積み木が東へ流れやすい。風上を奪い合う陣取りが中心で、扇風機が近づく瞬間に風下へ一気に押し込まれる場面が生まれる。",
                c =>
                {
                    c.Arena(0, 0, 36, 16, true, true, true, true);
                    c.Fan(new Vector2(-17, -5), new Vector2(-17, 5), Vector3.right, 32, 4, 12, 1.5f);
                    c.AreaRect(-6, 0, 4, 10);
                    c.AreaRect(8, 0, 4, 10);
                    // 扇風機の通り道にかぶせて置く
                    c.AreaRect(-16.5f, 0, 3, 6);
                    c.AreaRect(17.5f, 0, 3, 8);
                    c.AreaRect(1, 7.5f, 6, 3);
                    c.AreaRect(1, -7.5f, 6, 3);
                    c.Spawns(new Vector2(-13, -5), new Vector2(-13, 5), new Vector2(14, -5), new Vector2(14, 5));
                });

            Add("双子の渦", 28,
                "東西に逆回りの回転台を二つ並べた闘技場。補給所は各回転台の中心と、二つの間。",
                "西は時計回り、東は反時計回りなので、台の上で弾が曲がる向きが左右で逆になる。間の補給所は両方の台に挟まれ、取りに行くと両側から曲がる弾が飛んでくる激戦区。回転の向きを覚えて弾の曲がりを利用できるかが勝負を分ける。",
                c =>
                {
                    c.Arena(0, 0, 32, 22, true, true, true, true);
                    c.Turntable(-8, 0, 5, true, 60);
                    c.Turntable(8, 0, 5, false, 60);
                    c.AreaCircle(-8, 0, 2.5f);
                    c.AreaCircle(8, 0, 2.5f);
                    c.AreaRect(0, 0, 3, 8);
                    c.AreaRect(0, 10.5f, 6, 3);
                    c.AreaRect(0, -10.5f, 6, 3);
                    c.AreaRect(15.5f, 0, 3, 6);
                    c.AreaRect(-15.5f, 0, 3, 6);
                    c.Spawns(new Vector2(-13, -8), new Vector2(13, -8), new Vector2(-13, 8), new Vector2(13, 8));
                });

            Add("十字路", 24,
                "十字形の通路だけのステージ。通路の横は奈落、交差点にはバンパー。補給所は各通路の行き止まり。",
                "通路の幅が狭く、横から撃たれると避ける余地が少ない。交差点のバンパーで押し合いになると奈落へ落とされる危険がある。補給所は行き止まりにあるので、取りに行くと逃げ道のない袋小路で狙われる。取りに行く勇気と待ち伏せの読み合い。",
                c =>
                {
                    c.Floor(0, 0, 36, 8);
                    c.Floor(0, 0, 8, 36);
                    c.Wall(17.5f, 0, 1, 8);
                    c.Wall(-17.5f, 0, 1, 8);
                    c.Wall(0, 17.5f, 8, 1);
                    c.Wall(0, -17.5f, 8, 1);
                    c.Bumper(0, 0, 2);
                    c.AreaRect(14.5f, 0, 4, 6);
                    c.AreaRect(-14.5f, 0, 4, 6);
                    c.AreaRect(0, 14.5f, 6, 4);
                    c.AreaRect(0, -14.5f, 6, 4);
                    // 交差点のバンパーにかぶせる / 通路の角から奈落へはみ出す
                    c.AreaCircle(0, 0, 3.5f);
                    c.AreaRect(5.5f, 5.5f, 5, 5);
                    c.AreaRect(-5.5f, -5.5f, 5, 5);
                    c.Spawns(new Vector2(8, 0), new Vector2(-8, 0), new Vector2(0, 8), new Vector2(0, -8));
                });

            Add("群島", 20,
                "四つの島を細い橋でつないだステージ。各自が自分の島から始まり、積み木は橋の上に湧く。",
                "島の外周は壁で守られているが、橋の両側は奈落。積み木を手に入れるには橋に出る必要があり、橋の上で撃たれると避けた先が奈落になる。自分の島に籠って橋の上の相手を撃つ守りと、橋を渡って攻める攻めのバランスが問われる。",
                c =>
                {
                    c.Arena(-8, -8, 9, 9, false, true, false, true);
                    c.Arena(8, -8, 9, 9, false, true, true, false);
                    c.Arena(-8, 8, 9, 9, true, false, false, true);
                    c.Arena(8, 8, 9, 9, true, false, true, false);
                    c.Floor(0, 8, 7, 3);
                    c.Floor(0, -8, 7, 3);
                    c.Floor(8, 0, 3, 7);
                    c.Floor(-8, 0, 3, 7);
                    // 橋(両側の奈落へはみ出す)
                    c.AreaRect(0, 8, 5, 4);
                    c.AreaRect(0, -8, 5, 4);
                    c.AreaRect(8, 0, 4, 5);
                    c.AreaRect(-8, 0, 4, 5);
                    // 島の内側の角(奈落へはみ出す)
                    c.AreaRect(4.5f, 4.5f, 3, 3);
                    c.AreaRect(-4.5f, 4.5f, 3, 3);
                    c.AreaRect(4.5f, -4.5f, 3, 3);
                    c.AreaRect(-4.5f, -4.5f, 3, 3);
                    c.Spawns(new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8));
                });

            Add("バンパーの柵", 24,
                "中央の補給所をバンパーの輪が囲む闘技場。",
                "補給所に入るにはバンパーの隙間を抜ける必要があり、触れるとはじき出される。輪の外から隙間越しに撃つ、輪の中で拾った積み木を守る、という攻守がはっきり分かれる。輪に当たった弾は外へはじき返されるので、輪の周りは流れ弾が多い。",
                c =>
                {
                    c.Arena(0, 0, 28, 28, true, true, true, true);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI * 2.0f / 8;
                        c.Bumper(Mathf.Cos(a) * 6.5f, Mathf.Sin(a) * 6.5f, 1.3f);
                    }
                    c.AreaCircle(0, 0, 3.5f);
                    // 輪のバンパーにかぶせる
                    c.AreaCircle(0, 8.3f, 2);
                    c.AreaCircle(0, -8.3f, 2);
                    c.AreaCircle(8.3f, 0, 2);
                    c.AreaCircle(-8.3f, 0, 2);
                    c.AreaRect(0, 13, 8, 3);
                    c.AreaRect(0, -13, 8, 3);
                    c.Spawns(new Vector2(-10, -10), new Vector2(10, -10), new Vector2(-10, 10), new Vector2(10, 10));
                });

            Add("吹きさらしの崖", 22,
                "北側だけ壁があり、南側は崖。北の壁際を扇風機が往復して崖へ向かう風を送る。崖際にはバンパー。",
                "風は常に崖の方向へ吹くので、南側に立つほど落下の危険が高い。補給所は風上の北の壁際にあり、全員が北へ集まって押し合う。崖際のバンパーにはじかれると風に乗って奈落へ一直線。風下へ追い込む立ち回りが決め手。",
                c =>
                {
                    c.Arena(0, 0, 26, 20, true, false, false, false);
                    c.Fan(new Vector2(-10, 9), new Vector2(10, 9), Vector3.back, 18, 4, 14, 2);
                    c.Bumper(-7, -6, 1.3f);
                    c.Bumper(0, -7, 1.3f);
                    c.Bumper(7, -6, 1.3f);
                    c.AreaRect(0, 5.5f, 18, 3);
                    c.AreaCircle(0, -1, 2.5f);
                    // 崖の外へはみ出す / 崖際のバンパーにかぶせる
                    c.AreaRect(0, -9.5f, 8, 3);
                    c.AreaRect(12, 6, 3, 6);
                    c.AreaRect(-12, 6, 3, 6);
                    c.AreaCircle(-7, -3.8f, 1.8f);
                    c.AreaCircle(7, -3.8f, 1.8f);
                    c.Spawns(new Vector2(-10, 2), new Vector2(10, 2), new Vector2(-10, -3), new Vector2(10, -3));
                });

            Add("回転寿司", 26,
                "床の大半を占める巨大な回転台。補給所は台の上に四つ。",
                "湧いた積み木はゆっくり回りながら流れてくるので、台の外で待つか、台に乗って取りに行くかを選べる。台の上にいると弾が曲がり狙いにくい一方、台の外の相手からも当てにくい。流れてくる積み木の取り合いが絶えず起こる。",
                c =>
                {
                    c.Arena(0, 0, 26, 26, true, true, true, true);
                    c.Turntable(0, 0, 9, true, 30);
                    c.AreaCircle(6, 0, 2);
                    c.AreaCircle(-6, 0, 2);
                    c.AreaCircle(0, 6, 2);
                    c.AreaCircle(0, -6, 2);
                    c.AreaRect(0, 12, 8, 3);
                    c.AreaRect(0, -12, 8, 3);
                    c.AreaRect(12, 0, 3, 8);
                    c.AreaRect(-12, 0, 3, 8);
                    c.Spawns(new Vector2(-10.5f, -10.5f), new Vector2(10.5f, -10.5f), new Vector2(-10.5f, 10.5f), new Vector2(10.5f, 10.5f));
                });

            Add("風の要塞", 26,
                "四隅の扇風機が壁沿いに風を送り、外周をぐるりと回る風の環ができた闘技場。",
                "外周に近づくと時計回りに流され、外周を通る弾も風に乗って周回するように曲がる。風の環の内側は無風で、中央の補給所を巡る戦いになる。外周に押し出された相手は流されて体勢を崩すので、内側から外周へ追い出す攻めが有効。",
                c =>
                {
                    c.Arena(0, 0, 28, 28, true, true, true, true);
                    c.Fan(new Vector2(-12, -12), new Vector2(-12, -12), Vector3.right, 24, 4, 10, 0);
                    c.Fan(new Vector2(12, -12), new Vector2(12, -12), Vector3.forward, 24, 4, 10, 0);
                    c.Fan(new Vector2(12, 12), new Vector2(12, 12), Vector3.left, 24, 4, 10, 0);
                    c.Fan(new Vector2(-12, 12), new Vector2(-12, 12), Vector3.back, 24, 4, 10, 0);
                    c.AreaCircle(0, 0, 4);
                    // 風の環の中(壁にかぶせる) / 隅の扇風機にかぶせる
                    c.AreaRect(0, 13, 6, 3);
                    c.AreaRect(0, -13, 6, 3);
                    c.AreaRect(13, 0, 3, 6);
                    c.AreaRect(-13, 0, 3, 6);
                    c.AreaCircle(12, 12, 2);
                    c.AreaCircle(-12, -12, 2);
                    c.Spawns(new Vector2(-6, -6), new Vector2(6, -6), new Vector2(-6, 6), new Vector2(6, 6));
                });

            Add("バンパー迷路", 28,
                "バンパーを格子状に敷き詰めた闘技場。補給所は東西の端。",
                "どこに撃ってもどこかのバンパーにはじかれ、弾の行方がほとんど読めない。狙って当てるより、数を撃って跳弾を当てる手数勝負になる。補給所の東西を行き来するには迷路を抜ける必要があり、移動中に跳弾を受ける事故が多発する。",
                c =>
                {
                    c.Arena(0, 0, 30, 24, true, true, true, true);
                    foreach (int x in new[] { -8, -4, 0, 4, 8 })
                    {
                        foreach (int z in new[] { -6, 0, 6 }) c.Bumper(x, z, 1.2f);
                    }
                    c.AreaRect(13.5f, 0, 4, 14);
                    c.AreaRect(-13.5f, 0, 4, 14);
                    c.AreaCircle(2, 0, 1.8f);
                    c.AreaRect(0, 11.5f, 6, 3);
                    c.AreaRect(0, -11.5f, 6, 3);
                    c.AreaCircle(4, 3, 1.5f);
                    c.AreaCircle(-4, -3, 1.5f);
                    c.Spawns(new Vector2(-6, -10), new Vector2(6, -10), new Vector2(-6, 10), new Vector2(6, 10));
                });

            Add("二本橋", 26,
                "東西の陣地を二本の橋でつないだステージ。橋の間の柱に立つ扇風機が、橋の上へ横風を送る。",
                "橋を渡ると横風で奈落側へ押されるので、渡るだけで危険。補給所は東西の陣地にあり、自陣で積み木を集めて対岸を撃つ撃ち合いが基本。横風で曲がる弾を橋の上の相手に当てる、風を利用した狙撃が決め手になる。",
                c =>
                {
                    c.Arena(-10, 0, 12, 16, true, true, false, true);
                    c.Arena(10, 0, 12, 16, true, true, true, false);
                    c.Floor(0, 5, 8, 3);
                    c.Floor(0, -5, 8, 3);
                    c.Floor(0, 0, 2, 2.4f);
                    c.Fan(new Vector2(0, 0.6f), new Vector2(0, 0.6f), Vector3.forward, 7, 8, 18, 0);
                    c.Fan(new Vector2(0, -0.6f), new Vector2(0, -0.6f), Vector3.back, 7, 8, 18, 0);
                    c.AreaRect(-10, 0, 6, 10);
                    c.AreaRect(10, 0, 6, 10);
                    // 橋(両側の奈落へはみ出す) / 中央の柱の扇風機にかぶせる / 壁にかぶせる
                    c.AreaRect(0, 5, 6, 4);
                    c.AreaRect(0, -5, 6, 4);
                    c.AreaRect(-4.5f, 0, 3, 4);
                    c.AreaRect(-10, 8, 6, 2.5f);
                    c.AreaRect(10, -8, 6, 2.5f);
                    c.Spawns(new Vector2(-14.5f, -6), new Vector2(-14.5f, 6), new Vector2(14.5f, -6), new Vector2(14.5f, 6));
                });

            Add("回転決闘場", 18,
                "壁のない小さな舞台の中央に回転台、四隅にバンパー。",
                "足元の回転台に乗ると流されて位置がずれ、四隅のバンパーにはじかれると場外へ放り出される。逃げ場のない狭い舞台で、回転台の流れを利用して相手をバンパーや縁へ運ぶ、押し出し合いの短期決戦になる。",
                c =>
                {
                    c.Arena(0, 0, 20, 20, false, false, false, false);
                    c.Turntable(0, 0, 7, false, 50);
                    c.Bumper(-8, -8, 1.3f);
                    c.Bumper(8, -8, 1.3f);
                    c.Bumper(-8, 8, 1.3f);
                    c.Bumper(8, 8, 1.3f);
                    c.AreaCircle(0, 0, 3);
                    // 縁(奈落へはみ出す) / 隅のバンパーにかぶせる
                    c.AreaRect(0, 9.5f, 6, 2);
                    c.AreaRect(0, -9.5f, 6, 2);
                    c.AreaRect(9.5f, 0, 2, 6);
                    c.AreaRect(-9.5f, 0, 2, 6);
                    c.AreaCircle(8.6f, 5.6f, 1.5f);
                    c.AreaCircle(-8.6f, -5.6f, 1.5f);
                    c.Spawns(new Vector2(-6, -6), new Vector2(6, -6), new Vector2(-6, 6), new Vector2(6, 6));
                });

            Add("挟み撃ちの風", 22,
                "東西に長い闘技場の両端から、中央へ向かって扇風機が風を送る。",
                "両端の風が全員を中央へ押し寄せるので、離れて戦うのが難しい。中央の補給所に人と積み木が集まり、ステージで一番の密集戦になる。風に逆らって端へ逃げると動きが鈍るため、中央で戦い抜く体力勝負になる。",
                c =>
                {
                    c.Arena(0, 0, 36, 14, true, true, true, true);
                    c.Fan(new Vector2(-17, 0), new Vector2(-17, 0), Vector3.right, 16, 14, 10, 0);
                    c.Fan(new Vector2(17, 0), new Vector2(17, 0), Vector3.left, 16, 14, 10, 0);
                    c.AreaCircle(0, 0, 3);
                    // 壁にかぶせる / 両端の扇風機にかぶせる
                    c.AreaRect(-8, 6.5f, 5, 3);
                    c.AreaRect(8, 6.5f, 5, 3);
                    c.AreaRect(-8, -6.5f, 5, 3);
                    c.AreaRect(8, -6.5f, 5, 3);
                    c.AreaCircle(-15.5f, 0, 1.8f);
                    c.AreaCircle(15.5f, 0, 1.8f);
                    c.Spawns(new Vector2(-14, -4), new Vector2(-14, 4), new Vector2(14, -4), new Vector2(14, 4));
                });

            Add("歯車工場", 28,
                "歯車のように噛み合う三つの回転台。両端が時計回り、中央が反時計回りで、接する所の速さが揃っている。",
                "積み木は台から台へ受け渡されながら8の字を描くように流れていく。台の境目をまたぐ弾は曲がる向きが途中で変わり、軌道が蛇行する。三つの補給所はそれぞれ台の中心にあり、流れを読んで先回りする判断力が試される。",
                c =>
                {
                    c.Arena(0, 0, 32, 24, true, true, true, true);
                    c.Turntable(-9, 0, 5, true, 48);
                    c.Turntable(0, 0, 4, false, 60);
                    c.Turntable(9, 0, 5, true, 48);
                    c.AreaCircle(-9, 0, 2.5f);
                    c.AreaCircle(0, 0, 2);
                    c.AreaCircle(9, 0, 2.5f);
                    c.AreaRect(0, 11, 8, 3);
                    c.AreaRect(0, -11, 8, 3);
                    c.AreaRect(15.5f, 0, 3, 6);
                    c.AreaRect(-15.5f, 0, 3, 6);
                    c.Spawns(new Vector2(-12, -9), new Vector2(12, -9), new Vector2(-12, 9), new Vector2(12, 9));
                });

            Add("外周回廊", 26,
                "中央に大穴が空いた口の字形の回廊。内側の縁は奈落、穴の四隅の近くにバンパー。",
                "回廊は一周できるので、追いかけっこと回り込みが起きやすい。内側の縁で弾を避けると穴へ落ちる危険があり、バンパーにはじかれる場所では特に注意が必要。補給所は各辺の中央にあり、角を曲がった先で鉢合わせる遭遇戦が多い。",
                c =>
                {
                    c.Floor(0, 11, 32, 10);
                    c.Floor(0, -11, 32, 10);
                    c.Floor(-11, 0, 10, 12);
                    c.Floor(11, 0, 10, 12);
                    c.Wall(0, 15.5f, 32, 1);
                    c.Wall(0, -15.5f, 32, 1);
                    c.Wall(15.5f, 0, 1, 30);
                    c.Wall(-15.5f, 0, 1, 30);
                    c.Bumper(-8.5f, -8.5f, 1.3f);
                    c.Bumper(8.5f, -8.5f, 1.3f);
                    c.Bumper(-8.5f, 8.5f, 1.3f);
                    c.Bumper(8.5f, 8.5f, 1.3f);
                    // 内側の縁(大穴へはみ出す) / 穴の四隅のバンパーにかぶせる
                    c.AreaRect(0, 7.5f, 8, 4);
                    c.AreaRect(0, -7.5f, 8, 4);
                    c.AreaRect(7.5f, 0, 4, 8);
                    c.AreaRect(-7.5f, 0, 4, 8);
                    c.AreaCircle(10, 10, 2);
                    c.AreaCircle(-10, 10, 2);
                    c.AreaCircle(10, -10, 2);
                    c.AreaCircle(-10, -10, 2);
                    c.Spawns(new Vector2(-12, -12), new Vector2(12, -12), new Vector2(-12, 12), new Vector2(12, 12));
                });

            Add("嵐の目", 26,
                "中央に高速の回転台、四隅の扇風機が中央へ向けて風を送る。補給所は各辺の中央の無風地帯。",
                "風が全員を中央の回転台へ引き寄せ、台の上では高速回転で弾も体も大きく流される。補給所は風の当たらない外側にあるので、外で積み木を集め、中央へ流されてきた相手を狙うのが定石。台の上での混戦に巻き込まれるかどうかが分かれ目。",
                c =>
                {
                    c.Arena(0, 0, 30, 30, true, true, true, true);
                    c.Turntable(0, 0, 6, true, 70);
                    c.Fan(new Vector2(-13, -13), new Vector2(-13, -13), new Vector3(1, 0, 1), 12, 4, 12, 0);
                    c.Fan(new Vector2(13, -13), new Vector2(13, -13), new Vector3(-1, 0, 1), 12, 4, 12, 0);
                    c.Fan(new Vector2(13, 13), new Vector2(13, 13), new Vector3(-1, 0, -1), 12, 4, 12, 0);
                    c.Fan(new Vector2(-13, 13), new Vector2(-13, 13), new Vector3(1, 0, -1), 12, 4, 12, 0);
                    c.AreaCircle(0, 11, 2);
                    c.AreaCircle(0, -11, 2);
                    c.AreaCircle(11, 0, 2);
                    c.AreaCircle(-11, 0, 2);
                    // 中央の回転台 / 隅の扇風機にかぶせる
                    c.AreaCircle(0, 0, 2.5f);
                    c.AreaCircle(11.5f, -11.5f, 2.5f);
                    c.AreaCircle(-11.5f, 11.5f, 2.5f);
                    c.Spawns(new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8));
                });

            //----------------------------------------
            // デバッグステージ(DebugStage21〜30)。追加ギミック(せり上がり塀・加速床/減速床・移動床・ワープゲート・
            // シューターレーン・回転砲台)と、元からあるギミックを組み合わせたステージ
            // 21〜28 には背景(部室・運動場・道場・スタジアム・ゲレンデ・客船のデッキ・遺跡・宇宙ステーション)を付ける
            //----------------------------------------

            Add("波打つ防波堤", 26,
                "南北の陣地を、東西に一列に並んだせり上がり塀が仕切る。塀は西から東へ波のように順番に立ち上がる。四隅寄りにバンパー。",
                "塀が下がっている所だけが通り道になり、その切れ目は西から東へ流れていく。塀が立っている間は弾が跳ね返るので、切れ目を狙って撃ち込むか、塀の上に乗って高台から撃つかの駆け引きになる。切れ目を追って走ると、バンパーにはじかれて塀の前に押し戻されることもある。",
                c =>
                {
                    c.Arena(0, 0, 28, 24, true, true, true, true);
                    for (int i = 0; i < 13; i++) c.RisingWall(-12 + i * 2, 0, new Vector2(2, 1.2f), 1.5f, i * 0.25f);
                    c.Bumper(-8, 6, 1.2f);
                    c.Bumper(8, 6, 1.2f);
                    c.Bumper(-8, -6, 1.2f);
                    c.Bumper(8, -6, 1.2f);
                    c.AreaCircle(0, 6, 2.5f);
                    c.AreaCircle(0, -6, 2.5f);
                    // 四隅(壁にかぶせる)
                    c.AreaRect(-12, 10.5f, 4, 3);
                    c.AreaRect(12, 10.5f, 4, 3);
                    c.AreaRect(-12, -10.5f, 4, 3);
                    c.AreaRect(12, -10.5f, 4, 3);
                    c.Spawns(new Vector2(-11, 4), new Vector2(11, -4), new Vector2(-4, -9), new Vector2(4, 9));
                }).Backdrop = kClubroomBackdrop;

            Add("加速サーキット", 26,
                "中央の回転台を囲む周回路。各辺の中央に加速床、四隅に減速床が置かれている。",
                "加速床で一気に速くなった体と弾は、隅の減速床でがくんと落ちる。速さの変わり目で相手との距離が急に縮まったり開いたりするので、周回の流れを読んで待ち伏せるのが強い。中央の回転台に飛び込んだ弾は曲がりながら飛び出し、思わぬ方向から飛んでくる。",
                c =>
                {
                    c.MaxGimmickTypes = 3;
                    c.Arena(0, 0, 30, 30, true, true, true, true);
                    c.Turntable(0, 0, 5, true, 45);
                    c.SpeedFloor(0, 10, new Vector2(3, 3), true);
                    c.SpeedFloor(10, 0, new Vector2(3, 3), true);
                    c.SpeedFloor(0, -10, new Vector2(3, 3), true);
                    c.SpeedFloor(-10, 0, new Vector2(3, 3), true);
                    c.SpeedFloor(10, 10, new Vector2(3, 3), false);
                    c.SpeedFloor(-10, 10, new Vector2(3, 3), false);
                    c.SpeedFloor(10, -10, new Vector2(3, 3), false);
                    c.SpeedFloor(-10, -10, new Vector2(3, 3), false);
                    c.AreaCircle(0, 0, 2.5f);
                    // 各辺の壁にかぶせる
                    c.AreaRect(0, 13, 6, 3);
                    c.AreaRect(0, -13, 6, 3);
                    c.AreaRect(13, 0, 3, 6);
                    c.AreaRect(-13, 0, 3, 6);
                    c.AreaCircle(7, 0, 1.5f);
                    c.Spawns(new Vector2(-6, 6), new Vector2(6, 6), new Vector2(-6, -6), new Vector2(6, -6));
                }).Backdrop = kGroundBackdrop;

            Add("渡し舟の大河", 24,
                "南北に流れる大河(奈落)で東西の岸が分かれ、二艘の木のパレットが岸の間を往復する。河の中央に小島。両岸の扇風機が河へ向けて風を送る。",
                "対岸へ渡る手段はパレットだけで、乗っている間は逃げ場がない。岸の扇風機は河へ向けて吹くので、岸の端で撃ち合うと風に押されて河へ落ちる。小島の補給所は危険だが積み木が湧きやすく、パレットの往復に合わせて乗り込むタイミングが勝負。",
                c =>
                {
                    c.Arena(-10, 0, 12, 24, true, true, false, true);
                    c.Arena(10, 0, 12, 24, true, true, true, false);
                    c.Floor(0, 0, 3, 3);
                    c.MovingFloor(new Vector2(2.9f, 2.9f), MovingFloorGimmick.LoopMode.PingPong, 2.0f, 1.2f, new Vector2(-2.5f, 7), new Vector2(2.5f, 7));
                    c.MovingFloor(new Vector2(2.9f, 2.9f), MovingFloorGimmick.LoopMode.PingPong, 2.0f, 1.2f, new Vector2(2.5f, -7), new Vector2(-2.5f, -7));
                    c.Fan(new Vector2(-15, 0), new Vector2(-15, 0), Vector3.right, 10, 4, 10, 0);
                    c.Fan(new Vector2(15, 0), new Vector2(15, 0), Vector3.left, 10, 4, 10, 0);
                    c.AreaRect(-10, 8, 6, 4);
                    c.AreaRect(-10, -8, 6, 4);
                    c.AreaRect(10, 8, 6, 4);
                    c.AreaRect(10, -8, 6, 4);
                    c.AreaCircle(0, 0, 1.2f);
                    // 扇風機の前
                    c.AreaCircle(-11, 0, 1.5f);
                    c.AreaCircle(11, 0, 1.5f);
                    c.Spawns(new Vector2(-13, 4), new Vector2(-13, -4), new Vector2(13, 4), new Vector2(13, -4));
                }).Backdrop = kDojoBackdrop;

            Add("ワープ群島", 24,
                "奈落に浮かぶ四つの島。橋はなく、島の間はワープゲートでしか行き来できない。各島の中央にバンパー。",
                "内側の角のゲート(グループ0)はどの島へ飛ぶかランダム、外側の角のゲートはプレイヤーだけを対角の島へ送る。積み木は内側のゲートでしか運べないので、島ごとの積み木の偏りが生まれる。ゲートから出た直後を狙い撃ちされやすく、出口の光を見て待ち伏せる・逃げる駆け引きが中心になる。",
                c =>
                {
                    c.Arena(8, 8, 10, 10, true, false, true, false);
                    c.Arena(-8, 8, 10, 10, true, false, false, true);
                    c.Arena(8, -8, 10, 10, false, true, true, false);
                    c.Arena(-8, -8, 10, 10, false, true, false, true);
                    foreach (var p in new[] { new Vector2(8, 8), new Vector2(-8, 8), new Vector2(8, -8), new Vector2(-8, -8) }) c.Bumper(p.x, p.y, 1.2f);
                    // 内側の角: どこへ飛ぶか分からないゲート(どちらも・ランダム)
                    c.WarpGate(5.5f, 5.5f, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(-5.5f, 5.5f, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(5.5f, -5.5f, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(-5.5f, -5.5f, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    // 外側の角: 対角の島へ(プレイヤーのみ)
                    c.WarpGate(10.5f, 10.5f, 0, 1, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(-10.5f, -10.5f, 0, 1, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(-10.5f, 10.5f, 0, 2, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(10.5f, -10.5f, 0, 2, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    c.AreaCircle(11, 6, 1.5f);
                    c.AreaCircle(-11, 6, 1.5f);
                    c.AreaCircle(11, -6, 1.5f);
                    c.AreaCircle(-11, -6, 1.5f);
                    c.AreaCircle(6, 11, 1.5f);
                    c.AreaCircle(-6, -11, 1.5f);
                    c.Spawns(new Vector2(5, 9), new Vector2(-9, 5), new Vector2(9, -5), new Vector2(-5, -9));
                }).Backdrop = kStadiumBackdrop;

            Add("砲台の砦", 26,
                "東西に向かい合う二基の回転砲台が首を振りながら積み木を撃ち出す。中央の南北の線には、波のように立ち上がるせり上がり塀。",
                "砲台は持ち主なしの弾を扇状にばらまくので、誰の手柄にもならない流れ弾が常に飛び交う。中央の塀が立っている間は反対側の砲台の弾が防がれ、下がった所からは弾が抜けてくる。塀の動きを見て、砲台の弾から身を隠しつつ積み木を集める立ち回りが求められる。",
                c =>
                {
                    c.Arena(0, 0, 30, 26, true, true, true, true);
                    c.Cannon(-8, 0, 90, TumikiCannon.RotationMode.Swing, 1, 3);
                    c.Cannon(8, 0, 270, TumikiCannon.RotationMode.Swing, 1, 3);
                    for (int i = 0; i < 10; i++) c.RisingWall(0, -9 + i * 2, new Vector2(1.2f, 2), 1.5f, i * 0.3f);
                    c.AreaCircle(-8, 8, 2);
                    c.AreaCircle(8, 8, 2);
                    c.AreaCircle(-8, -8, 2);
                    c.AreaCircle(8, -8, 2);
                    // 東西の壁にかぶせる
                    c.AreaRect(-13, 0, 3, 6);
                    c.AreaRect(13, 0, 3, 6);
                    c.Spawns(new Vector2(-4, 5), new Vector2(4, -5), new Vector2(-4, -5), new Vector2(4, 5));
                }).Backdrop = kSlopeBackdrop;

            Add("シューター回廊", 26,
                "四隅のシューターレーンが、中央の回転台へ向けて斜めに積み木を撃ち出す。",
                "レーンに積み木を運べば必ず中央へ向けて飛ぶが、回転台の上を通ると弾道が曲がり、どこへ抜けるかは回転の向き次第。落ちていた積み木は持ち主なしで飛ぶので、自分で殴った積み木をレーンへ打ち込むと手柄を保ったまま撃ち出せる。レーンの前に立つと流れ弾の通り道になるので注意。",
                c =>
                {
                    c.Arena(0, 0, 32, 24, true, true, true, true);
                    c.Turntable(0, 0, 4, true, 50);
                    c.ShooterLane(-12, -8, 45, new Vector2(2.5f, 3));
                    c.ShooterLane(12, 8, 225, new Vector2(2.5f, 3));
                    c.ShooterLane(12, -8, 315, new Vector2(2.5f, 3));
                    c.ShooterLane(-12, 8, 135, new Vector2(2.5f, 3));
                    c.AreaCircle(0, 0, 2);
                    // 南北・東西の壁にかぶせる
                    c.AreaRect(0, 10, 8, 3);
                    c.AreaRect(0, -10, 8, 3);
                    c.AreaRect(14.5f, 0, 3, 6);
                    c.AreaRect(-14.5f, 0, 3, 6);
                    c.AreaCircle(7, 0, 1.5f);
                    c.Spawns(new Vector2(-7, 4), new Vector2(7, -4), new Vector2(-7, -4), new Vector2(7, 4));
                }).Backdrop = kCruiseBackdrop;

            Add("氷と泥の庭", 26,
                "加速床と減速床が市松模様に並ぶ庭。中央に大きなバンパー、四隅に小さなバンパー。",
                "加速床(オレンジ)を踏むと体も弾も一気に速くなり、減速床(青緑)では泥にはまったように遅くなる。止まっている積み木も加速床の上では向いている方向へ走り出すので、床の色を見て積み木の行き先を読む必要がある。中央のバンパーにはじかれて加速床へ飛び込むと、そのまま壁まで滑っていく。",
                c =>
                {
                    c.MaxGimmickTypes = 3;
                    c.Arena(0, 0, 28, 28, true, true, true, true);
                    c.SpeedFloor(6, 6, new Vector2(4, 4), true);
                    c.SpeedFloor(-6, 6, new Vector2(4, 4), true);
                    c.SpeedFloor(6, -6, new Vector2(4, 4), true);
                    c.SpeedFloor(-6, -6, new Vector2(4, 4), true);
                    c.SpeedFloor(0, 8, new Vector2(3, 3), false);
                    c.SpeedFloor(0, -8, new Vector2(3, 3), false);
                    c.SpeedFloor(8, 0, new Vector2(3, 3), false);
                    c.SpeedFloor(-8, 0, new Vector2(3, 3), false);
                    c.Bumper(0, 0, 1.6f);
                    c.Bumper(11, 11, 1.2f);
                    c.Bumper(-11, 11, 1.2f);
                    c.Bumper(11, -11, 1.2f);
                    c.Bumper(-11, -11, 1.2f);
                    c.AreaCircle(0, 4, 1.5f);
                    c.AreaCircle(0, -4, 1.5f);
                    c.AreaCircle(4, 0, 1.5f);
                    c.AreaCircle(-4, 0, 1.5f);
                    // 南北の壁にかぶせる
                    c.AreaRect(0, 12.5f, 6, 2);
                    c.AreaRect(0, -12.5f, 6, 2);
                    c.Spawns(new Vector2(-11, 4), new Vector2(11, -4), new Vector2(4, 11), new Vector2(-4, -11));
                }).Backdrop = kRuinsBackdrop;

            Add("流れる足場の工場", 26,
                "中央に四角い大穴が空いた工場。穴の中を二枚のパレットが同じ向きに周回し、外周の四隅のシューターレーンが壁沿いに時計回りに積み木を送る。",
                "外周の積み木はレーンからレーンへと時計回りに運ばれ続け、レーンの先に立つ相手を襲う。穴を突っ切るにはパレットに乗るしかなく、周回のタイミングを外すと奈落へ落ちる。パレットの上から外周の相手を撃つか、外周を回って積み木を集めるかの選択になる。",
                c =>
                {
                    // 遊べる範囲 32×32m。中央 12×12m の穴を残して床を並べ、四方に壁
                    c.Floor(0, 11.5f, 34, 11);
                    c.Floor(0, -11.5f, 34, 11);
                    c.Floor(-11.5f, 0, 11, 12);
                    c.Floor(11.5f, 0, 11, 12);
                    c.Wall(0, 16.5f, 34, 1);
                    c.Wall(0, -16.5f, 34, 1);
                    c.Wall(16.5f, 0, 1, 32);
                    c.Wall(-16.5f, 0, 1, 32);
                    // 同じ経路・同じ速さで、反対側から周回を始める(ぶつからない)
                    c.MovingFloor(new Vector2(2.9f, 2.9f), MovingFloorGimmick.LoopMode.Loop, 2.5f, 1.0f,
                        new Vector2(-4.5f, -4.5f), new Vector2(4.5f, -4.5f), new Vector2(4.5f, 4.5f), new Vector2(-4.5f, 4.5f));
                    c.MovingFloor(new Vector2(2.9f, 2.9f), MovingFloorGimmick.LoopMode.Loop, 2.5f, 1.0f,
                        new Vector2(4.5f, 4.5f), new Vector2(-4.5f, 4.5f), new Vector2(-4.5f, -4.5f), new Vector2(4.5f, -4.5f));
                    c.ShooterLane(-12, -12, 0, new Vector2(2.5f, 3));
                    c.ShooterLane(-12, 12, 90, new Vector2(2.5f, 3));
                    c.ShooterLane(12, 12, 180, new Vector2(2.5f, 3));
                    c.ShooterLane(12, -12, 270, new Vector2(2.5f, 3));
                    c.AreaRect(0, 11, 8, 3);
                    c.AreaRect(0, -11, 8, 3);
                    c.AreaRect(11, 0, 3, 8);
                    c.AreaRect(-11, 0, 3, 8);
                    c.AreaCircle(14, 8, 1.5f);
                    c.AreaCircle(-14, -8, 1.5f);
                    c.Spawns(new Vector2(-9, 9), new Vector2(9, 9), new Vector2(-9, -9), new Vector2(9, -9));
                }).Backdrop = kSpaceBackdrop;

            Add("嵐のワープ", 24,
                "東西に長い闘技場。両端の扇風機が南北に往復しながら中央へ風を送る。四隅寄りにどちらも送るワープゲート、南北の中央に積み木だけを送り向きを反転させるゲート。",
                "風に押されてゲートへ流され、思わぬ場所へ飛ばされるのがこのステージの事故の元。南北の積み木専用ゲートはゲートの向きを基準に回すので、北のゲートに飛び込んだ弾は南のゲートから反対向きに飛び出す。風の切れ目とゲートの位置を覚えれば、遠くの相手を奇襲できる。",
                c =>
                {
                    c.Arena(0, 0, 34, 20, true, true, true, true);
                    c.Fan(new Vector2(-16, 6), new Vector2(-16, -6), Vector3.right, 14, 4, 12, 2);
                    c.Fan(new Vector2(16, -6), new Vector2(16, 6), Vector3.left, 14, 4, 12, 2);
                    c.WarpGate(-8, 6, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(8, 6, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(-8, -6, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(8, -6, 0, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(0, 7, 0, 1, WarpGateGimmick.TargetType.Blocks, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.RelativeToGate);
                    c.WarpGate(0, -7, 180, 1, WarpGateGimmick.TargetType.Blocks, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.RelativeToGate);
                    c.AreaCircle(0, 0, 2.5f);
                    c.AreaCircle(-4, 0, 1.2f);
                    // 四隅(壁にかぶせる)
                    c.AreaRect(-12, 8.5f, 4, 2);
                    c.AreaRect(12, 8.5f, 4, 2);
                    c.AreaRect(-12, -8.5f, 4, 2);
                    c.AreaRect(12, -8.5f, 4, 2);
                    c.Spawns(new Vector2(-4, 4), new Vector2(4, -4), new Vector2(4, 4), new Vector2(-4, -4));
                });

            Add("総力戦の闘技場", 28,
                "中央の回転砲台を、交互に立ち上がるせり上がり塀が囲む。四隅には加速床。",
                "砲台から扇状にばらまかれる積み木は、塀が下がっている面からしか外へ出てこない。塀が上がると中の積み木は閉じ込められ、塀の上は砲台を見下ろす高台になる。四隅の加速床で勢いをつけた弾や体が中央へ飛び込み、砲台のまわりは常に乱戦になる。",
                c =>
                {
                    c.MaxGimmickTypes = 3;
                    c.Arena(0, 0, 30, 30, true, true, true, true);
                    c.Cannon(0, 0, 0, TumikiCannon.RotationMode.Spin, 1, 4);
                    // 砲台を囲む塀(北・南は横長、東・西は縦長)。隣どうしで開始をずらして交互に立ち上がる
                    c.RisingWall(-2, 5, new Vector2(2, 1), 1.5f, 0.0f);
                    c.RisingWall(2, 5, new Vector2(2, 1), 1.5f, 2.0f);
                    c.RisingWall(5, 2, new Vector2(1, 2), 1.5f, 4.0f);
                    c.RisingWall(5, -2, new Vector2(1, 2), 1.5f, 6.0f);
                    c.RisingWall(2, -5, new Vector2(2, 1), 1.5f, 0.0f);
                    c.RisingWall(-2, -5, new Vector2(2, 1), 1.5f, 2.0f);
                    c.RisingWall(-5, -2, new Vector2(1, 2), 1.5f, 4.0f);
                    c.RisingWall(-5, 2, new Vector2(1, 2), 1.5f, 6.0f);
                    c.SpeedFloor(10, 10, new Vector2(3, 3), true);
                    c.SpeedFloor(-10, 10, new Vector2(3, 3), true);
                    c.SpeedFloor(10, -10, new Vector2(3, 3), true);
                    c.SpeedFloor(-10, -10, new Vector2(3, 3), true);
                    // 各辺の壁にかぶせる
                    c.AreaRect(0, 12.5f, 8, 3);
                    c.AreaRect(0, -12.5f, 8, 3);
                    c.AreaRect(12.5f, 0, 3, 8);
                    c.AreaRect(-12.5f, 0, 3, 8);
                    c.AreaCircle(0, 8, 1.5f);
                    c.AreaCircle(0, -8, 1.5f);
                    c.Spawns(new Vector2(-8, 3), new Vector2(8, -3), new Vector2(3, 8), new Vector2(-3, -8));
                });

            //----------------------------------------
            // 本編のステージ(Stage01〜)。家にある物とその周りをテーマにした背景の上で戦う
            //----------------------------------------
            BeginSeries(kStagePrefix);

            // お茶の間の机の上(Stage01〜05)。ちゃぶ台の上に積んだ本の上で戦う。背景: Chanoma

            Add("お盆の上", 24,
                "ちゃぶ台に置かれたお盆の上。縁が壁になった闘技場で、湯呑みのように並んだ4つのバンパーが中央の補給所を囲む。",
                "四方の縁(壁)で弾が跳ね返るので、どこにいても跳弾が飛んでくる。中央の補給所へはバンパーの間を抜けて入ることになり、はじかれた弾や体が思わぬ方向へ飛ぶ。お茶の間シリーズの入門として、基本の撃ち合いとバンパーの使い方を覚える舞台。",
                c =>
                {
                    c.Arena(0, 0, 24, 20, true, true, true, true);
                    c.Bumper(-6, -4, 1.4f);
                    c.Bumper(6, -4, 1.4f);
                    c.Bumper(-6, 4, 1.4f);
                    c.Bumper(6, 4, 1.4f);
                    c.AreaCircle(0, 0, 3.5f);
                    // 湯呑みのようなバンパーにかぶせる / お盆の縁(壁)の角にかぶせる
                    c.AreaCircle(-8, -5.5f, 1.8f);
                    c.AreaCircle(8, -5.5f, 1.8f);
                    c.AreaCircle(-8, 5.5f, 1.8f);
                    c.AreaCircle(8, 5.5f, 1.8f);
                    c.AreaRect(10.5f, 9, 4, 3);
                    c.AreaRect(-10.5f, -9, 4, 3);
                    c.Spawns(new Vector2(-9.5f, 0), new Vector2(9.5f, 0), new Vector2(0, -7.5f), new Vector2(0, 7.5f));
                }).Backdrop = kChanomaBackdrop;

            Add("卓上扇風機", 22,
                "東西だけ壁のある本の上。中央の扇風機二台が首を振るように東西へ往復し、南北の崖へ向けて風を送る。",
                "風の通り道が左右に動き続けるので、南北の崖際に立つと風に押されて落ちる危険がある。補給所は風の来ない東西の壁際。扇風機の往復を読んで風の切れ目を渡り、反対側の補給所へ攻め込むタイミングが勝負を分ける。",
                c =>
                {
                    c.Arena(0, 0, 24, 24, false, false, true, true);
                    c.Fan(new Vector2(-6, 0.7f), new Vector2(6, 0.7f), Vector3.forward, 12, 4, 12, 2);
                    c.Fan(new Vector2(-6, -0.7f), new Vector2(6, -0.7f), Vector3.back, 12, 4, 12, 2);
                    c.AreaRect(-9.5f, 0, 3, 10);
                    c.AreaRect(9.5f, 0, 3, 10);
                    // 崖の外へはみ出す / 中央の扇風機にかぶせる
                    c.AreaRect(0, 11, 8, 3);
                    c.AreaRect(0, -11, 8, 3);
                    c.AreaRect(0, 0, 4, 8);
                    c.AreaCircle(4, -5, 1.5f);
                    c.AreaCircle(-4, 5, 1.5f);
                    c.Spawns(new Vector2(-9, -8), new Vector2(9, -8), new Vector2(-9, 8), new Vector2(9, 8));
                }).Backdrop = kChanomaBackdrop;

            Add("みかんの回転盆", 22,
                "中央の本の上に、みかんを載せるような回転盆。四方の小さな本の島と、細い栞の橋でつながっている。",
                "積み木は中央の回転盆の上に湧き、回りながら流れていく。各自の島から橋を渡って取りに行くが、橋の両側は崖なので、渡っている最中に撃たれると逃げ場がない。回転盆の上では弾が曲がるので、島から盆の上の相手を狙うには曲がりを読む必要がある。",
                c =>
                {
                    c.Floor(0, 0, 14, 14);
                    c.Turntable(0, 0, 5.5f, true, 45);
                    c.Arena(0, 13, 10, 6, true, false, false, false);
                    c.Arena(0, -13, 10, 6, false, true, false, false);
                    c.Arena(13, 0, 6, 10, false, false, true, false);
                    c.Arena(-13, 0, 6, 10, false, false, false, true);
                    c.Floor(0, 8.5f, 3, 3);
                    c.Floor(0, -8.5f, 3, 3);
                    c.Floor(8.5f, 0, 3, 3);
                    c.Floor(-8.5f, 0, 3, 3);
                    c.AreaCircle(0, 0, 3);
                    // 四方の島の角(崖へはみ出す) / 中央の本の角(崖へはみ出す)
                    c.AreaRect(-4, 12.5f, 3, 4);
                    c.AreaRect(4, -12.5f, 3, 4);
                    c.AreaRect(12.5f, 4, 4, 3);
                    c.AreaRect(-12.5f, -4, 4, 3);
                    c.AreaCircle(6.5f, 6.5f, 1.5f);
                    c.AreaCircle(-6.5f, -6.5f, 1.5f);
                    c.Spawns(new Vector2(0, 13), new Vector2(0, -13), new Vector2(13, 0), new Vector2(-13, 0));
                }).Backdrop = kChanomaBackdrop;

            Add("栞の橋", 26,
                "開いた二冊の本を一本の栞がつなぐ。本の外側は壁、栞の両側は崖。栞の入口をバンパーが固める。",
                "補給所はそれぞれの本の両端にあるので、序盤は自分の本の上で積み木を集める撃ち合い。相手の本へ攻め込むには細い栞を渡るしかなく、入口のバンパーにはじかれると崖へ落ちる危険がある。栞の上での撃ち合いと、入口での押し合いが山場。",
                c =>
                {
                    c.Arena(-10, 0, 8, 24, true, true, false, true);
                    c.Arena(10, 0, 8, 24, true, true, true, false);
                    c.Floor(0, 0, 12, 4);
                    c.Bumper(-8.5f, 4, 1.2f);
                    c.Bumper(-8.5f, -4, 1.2f);
                    c.Bumper(8.5f, 4, 1.2f);
                    c.Bumper(8.5f, -4, 1.2f);
                    c.AreaRect(-10, 8.5f, 6, 4);
                    c.AreaRect(-10, -8.5f, 6, 4);
                    c.AreaRect(10, 8.5f, 6, 4);
                    c.AreaRect(10, -8.5f, 6, 4);
                    // 栞(両側の崖へはみ出す) / 入口のバンパーにかぶせる
                    c.AreaRect(0, 0, 10, 5);
                    c.AreaCircle(8.5f, 6, 1.6f);
                    c.AreaCircle(-8.5f, -6, 1.6f);
                    c.Spawns(new Vector2(-11, -2.5f), new Vector2(-11, 2.5f), new Vector2(11, -2.5f), new Vector2(11, 2.5f));
                }).Backdrop = kChanomaBackdrop;

            Add("ちゃぶ台返し", 26,
                "南側だけ壁のない大きな本の上。北の隅の扇風機二台が、ちゃぶ台をひっくり返すような突風を南の崖へ斜めに吹きつける。",
                "風は北の隅から南の崖へ向かって吹くので、南へ行くほど落下の危険が高い。補給所は風上の北の壁際と、崖に近い南の中央。安全な北で地道に集めるか、崖際の補給所で一気に稼ぐかの選択になる。中央のバンパーにはじかれて風に乗ると、そのまま崖まで運ばれる。",
                c =>
                {
                    c.Arena(0, 0, 30, 24, true, false, true, true);
                    c.Fan(new Vector2(-13, 10), new Vector2(-13, 10), new Vector3(1, 0, -1), 20, 5, 12, 0);
                    c.Fan(new Vector2(13, 10), new Vector2(13, 10), new Vector3(-1, 0, -1), 20, 5, 12, 0);
                    c.Bumper(-6, 2, 1.3f);
                    c.Bumper(6, 2, 1.3f);
                    c.Bumper(0, -4, 1.3f);
                    c.AreaRect(0, 9, 10, 3);
                    c.AreaCircle(0, -8.5f, 2.5f);
                    // 南の崖の外へはみ出す / 隅の扇風機・バンパーにかぶせる
                    c.AreaRect(0, -11.5f, 10, 2);
                    c.AreaCircle(11.5f, 9, 2);
                    c.AreaCircle(-11.5f, 9, 2);
                    c.AreaCircle(6, 4, 1.6f);
                    c.AreaCircle(0, -6.2f, 1.6f);
                    c.Spawns(new Vector2(-10, -2), new Vector2(10, -2), new Vector2(-10, 6), new Vector2(10, 6));
                }).Backdrop = kChanomaBackdrop;

            // 台所の調理台(Stage06〜08)。調理台の上に重ねたまな板の上で戦う。背景: Kitchen
            Add("まな板の上", 24,
                "調理台に置いた細長いまな板の上。長い辺には縁(壁)があり、短い辺の先は調理台へ真っ逆さま。野菜のように並んだ4つのバンパーが中央を囲む。",
                "東西の端は崖なので、端に近い補給所は積み木が湧いても取りに行くのが怖い。中央の補給所はバンパーの間にあり、はじかれた弾が縁で跳ね返って戻ってくる。端へ押し出すか、中央で跳弾を浴びるかの駆け引き。",
                c =>
                {
                    c.Arena(0, 0, 26, 18, true, true, false, false);
                    c.Bumper(-6, 0, 1.3f);
                    c.Bumper(6, 0, 1.3f);
                    c.Bumper(0, 4.5f, 1.3f);
                    c.Bumper(0, -4.5f, 1.3f);
                    c.AreaCircle(0, 0, 2.5f);
                    // 崖の外へはみ出す / 縁(壁)にかぶせる / バンパーにかぶせる
                    c.AreaRect(12, 0, 3, 6);
                    c.AreaRect(-12, 0, 3, 6);
                    c.AreaRect(0, 8, 6, 3);
                    c.AreaRect(0, -8, 6, 3);
                    c.AreaCircle(-6, 2.2f, 1.6f);
                    c.Spawns(new Vector2(-10, 5), new Vector2(10, 5), new Vector2(-10, -5), new Vector2(10, -5));
                }).Backdrop = kKitchenBackdrop;

            Add("電子レンジの回転皿", 20,
                "四方を壁に囲まれた電子レンジの中。床の大半を大きな回転皿が占め、ゆっくり逆回りに回り続ける。",
                "回転皿の上を通る弾は大きく曲がり、壁で跳ね返ってさらに読めない軌道になる。出現位置は皿の外の四隅で、皿に乗ると流されて位置がずれる。皿の上の補給所に湧いた積み木は回りながら外へ運ばれるので、皿の縁で待ち構えるのも手。",
                c =>
                {
                    c.Arena(0, 0, 22, 22, true, true, true, true);
                    c.Turntable(0, 0, 7, false, 50);
                    c.AreaCircle(0, 0, 2.5f);
                    c.AreaCircle(4, 4, 1.5f);
                    c.AreaRect(0, 10, 6, 3);
                    c.AreaRect(0, -10, 6, 3);
                    c.AreaRect(10, 0, 3, 6);
                    c.AreaRect(-10, 0, 3, 6);
                    c.Spawns(new Vector2(-8.5f, -8.5f), new Vector2(8.5f, -8.5f), new Vector2(-8.5f, 8.5f), new Vector2(8.5f, 8.5f));
                }).Backdrop = kKitchenBackdrop;

            Add("流し台", 24,
                "中央に流しの穴が空いた調理台。西の壁際を換気扇の風が上下に往復し、東の崖へ向かって吹き続ける。",
                "穴のまわりを回る口の字の通路で、東へ行くほど風下で崖が近い。風上(西)から撃つ弾は風に乗って伸び、風下からの弾は押し戻される。穴の縁の補給所は落ちる危険と隣り合わせ。風の往復の切れ目を読んで攻め込む。",
                c =>
                {
                    c.Floor(-0.5f, 7.5f, 29, 7);
                    c.Floor(-0.5f, -7.5f, 29, 7);
                    c.Floor(-10, 0, 10, 8);
                    c.Floor(9.5f, 0, 9, 8);
                    c.Wall(-0.5f, 10.5f, 29, 1);
                    c.Wall(-0.5f, -10.5f, 29, 1);
                    c.Wall(-14.5f, 0, 1, 20);
                    c.Fan(new Vector2(-12, -6), new Vector2(-12, 6), Vector3.right, 26, 4, 10, 2);
                    c.Bumper(0, 7.5f, 1.2f);
                    c.Bumper(0, -7.5f, 1.2f);
                    c.AreaRect(9.5f, 0, 4, 6);
                    // 東の崖・中央の穴へはみ出す / 換気扇の通り道の近く / バンパーにかぶせる
                    c.AreaRect(13.5f, 7.5f, 3, 6);
                    c.AreaRect(13.5f, -7.5f, 3, 6);
                    c.AreaRect(0, 0, 14, 10);
                    c.AreaCircle(-9, 0, 2);
                    c.AreaCircle(2.5f, 7.5f, 1.8f);
                    c.Spawns(new Vector2(-8, 7.5f), new Vector2(8, 7.5f), new Vector2(-8, -7.5f), new Vector2(8, -7.5f));
                }).Backdrop = kKitchenBackdrop;

            // 子ども部屋の勉強机(Stage09〜11)。机に積んだ教科書・ノートの上で戦う。背景: Desk
            Add("消しゴムの林", 26,
                "四方を壁で囲んだノートの上に、消しゴムのようなバンパーが互い違いに立ち並ぶ。",
                "どの方向に撃っても消しゴムにはじかれ、壁で跳ね返る跳弾の多いステージ。出現位置は四隅で、補給所は中央と壁際。林の隙間を縫って撃つか、壁に跳ね返して裏から当てるかの撃ち合い。",
                c =>
                {
                    c.Arena(0, 0, 30, 20, true, true, true, true);
                    c.Bumper(-8, -4, 1.3f);
                    c.Bumper(0, -4, 1.3f);
                    c.Bumper(8, -4, 1.3f);
                    c.Bumper(-4, 4, 1.3f);
                    c.Bumper(4, 4, 1.3f);
                    c.AreaCircle(0, 0, 2.5f);
                    c.AreaRect(0, 9.5f, 8, 3);
                    c.AreaRect(0, -9.5f, 8, 3);
                    c.AreaRect(14, 0, 3, 6);
                    c.AreaRect(-14, 0, 3, 6);
                    c.AreaCircle(-8, -1.8f, 1.6f);
                    c.Spawns(new Vector2(-12, -7), new Vector2(12, -7), new Vector2(-12, 7), new Vector2(12, 7));
                }).Backdrop = kDeskBackdrop;

            Add("定規の橋", 22,
                "東西の教科書を三本の定規がつなぐ。真ん中の定規の途中には小さな回転台の付いた消しゴムの島。",
                "自分の教科書の上で積み木を集め、細い定規を渡って攻め込む。定規の両側は崖なので、撃たれて避けると落ちる。中央の島の回転台に乗ると流されて定規から外れやすい。三本のどの定規から攻めるかの読み合い。",
                c =>
                {
                    c.Arena(-11, 0, 10, 16, true, true, false, true);
                    c.Arena(11, 0, 10, 16, true, true, true, false);
                    c.Floor(0, 5, 12, 2.5f);
                    c.Floor(0, -5, 12, 2.5f);
                    c.Floor(0, 0, 12, 2.5f);
                    c.Floor(0, 0, 6, 6);
                    c.Turntable(0, 0, 2.8f, true, 60);
                    c.AreaCircle(0, 0, 2);
                    c.AreaRect(11, 0, 4, 4);
                    c.AreaRect(-11, 0, 4, 4);
                    // 定規(崖へはみ出す) / 壁にかぶせる
                    c.AreaRect(0, 5, 8, 3.5f);
                    c.AreaRect(0, -5, 8, 3.5f);
                    c.AreaRect(-11, 8, 6, 2.5f);
                    c.Spawns(new Vector2(-11, -5), new Vector2(11, -5), new Vector2(-11, 5), new Vector2(11, 5));
                }).Backdrop = kDeskBackdrop;

            Add("鉛筆の仕切り", 24,
                "鉛筆の仕切り(壁)で区切られた筆箱の中。南西の隅の小型扇風機が、南の通路へ東向きの風を送る。",
                "仕切りに沿って弾が跳ね返るので、壁越しの跳弾を狙える。南の通路は風に乗った弾が速く飛ぶ危険地帯。中央のバンパーのまわりと、仕切りの陰の補給所を行き来しながら、壁を使って相手の死角から撃つ。",
                c =>
                {
                    c.Arena(0, 0, 28, 22, true, true, true, true);
                    c.Wall(-5, 4, 1, 10);
                    c.Wall(5, -4, 1, 10);
                    c.Fan(new Vector2(-12.5f, -8), new Vector2(-12.5f, -8), Vector3.right, 24, 4, 10, 0);
                    c.Bumper(0, 0, 1.3f);
                    c.AreaRect(0, 10, 8, 3);
                    c.AreaRect(0, -10, 8, 3);
                    c.AreaRect(13, -6, 3, 6);
                    c.AreaRect(-13, 4, 3, 6);
                    c.AreaCircle(-10.5f, -8, 1.8f);
                    c.AreaCircle(0, 2.2f, 1.6f);
                    c.Spawns(new Vector2(-10, 8), new Vector2(10, 8), new Vector2(-10, -3), new Vector2(10, 3));
                }).Backdrop = kDeskBackdrop;

            // 夏の縁側(Stage12〜14)。縁側の板の間と庭で戦う。背景: Engawa
            Add("縁側の板の間", 24,
                "障子の前の細長い板の間。南と東西は庭へ落ちる崖。西の端の扇風機が首を振るように南北へ往復し、東へ風を送る。",
                "風は西から東へ吹き抜けるので、東の端ほど落ちやすい。南の縁も崖なので、風に流されながら撃ち合うと足元が危うい。補給所は障子の前・庭側の縁・東の端に分かれ、風上の西を取った人が有利になる。",
                c =>
                {
                    c.Arena(0, 0, 36, 12, true, false, false, false);
                    c.Fan(new Vector2(-16, -3), new Vector2(-16, 3), Vector3.right, 30, 4, 10, 1.5f);
                    c.Bumper(6, 2, 1.2f);
                    c.Bumper(-2, -2, 1.2f);
                    c.AreaCircle(2, -0.5f, 1.5f);
                    c.AreaRect(17, 0, 3, 8);
                    c.AreaRect(0, -5.5f, 10, 3);
                    c.AreaRect(0, 5.5f, 8, 3);
                    c.AreaCircle(-13.5f, 0, 1.8f);
                    c.AreaCircle(6, 4.2f, 1.6f);
                    c.Spawns(new Vector2(-10, 3), new Vector2(10, 3), new Vector2(-10, -3), new Vector2(10, -3));
                }).Backdrop = kEngawaBackdrop;

            Add("庭の飛び石", 20,
                "庭に並んだ飛び石が、中央の大きな石でX字に交わる。中央の石には回転台。まわりはすべて崖。",
                "各自が四隅の石から始まり、斜めに並ぶ飛び石を渡って中央へ向かう。石と石の継ぎ目は狭く、横から撃たれると落ちやすい。中央の回転台の補給所を取るか、途中の石の補給所で稼ぐか。",
                c =>
                {
                    c.Floor(-12, -8, 8, 8);
                    c.Floor(-6, -4, 6, 6);
                    c.Floor(0, 0, 8, 8);
                    c.Floor(6, 4, 6, 6);
                    c.Floor(12, 8, 8, 8);
                    c.Floor(-12, 8, 8, 8);
                    c.Floor(-6, 4, 6, 6);
                    c.Floor(6, -4, 6, 6);
                    c.Floor(12, -8, 8, 8);
                    c.Turntable(0, 0, 3.5f, false, 40);
                    c.AreaCircle(0, 0, 2.5f);
                    // 途中の石・四隅の石の外側(崖へはみ出す)
                    c.AreaRect(6, 4, 7, 7);
                    c.AreaRect(-6, -4, 7, 7);
                    c.AreaRect(-6, 4, 7, 7);
                    c.AreaRect(6, -4, 7, 7);
                    c.AreaRect(14.8f, -10.8f, 4, 4);
                    c.AreaRect(-14.8f, 10.8f, 4, 4);
                    c.Spawns(new Vector2(-12, -8), new Vector2(12, -8), new Vector2(-12, 8), new Vector2(12, 8));
                }).Backdrop = kEngawaBackdrop;

            Add("蚊取り線香の渦", 24,
                "蚊取り線香のような渦巻きの仕切り(壁)が中心へ向かって巻いている。四方の通路から中心の補給所へ向かう。",
                "渦巻きの通路は細く、仕切りで弾が何度も跳ね返る。中心へ行くには通路を回り込む必要があり、外周の補給所で集めてから中心を目指すのが基本。曲がり角の先での鉢合わせと、壁越しの跳弾が見どころ。",
                c =>
                {
                    c.Arena(0, 0, 26, 26, true, true, true, true);
                    c.Wall(-3, 9, 20, 1);
                    c.Wall(7.5f, 1, 1, 15);
                    c.Wall(-0.5f, -6, 15, 1);
                    c.Wall(-8.5f, -0.75f, 1, 9.5f);
                    c.Wall(-3, 3.5f, 10, 1);
                    c.Bumper(10.5f, 10.5f, 1.2f);
                    c.Bumper(-10.5f, -10.5f, 1.2f);
                    c.AreaCircle(2, -1, 2.5f);
                    c.AreaRect(0, 11.5f, 8, 4);
                    c.AreaRect(10.5f, -5, 4, 6);
                    c.AreaRect(-5, -10, 6, 4);
                    c.AreaRect(-11, 6, 4, 4);
                    c.AreaCircle(-3, 6.2f, 1.8f);
                    c.Spawns(new Vector2(-10, 11.2f), new Vector2(10.5f, 0), new Vector2(0, -10), new Vector2(-11, -2));
                }).Backdrop = kEngawaBackdrop;

            // お風呂場(Stage15〜17)。タイルの床に置いた風呂ふた・洗面器の上で戦う。背景: Bath
            Add("洗い場の渦", 24,
                "南側だけ壁のない洗い場。南寄りの排水口の渦(高速の回転台)が、近づいた物を巻き込んで流す。",
                "排水口の渦は南の崖のすぐそばにあり、乗ると流されて崖へ運ばれやすい。渦の上の補給所は危険だが積み木が多く湧く。北の壁際は安全な撃ち合いの場。バンパーにはじかれて渦に入ると、そのまま崖へ一直線。",
                c =>
                {
                    c.Arena(0, 0, 28, 22, true, false, true, true);
                    c.Turntable(0, -6, 4, false, 70);
                    c.Bumper(-8, 4, 1.3f);
                    c.Bumper(8, 4, 1.3f);
                    c.AreaCircle(0, -6, 2);
                    c.AreaRect(0, -10.5f, 8, 3);
                    c.AreaRect(0, 10.5f, 8, 3);
                    c.AreaRect(13.5f, 0, 3, 6);
                    c.AreaRect(-13.5f, 0, 3, 6);
                    c.AreaCircle(-6, 5.8f, 1.6f);
                    c.Spawns(new Vector2(-10, -5), new Vector2(10, -5), new Vector2(-10, 7), new Vector2(10, 7));
                }).Backdrop = kBathBackdrop;

            Add("湯船のふた", 24,
                "湯船に渡した三枚の風呂ふたの上。長い辺は湯船の縁(壁)、短い辺の先は湯船へ落ちる崖。アヒルのようなバンパーが浮かぶ。",
                "東西の端は崖なので、端の補給所は落下と隣り合わせ。中央のバンパーのまわりで撃ち合うと、はじかれた弾が縁で跳ね返って戻ってくる。三枚のふたの継ぎ目をまたいで追いかけっこが起きる。",
                c =>
                {
                    c.Floor(-8, 0, 8, 20);
                    c.Floor(0, 0, 8, 20);
                    c.Floor(8, 0, 8, 20);
                    c.Wall(0, 9.5f, 24, 1);
                    c.Wall(0, -9.5f, 24, 1);
                    c.Bumper(-8, 3, 1.3f);
                    c.Bumper(0, -3, 1.3f);
                    c.Bumper(8, 3, 1.3f);
                    c.AreaRect(11.5f, 0, 3, 8);
                    c.AreaRect(-11.5f, 0, 3, 8);
                    c.AreaCircle(0, -0.8f, 1.6f);
                    c.AreaRect(0, 8, 8, 3);
                    c.AreaRect(0, -8, 8, 3);
                    c.AreaCircle(-10, 4.5f, 1.6f);
                    c.Spawns(new Vector2(-9, -5), new Vector2(9, -5), new Vector2(-6, 6), new Vector2(6, 6));
                }).Backdrop = kBathBackdrop;

            Add("換気扇のつむじ風", 22,
                "壁のない風呂いすの上。北西と南東の隅の扇風機が縁に沿って逆向きに風を送り、外周につむじ風ができる。",
                "外周の風は時計回りに体と弾を運び、縁から押し出そうとする。中央はバンパー二つが並ぶ無風地帯。外周の補給所は風に乗って崖へ流されやすく、中央の補給所はバンパーにはじかれやすい。",
                c =>
                {
                    c.Arena(0, 0, 24, 24, false, false, false, false);
                    c.Fan(new Vector2(-10, 10), new Vector2(-10, 10), Vector3.right, 20, 4, 12, 0);
                    c.Fan(new Vector2(10, -10), new Vector2(10, -10), Vector3.left, 20, 4, 12, 0);
                    c.Bumper(0, 3, 1.2f);
                    c.Bumper(0, -3, 1.2f);
                    c.AreaCircle(0, 1, 1.6f);
                    c.AreaRect(0, 11.5f, 8, 2.5f);
                    c.AreaRect(0, -11.5f, 8, 2.5f);
                    c.AreaRect(11.5f, 0, 2.5f, 8);
                    c.AreaRect(-11.5f, 0, 2.5f, 8);
                    c.AreaCircle(-8, 10, 1.8f);
                    c.Spawns(new Vector2(-6, 6), new Vector2(6, 6), new Vector2(-6, -6), new Vector2(6, -6));
                }).Backdrop = kBathBackdrop;

            //----------------------------------------
            // 追加ギミックの確認用ステージ(GimmickDebugStage01〜)。背景なし・種類の数の制限なし
            //----------------------------------------
            BeginSeries(kGimmickDebugPrefix);

            Add("追加ギミック見本市", 30,
                "追加ギミック6種(せり上がり塀・加速床/減速床・移動床・ワープゲート・シューターレーン・回転砲台)を1つの闘技場にまとめた確認用ステージ。",
                "北西の塀が波のように立ち上がって壁と高台を作り、北東の加速床・減速床で弾と体の速さが変わる。南西の穴は移動床でしか渡れず、ワープゲートは色(グループ)ごとに行き先が決まっている。東のシューターレーンは乗った積み木を一直線に撃ち出し、中央南の回転砲台は生成命令のたびに積み木を扇状にばらまく。",
                c =>
                {
                    c.MaxGimmickTypes = int.MaxValue;

                    // 遊べる範囲 36×32m。南西に穴(移動床で渡る)を空けた床と、四方の壁
                    c.Floor(0, 3, 38, 28);
                    c.Floor(0, -15.5f, 38, 3);
                    c.Floor(-17, -12.5f, 4, 3);
                    c.Floor(8, -12.5f, 22, 3);
                    c.Wall(0, 16.5f, 38, 1);
                    c.Wall(0, -16.5f, 38, 1);
                    c.Wall(18.5f, 0, 1, 34);
                    c.Wall(-18.5f, 0, 1, 34);

                    // 案1: せり上がり塀(北西)。開始を0.3秒ずつずらして、西から東へ波のように立ち上がる
                    for (int i = 0; i < 5; i++) c.RisingWall(-15 + i * 2, 12, new Vector2(2, 1.5f), 1.5f, i * 0.3f);

                    // 案2: 加速床・減速床(北東)
                    c.SpeedFloor(4, 12, new Vector2(3, 3), true);
                    c.SpeedFloor(10, 12, new Vector2(3, 3), false);

                    // 案3: 移動床(南西の穴を往復)
                    c.MovingFloor(new Vector2(2.9f, 2.9f), MovingFloorGimmick.LoopMode.PingPong, 2.5f, 1.5f,
                        new Vector2(-13.5f, -12.5f), new Vector2(-4.5f, -12.5f));

                    // 案4: ワープゲート
                    //  グループ0(どちらも・ランダム): 西・北・南東の3か所
                    c.WarpGate(-15, 4, 90, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(0, 9, 180, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(16, -12.5f, 270, 0, WarpGateGimmick.TargetType.Both, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.Keep);
                    //  グループ1(プレイヤーのみ・順番)
                    c.WarpGate(-9, -7, 0, 1, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    c.WarpGate(8, 4, 0, 1, WarpGateGimmick.TargetType.Players, WarpGateGimmick.Selection.Sequential, WarpGateGimmick.Orientation.Keep);
                    //  グループ2(積み木のみ・ゲートの向きを基準に回す)
                    c.WarpGate(-15, -4, 90, 2, WarpGateGimmick.TargetType.Blocks, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.RelativeToGate);
                    c.WarpGate(16, 10, 270, 2, WarpGateGimmick.TargetType.Blocks, WarpGateGimmick.Selection.Random, WarpGateGimmick.Orientation.RelativeToGate);

                    // 案5: シューターレーン(東: 西向き / 南東: 北向き)
                    c.ShooterLane(15.5f, 3, 270, new Vector2(2.5f, 3));
                    c.ShooterLane(12, -7, 0, new Vector2(2.5f, 3));

                    // 案6: 回転砲台(中央南。生成エリアの一種)
                    c.Cannon(2, -6, 0, TumikiCannon.RotationMode.Spin, 1, 4);

                    // 積み木の生成エリア
                    c.AreaCircle(0, 2, 2.5f);
                    c.AreaRect(-11, 8, 6, 3);
                    c.AreaRect(7, 8.5f, 6, 2.5f);
                    c.AreaCircle(-11, -2, 2);
                    c.AreaRect(8, -12.5f, 6, 3);
                    c.AreaCircle(12, -1, 1.8f);
                    c.Spawns(new Vector2(-6, 0), new Vector2(6, -1), new Vector2(-2, 6), new Vector2(-3, -6));
                });

            return list;
        }

        //========================================
        // ステージの組み立て
        //========================================

        // ステージ1つ分の組み立てと、配置の検証(床の上か・ギミックと重なっていないか)を座標の計算で行う
        // (開いている他のシーンの物と混ざらないよう、物理演算は使わない)
        class Ctx
        {
            public Scene Scene;
            public Transform Stage, Gimmicks, Areas;
            public readonly List<Rect> Floors = new List<Rect>();
            public readonly List<Vector2> SpawnPoints = new List<Vector2>();
            public readonly List<TumikiSpawnArea> AreaList = new List<TumikiSpawnArea>();
            public readonly List<string> AreaDesc = new List<string>();
            // 積み木を置けない場所(バンパー・扇風機の当たり判定)を円で近似
            public readonly List<Vector3> Blockers = new List<Vector3>(); // x, z, 半径
            public readonly List<Vector3> Tables = new List<Vector3>();   // x, z, 半径
            public readonly SortedSet<string> GimmickTypes = new SortedSet<string>();
            // ギミックの種類ごとの数(名前 → 数)
            public readonly Dictionary<string, int> GimmickCounts = new Dictionary<string, int>();
            // 1ステージに置いてよいギミックの種類の数(ギミックを試すステージでは増やす)
            public int MaxGimmickTypes = 2;
            // 床・壁の見た目(背景に合わせる場合。null なら既定)
            public Material FloorMaterial, WallMaterial;
            int m_fanIndex;

            public void Floor(float cx, float cz, float w, float d)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Floor_{Floors.Count}";
                go.transform.SetParent(Stage, false);
                go.transform.localPosition = new Vector3(cx, -0.5f, cz);
                go.transform.localScale = new Vector3(w, 1.0f, d);
                go.GetComponent<Renderer>().sharedMaterial = FloorMaterial != null ? FloorMaterial : Load<Material>(kStageMaterial);
                go.isStatic = true;
                Floors.Add(new Rect(cx - w * 0.5f, cz - d * 0.5f, w, d));
            }

            public void Wall(float cx, float cz, float w, float d)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Wall";
                go.transform.SetParent(Stage, false);
                go.transform.localPosition = new Vector3(cx, kWallHeight * 0.5f, cz);
                go.transform.localScale = new Vector3(w, kWallHeight, d);
                go.GetComponent<Renderer>().sharedMaterial = WallMaterial != null ? WallMaterial : SeeThroughWallMaterial();
                go.AddComponent<ReflectWall>();
                go.isStatic = true;
            }

            // 遊べる範囲 w×d の床。壁を置く辺は壁の下まで床を伸ばす
            public void Arena(float cx, float cz, float w, float d, bool north, bool south, bool east, bool west)
            {
                float xMin = cx - w * 0.5f - (west ? 1 : 0);
                float xMax = cx + w * 0.5f + (east ? 1 : 0);
                float zMin = cz - d * 0.5f - (south ? 1 : 0);
                float zMax = cz + d * 0.5f + (north ? 1 : 0);
                Floor((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f, xMax - xMin, zMax - zMin);
                float fw = xMax - xMin, fd = zMax - zMin, mx = (xMin + xMax) * 0.5f, mz = (zMin + zMax) * 0.5f;
                if (north) Wall(mx, zMax - 0.5f, fw, 1);
                if (south) Wall(mx, zMin + 0.5f, fw, 1);
                if (east) Wall(xMax - 0.5f, mz, 1, fd);
                if (west) Wall(xMin + 0.5f, mz, 1, fd);
            }

            public void Bumper(float x, float z, float scale)
            {
                var go = Place("Bumper", new Vector3(x, 0, z));
                go.transform.localScale = new Vector3(scale, 1.0f, scale);
                Blockers.Add(new Vector3(x, z, kBumperBaseRadius * scale * 1.12f)); // 外周のリングまで
                Count("バンパー");
            }

            // ギミックを1つ数える(説明の「ギミック」の欄と、種類の数の検証に使う)
            public void Count(string gimmickName)
            {
                GimmickTypes.Add(gimmickName);
                GimmickCounts.TryGetValue(gimmickName, out int n);
                GimmickCounts[gimmickName] = n + 1;
            }

            // speed: 往復の速さ(0なら止まったまま)
            public void Fan(Vector2 a, Vector2 b, Vector3 dir, float length, float width, float force, float speed)
            {
                var group = new GameObject($"Fan_Group_{m_fanIndex++}").transform;
                group.SetParent(Gimmicks, false);
                var pa = new GameObject("PointA").transform;
                pa.SetParent(group, false);
                pa.position = new Vector3(a.x, 0, a.y);
                var pb = new GameObject("PointB").transform;
                pb.SetParent(group, false);
                pb.position = new Vector3(b.x, 0, b.y);

                var go = Place("Fan", pa.position, group);
                var fan = go.GetComponent<FanGimmick>();
                var so = new SerializedObject(fan);
                so.FindProperty("m_pointA").objectReferenceValue = pa;
                so.FindProperty("m_pointB").objectReferenceValue = pb;
                so.FindProperty("m_windDirection").vector3Value = dir.normalized;
                so.FindProperty("m_windLength").floatValue = length;
                so.FindProperty("m_windSize").vector2Value = new Vector2(width, 2.0f);
                so.FindProperty("m_windForce").floatValue = force;
                so.FindProperty("m_moveSpeed").floatValue = speed;
                so.ApplyModifiedPropertiesWithoutUndo();
                go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
                fan.ApplyParticleSettings();

                // 往復の経路全体を積み木を置けない場所にする(点で近似)
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
                    Blockers.Add(new Vector3(p.x, p.y, 0.8f));
                }
                Count("扇風機");
            }

            public void Turntable(float x, float z, float radius, bool clockwise, float speed)
            {
                var go = Place("Turntable", new Vector3(x, 0, z));
                var table = go.GetComponent<TurntableGimmick>();
                go.name = clockwise ? "Turntable_Clockwise" : "Turntable_CounterClockwise";
                var so = new SerializedObject(table);
                so.FindProperty("m_direction").enumValueIndex = (int)(clockwise ? TurntableGimmick.Direction.Clockwise : TurntableGimmick.Direction.CounterClockwise);
                so.FindProperty("m_radius").floatValue = radius;
                so.FindProperty("m_speed").floatValue = speed;
                so.ApplyModifiedPropertiesWithoutUndo();
                table.ApplyShape();
                Tables.Add(new Vector3(x, z, radius));
                Count("回転台");
            }

            //---- 追加ギミック(プレハブは AdditionalGimmickBuilder が作る) ----

            int m_movingFloorIndex;

            // せり上がり塀。phaseOffset: 開始のずれ(秒)。並べて少しずつずらすと波のように立ち上がる
            public void RisingWall(float x, float z, Vector2 size, float height, float phaseOffset)
            {
                var go = Place(AdditionalGimmickBuilder.kRisingWall, new Vector3(x, 0, z));
                var wall = go.GetComponent<RisingWallGimmick>();
                var so = new SerializedObject(wall);
                so.FindProperty("m_size").vector2Value = size;
                so.FindProperty("m_wallHeight").floatValue = height;
                so.FindProperty("m_phaseOffset").floatValue = phaseOffset;
                so.ApplyModifiedPropertiesWithoutUndo();
                wall.ApplyShape();
                Count("せり上がり塀");
            }

            public void SpeedFloor(float x, float z, Vector2 size, bool accelerate)
            {
                var go = Place(accelerate ? AdditionalGimmickBuilder.kAccelerateFloor : AdditionalGimmickBuilder.kDecelerateFloor, new Vector3(x, 0, z));
                var floor = go.GetComponent<SpeedFloorGimmick>();
                var so = new SerializedObject(floor);
                so.FindProperty("m_size").vector2Value = size;
                so.ApplyModifiedPropertiesWithoutUndo();
                floor.ApplyShape();
                Count(accelerate ? "加速床" : "減速床");
            }

            // 移動床。points の順に移動する(最初の地点から始める)。床の上面は床と同じ高さ
            public void MovingFloor(Vector2 size, MovingFloorGimmick.LoopMode mode, float speed, float wait, params Vector2[] points)
            {
                var group = new GameObject($"MovingFloor_Group_{m_movingFloorIndex++}").transform;
                group.SetParent(Gimmicks, false);
                var go = Place(AdditionalGimmickBuilder.kMovingFloor, new Vector3(points[0].x, 0, points[0].y), group);
                var floor = go.GetComponent<MovingFloorGimmick>();
                var so = new SerializedObject(floor);
                var list = so.FindProperty("m_waypoints");
                list.arraySize = points.Length;
                for (int i = 0; i < points.Length; i++)
                {
                    var t = new GameObject($"Point{i}").transform;
                    t.SetParent(group, false);
                    t.position = new Vector3(points[i].x, 0, points[i].y);
                    var element = list.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("Point").objectReferenceValue = t;
                    element.FindPropertyRelative("WaitTime").floatValue = -1.0f;
                }
                so.FindProperty("m_size").vector2Value = size;
                so.FindProperty("m_loopMode").enumValueIndex = (int)mode;
                so.FindProperty("m_speed").floatValue = speed;
                so.FindProperty("m_waitTime").floatValue = wait;
                so.ApplyModifiedPropertiesWithoutUndo();
                floor.ApplyShape();
                Count("移動床");
            }

            // ワープゲート。yaw: ゲートの向き(ゲートの向きを基準に回す方式で使う)
            public void WarpGate(float x, float z, float yaw, int group, WarpGateGimmick.TargetType target,
                WarpGateGimmick.Selection selection, WarpGateGimmick.Orientation orientation, float radius = 1.0f)
            {
                var go = Place(AdditionalGimmickBuilder.kWarpGate, new Vector3(x, 0, z));
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                go.name = $"WarpGate_G{group}_{target}";
                var gate = go.GetComponent<WarpGateGimmick>();
                var so = new SerializedObject(gate);
                so.FindProperty("m_radius").floatValue = radius;
                so.FindProperty("m_group").intValue = group;
                so.FindProperty("m_target").enumValueIndex = (int)target;
                so.FindProperty("m_selection").enumValueIndex = (int)selection;
                so.FindProperty("m_orientation").enumValueIndex = (int)orientation;
                so.ApplyModifiedPropertiesWithoutUndo();
                gate.ApplyShape();
                Count("ワープゲート");
            }

            // シューターレーン。yaw: 飛ばす向き(0 = 北(+Z))。size: 幅×長さ
            public void ShooterLane(float x, float z, float yaw, Vector2 size)
            {
                var go = Place(AdditionalGimmickBuilder.kShooterLane, new Vector3(x, 0, z));
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var lane = go.GetComponent<ShooterLaneGimmick>();
                var so = new SerializedObject(lane);
                so.FindProperty("m_size").vector2Value = size;
                so.ApplyModifiedPropertiesWithoutUndo();
                lane.ApplyShape();
                Count("シューターレーン");
            }

            // 回転砲台(生成エリアの一種。TumikiSpawner の命令で積み木を撃つ)。yaw: 首振り・固定の基準の向き
            public void Cannon(float x, float z, float yaw, TumikiCannon.RotationMode mode, int minAmount, int maxAmount)
            {
                var go = Place(AdditionalGimmickBuilder.kCannon, new Vector3(x, 0, z));
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var cannon = go.GetComponent<TumikiCannon>();
                var so = new SerializedObject(cannon);
                so.FindProperty("m_rotationMode").enumValueIndex = (int)mode;
                so.FindProperty("m_minAmount").intValue = minAmount;
                so.FindProperty("m_maxAmount").intValue = maxAmount;
                float radius = so.FindProperty("m_radius").floatValue;
                so.ApplyModifiedPropertiesWithoutUndo();
                cannon.ApplyShape();
                Blockers.Add(new Vector3(x, z, radius + 0.1f));
                Count("回転砲台");
            }

            public void AreaRect(float x, float z, float w, float d)
            {
                AddArea(x, z, TumikiSpawnArea.Shape.Rectangle, new Vector2(w, d), 0);
                AreaDesc.Add($"矩形{w}×{d}");
            }

            public void AreaCircle(float x, float z, float r)
            {
                AddArea(x, z, TumikiSpawnArea.Shape.Circle, Vector2.zero, r);
                AreaDesc.Add($"円形 半径{r}");
            }

            void AddArea(float x, float z, TumikiSpawnArea.Shape shape, Vector2 size, float radius)
            {
                var go = Place("TumikiSpawnArea", new Vector3(x, 0, z), Areas);
                go.name = $"SpawnArea_{AreaList.Count + 1}_{shape}";
                var area = go.GetComponent<TumikiSpawnArea>();
                ConfigureArea(area, shape, size, radius);
                AreaList.Add(area);
            }

            public void Spawns(params Vector2[] points) => SpawnPoints.AddRange(points);

            GameObject Place(string prefabName, Vector3 pos, Transform parent = null)
            {
                var prefab = Load<GameObject>($"{kPrefabDir}/{prefabName}.prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Scene);
                go.transform.SetParent(parent != null ? parent : Gimmicks, false);
                go.transform.position = pos;
                return go;
            }

            public bool OnFloor(float x, float z)
            {
                foreach (var r in Floors)
                {
                    if (x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax) return true;
                }
                return false;
            }

            public Rect Bounds()
            {
                Rect b = Floors[0];
                foreach (var r in Floors)
                {
                    b = Rect.MinMaxRect(Mathf.Min(b.xMin, r.xMin), Mathf.Min(b.yMin, r.yMin), Mathf.Max(b.xMax, r.xMax), Mathf.Max(b.yMax, r.yMax));
                }
                return b;
            }
        }

        static void ConfigureArea(TumikiSpawnArea area, TumikiSpawnArea.Shape shape, Vector2 size, float radius)
        {
            var so = new SerializedObject(area);
            so.FindProperty("m_shape").enumValueIndex = (int)shape;
            so.FindProperty("m_size").vector2Value = size;
            so.FindProperty("m_radius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
            area.ApplyShape();
        }

        //========================================
        // 生成エリアだけを写す
        //========================================

        // 定義から作った生成エリア1つ分
        struct AreaSpec
        {
            public string Name;
            public Vector3 Position;
            public Quaternion Rotation;
            public TumikiSpawnArea.Shape Shape;
            public Vector2 Size;
            public float Radius;
        }

        // 定義の積み木の生成エリアを、まとめたシーン(AllStages)と、フォルダにあるステージのシーンへ写す
        // ステージ(床・ギミック・背景など)は作り直さないので、まとめたシーンで手を加えた所はそのまま残る
        [MenuItem("Nakagawa/Stages/Sync Spawn Areas From Definitions (AllStages + Stage Scenes)")]
        public static void SyncSpawnAreas()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (Load<GameObject>($"{kPrefabDir}/TumikiSpawnArea.prefab") == null) GimmickTestFieldBuilder.BuildSpawnAssets();

            // 定義からステージを一時的なシーンに組み立てて、生成エリアと検証の結果を取り出す(保存しない)
            Scene original = SceneManager.GetActiveScene();
            var defs = Definitions();
            var specs = new Dictionary<string, List<AreaSpec>>();
            var report = new StringBuilder();
            foreach (var def in defs)
            {
                specs[def.Id] = BuildAreaSpecs(def, out string validation);
                report.AppendLine($"{def.Id} {def.Title}: 生成エリア{specs[def.Id].Count}か所 / {validation}");
            }
            if (original.IsValid()) SceneManager.SetActiveScene(original);

            // まとめたシーン
            int combinedCount = 0;
            if (File.Exists(kCombinedPath))
            {
                Scene combined = SceneManager.GetSceneByPath(kCombinedPath);
                bool wasLoaded = combined.IsValid() && combined.isLoaded;
                if (!wasLoaded) combined = EditorSceneManager.OpenScene(kCombinedPath, OpenSceneMode.Additive);
                foreach (var root in combined.GetRootGameObjects())
                {
                    var entry = root.GetComponent<StageEntry>();
                    var def = defs.Find(d => d.Id == root.name);
                    if (entry == null || def == null) continue;
                    ApplyAreaSpecs(root.transform, specs[def.Id]);
                    if (entry.Info != null) SetLayout(entry.Info, def);
                    combinedCount++;
                }
                EditorSceneManager.SaveScene(combined);
                if (!wasLoaded) EditorSceneManager.CloseScene(combined, true);
            }

            // ステージのシーン(あるものだけ)
            int sceneCount = 0;
            foreach (var def in defs)
            {
                string path = $"{kStageDir}/{def.Id}.unity";
                if (!File.Exists(path)) continue;
                Scene scene = SceneManager.GetSceneByPath(path);
                bool wasLoaded = scene.IsValid() && scene.isLoaded;
                if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.GetComponent<TumikiSpawner>() != null) ApplyAreaSpecs(root.transform, specs[def.Id]);
                    if (root.TryGetComponent<StageInfo>(out var info)) SetLayout(info, def);
                }
                EditorSceneManager.SaveScene(scene);
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
                sceneCount++;
            }

            WriteDocument(defs);
            AssetDatabase.Refresh();
            Debug.Log($"[StageBuilder] 生成エリアを写しました(AllStages {combinedCount}ステージ / ステージのシーン {sceneCount}個)\n" + report);
        }

        static void SetLayout(StageInfo info, StageDef def)
        {
            info.Set(def.Id, def.Title, def.Concept, def.Flow, def.Layout);
            EditorUtility.SetDirty(info);
        }

        // 定義どおりにステージを一時的なシーンに組み立て、生成エリアを取り出す
        static List<AreaSpec> BuildAreaSpecs(StageDef def, out string validation)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var list = new List<AreaSpec>();
            try
            {
                var c = new Ctx { Scene = scene };
                c.Stage = new GameObject("Stage").transform;
                c.Gimmicks = new GameObject("Gimmicks").transform;
                c.Areas = new GameObject("TumikiSpawner").transform;
                def.Build(c);
                def.Layout = Layout(c, c.Bounds(), def.MaxBlocks);
                validation = Validate(c);
                foreach (var area in c.AreaList)
                {
                    var so = new SerializedObject(area);
                    list.Add(new AreaSpec
                    {
                        Name = area.name,
                        Position = area.transform.position,
                        Rotation = area.transform.rotation,
                        Shape = (TumikiSpawnArea.Shape)so.FindProperty("m_shape").enumValueIndex,
                        Size = so.FindProperty("m_size").vector2Value,
                        Radius = so.FindProperty("m_radius").floatValue,
                    });
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            return list;
        }

        // stageRoot の下の生成エリアを、specs に入れ替える(TumikiSpawner の子に置く)
        static void ApplyAreaSpecs(Transform stageRoot, List<AreaSpec> specs)
        {
            var spawner = stageRoot.GetComponentInChildren<TumikiSpawner>(true);
            Transform parent = spawner != null ? spawner.transform : stageRoot;
            foreach (var old in stageRoot.GetComponentsInChildren<TumikiSpawnArea>(true)) Object.DestroyImmediate(old.gameObject);

            var prefab = Load<GameObject>($"{kPrefabDir}/TumikiSpawnArea.prefab");
            foreach (var spec in specs)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stageRoot.gameObject.scene);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(spec.Position, spec.Rotation);
                go.name = spec.Name;
                ConfigureArea(go.GetComponent<TumikiSpawnArea>(), spec.Shape, spec.Size, spec.Radius);
            }
        }

        // ステージ1つ分を、新しい一時的なシーン(保存しない)に組み立てる。シーンを閉じるのは呼んだ側
        // (StageCombiner がまとめたシーンへ移してから閉じる)
        static Scene BuildStageScene(StageDef def, out string report)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var c = new Ctx { Scene = scene };
            StageBackdrop backdrop = BackdropBuilder.LoadOrBuild(def.Backdrop);
            if (backdrop != null)
            {
                c.FloorMaterial = backdrop.FloorMaterial;
                c.WallMaterial = backdrop.WallMaterial;
            }
            c.Stage = new GameObject("Stage").transform;
            c.Gimmicks = new GameObject("Gimmicks").transform;
            var spawnerGo = new GameObject("TumikiSpawner");
            c.Areas = spawnerGo.transform;
            def.Build(c);
            // 重なった床・壁の同じ平面の面はちらつくので、重ならないように分ける・縮める
            StageGeometryFixer.Fix(c.Stage);

            Rect bounds = c.Bounds();
            Vector3 center = new Vector3(bounds.center.x, 0, bounds.center.y);

            // 奈落の見た目(床の穴や縁の外が真っ暗に見えるように。当たり判定なし)
            var abyss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            abyss.name = "Abyss";
            Object.DestroyImmediate(abyss.GetComponent<Collider>());
            abyss.transform.SetParent(c.Stage, false);
            abyss.transform.position = new Vector3(center.x, -12.0f, center.z);
            abyss.transform.localScale = new Vector3(bounds.width + 80, 0.1f, bounds.height + 80);
            abyss.GetComponent<Renderer>().sharedMaterial = AbyssMaterial();

            var stageArea = new GameObject("StageArea").AddComponent<StageArea>();
            stageArea.transform.position = center;
            var saSo = new SerializedObject(stageArea);
            saSo.FindProperty("m_size").vector2Value = bounds.size;
            saSo.ApplyModifiedPropertiesWithoutUndo();

            // 背景(当たり判定のない飾り)。まとめたシーンでは、このステージの子に移される
            if (backdrop != null) BackdropBuilder.Place(scene, null, backdrop, center, bounds.size, c.Stage);

            var spawnRoot = new GameObject("SpawnPoints").transform;
            var spawns = new Transform[c.SpawnPoints.Count];
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = new GameObject($"SpawnPoint{i + 1}").transform;
                spawns[i].SetParent(spawnRoot, false);
                spawns[i].position = new Vector3(c.SpawnPoints[i].x, 0, c.SpawnPoints[i].y);
            }

            DarumaMatch match = BuildMatch(spawns, stageArea);
            SetupSpawner(spawnerGo.AddComponent<TumikiSpawner>(), c.AreaList, def.MaxBlocks, match);

            BuildLightAndCamera(bounds);
            SetupCameraAndDebugTools(scene);

            string layout = Layout(c, bounds, def.MaxBlocks);
            def.Layout = layout;
            new GameObject("StageInfo").AddComponent<StageInfo>().Set(def.Id, def.Title, def.Concept, def.Flow, layout);

            report = $"{def.Id} {def.Title}: {Validate(c)}";
            return scene;
        }

        static DarumaMatch BuildMatch(Transform[] spawns, StageArea stageArea)
        {
            var go = new GameObject("Match");
            go.SetActive(false);

            var pim = go.AddComponent<PlayerInputManager>();
            var pimSo = new SerializedObject(pim);
            pimSo.FindProperty("m_NotificationBehavior").intValue = 3;
            pimSo.FindProperty("m_JoinBehavior").intValue = 0;
            pimSo.FindProperty("m_MaxPlayerCount").intValue = -1;
            pimSo.FindProperty("m_PlayerPrefab").objectReferenceValue = Load<GameObject>(kPlayerPrefab);
            pimSo.ApplyModifiedPropertiesWithoutUndo();

            var match = go.AddComponent<DarumaMatch>();
            var so = new SerializedObject(match);
            var points = so.FindProperty("m_spawnPoints");
            points.arraySize = spawns.Length;
            for (int i = 0; i < spawns.Length; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = spawns[i];
            so.FindProperty("m_stageArea").objectReferenceValue = stageArea;
            so.FindProperty("m_characterPrefab").objectReferenceValue = Load<GameObject>(kCharacterPrefab).GetComponent<DarumaCharacter>();
            so.FindProperty("m_ghostPrefab").objectReferenceValue = Load<GameObject>(kGhostPrefab).GetComponent<DarumaGhost>();
            so.FindProperty("m_tumikiPrefab").objectReferenceValue = Load<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
            so.FindProperty("m_dummyCount").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(true);
            return match;
        }

        static void SetupSpawner(TumikiSpawner spawner, List<TumikiSpawnArea> areas, int maxBlocks, DarumaMatch match)
        {
            var so = new SerializedObject(spawner);
            so.FindProperty("m_tumikiPrefab").objectReferenceValue = Load<GameObject>(kTumikiPrefab).GetComponent<TumikiBlock>();
            var patterns = so.FindProperty("m_patterns");
            patterns.arraySize = 2;
            patterns.GetArrayElementAtIndex(0).objectReferenceValue = Load<TumikiSpawnPattern>(kRandomPatternPath);
            patterns.GetArrayElementAtIndex(1).objectReferenceValue = Load<TumikiSpawnPattern>(kEqualPatternPath);
            // 生成エリアは TumikiSpawner がゲーム開始時に盤面から探す
            so.FindProperty("m_maxBlocksOnBoard").intValue = maxBlocks;
            so.FindProperty("m_match").objectReferenceValue = match;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ステージ全体が収まる見下ろしのカメラ
        static void BuildLightAndCamera(Rect bounds)
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.fieldOfView = 45.0f;
            Quaternion rot = Quaternion.Euler(55.0f, 0.0f, 0.0f);
            Vector3 target = new Vector3(bounds.center.x, 0.0f, bounds.center.y);

            // 16:9の画面で、ステージの四隅(床と壁の上端)がすべて余白付きで映るまで引く
            camera.aspect = 16.0f / 9.0f;
            var corners = new List<Vector3>();
            foreach (float y in new[] { 0.0f, kWallHeight })
            {
                corners.Add(new Vector3(bounds.xMin, y, bounds.yMin));
                corners.Add(new Vector3(bounds.xMax, y, bounds.yMin));
                corners.Add(new Vector3(bounds.xMin, y, bounds.yMax));
                corners.Add(new Vector3(bounds.xMax, y, bounds.yMax));
            }
            const float kMargin = 0.04f;
            for (float distance = 10.0f; distance < 200.0f; distance += 0.5f)
            {
                camera.transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
                bool fits = true;
                foreach (var p in corners)
                {
                    Vector3 v = camera.WorldToViewportPoint(p);
                    if (v.z <= 0 || v.x < kMargin || v.x > 1 - kMargin || v.y < kMargin || v.y > 1 - kMargin) { fits = false; break; }
                }
                if (fits) break;
            }
            camera.ResetAspect();
        }

        // カメラの演出(プレイヤーを追う・揺れる・壁を透過する)、壁の透過用マテリアル、デバッグ用の切り替え(積み木の衝突の振る舞い)を付ける
        // 付いていれば何もしない
        public static void SetupCameraAndDebugTools(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var camera = root.GetComponent<Camera>();
                if (camera == null || !root.CompareTag("MainCamera")) continue;
                if (root.GetComponent<MatchCamera>() == null) root.AddComponent<MatchCamera>();
                if (root.GetComponent<CameraShake>() == null) root.AddComponent<CameraShake>();
                if (root.GetComponent<SeeThroughTargets>() == null) root.AddComponent<SeeThroughTargets>();
                var cinematic = root.GetComponent<EliminationCinematic>();
                if (cinematic == null) cinematic = root.AddComponent<EliminationCinematic>();
                var cinematicSo = new SerializedObject(cinematic);
                if (cinematicSo.FindProperty("m_effectPrefab").objectReferenceValue == null)
                {
                    var prefab = Load<GameObject>(EffectBuilder.kEliminationEffectPath);
                    if (prefab == null) prefab = EffectBuilder.BuildEliminationEffect();
                    cinematicSo.FindProperty("m_effectPrefab").objectReferenceValue = prefab;
                    cinematicSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // 壁(ReflectWall)の元のマテリアルを、裏が透けて見えるマテリアルに置き換える
            var wallMaterial = Load<Material>(kWallMaterial);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var wall in root.GetComponentsInChildren<ReflectWall>(true))
                {
                    var r = wall.GetComponent<Renderer>();
                    if (r != null && r.sharedMaterial == wallMaterial) r.sharedMaterial = SeeThroughWallMaterial();
                }
            }

            // デバッグ用の切り替え。モードが未設定なら既定の並びを入れる
            var toggle = FindInScene<TumikiImpactModeToggle>(scene);
            if (toggle == null)
            {
                var go = new GameObject("DebugTools");
                SceneManager.MoveGameObjectToScene(go, scene);
                toggle = go.AddComponent<TumikiImpactModeToggle>();
            }
            SetupImpactModes(toggle);
            // 出現位置のデバッグ表示
            if (toggle.GetComponent<SpawnPointDebugView>() == null) toggle.gameObject.AddComponent<SpawnPointDebugView>();
            // ステージをまとめたシーンでは、再生中にステージごとのカメラを調整・保存できるようにする
            var switcher = FindInScene<StageSwitcher>(scene);
            if (switcher != null)
            {
                if (toggle.GetComponent<StageCameraTuner>() == null) toggle.gameObject.AddComponent<StageCameraTuner>();
                var switcherSo = new SerializedObject(switcher);
                var presetsProp = switcherSo.FindProperty("m_cameraPresets");
                if (presetsProp.objectReferenceValue == null)
                {
                    presetsProp.objectReferenceValue = StageCombiner.LoadOrCreateCameraPresets();
                    switcherSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // 画面下の表示(ストック・点数・撃墜数)は試合と同じ GameObject に付ける
            var match = FindInScene<DarumaMatch>(scene);
            if (match != null && match.GetComponent<MatchHud>() == null) match.gameObject.AddComponent<MatchHud>();

            // 飛んできた積み木に乗ったときの成功エフェクト(エフェクトのプレハブが未設定なら設定する)
            var effect = FindInScene<TumikiCatchEffect>(scene);
            if (effect == null)
            {
                var go = new GameObject("Effects");
                SceneManager.MoveGameObjectToScene(go, scene);
                effect = go.AddComponent<TumikiCatchEffect>();
            }
            var effectSo = new SerializedObject(effect);
            if (effectSo.FindProperty("m_particleMaterial").objectReferenceValue == null)
            {
                effectSo.FindProperty("m_particleMaterial").objectReferenceValue = Load<Material>(kParticleMaterial);
            }
            if (effectSo.FindProperty("m_effectPrefab").objectReferenceValue == null)
            {
                var prefab = Load<GameObject>(EffectBuilder.kCatchEffectPath);
                if (prefab == null) prefab = EffectBuilder.BuildCatchEffect();
                effectSo.FindProperty("m_effectPrefab").objectReferenceValue = prefab;
                // 派手なエフェクトに合わせて、カメラも少し揺らす
                effectSo.FindProperty("m_shakeCamera").boolValue = true;
            }
            effectSo.ApplyModifiedPropertiesWithoutUndo();
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var c = root.GetComponentInChildren<T>(true);
                if (c != null) return c;
            }
            return null;
        }

        // M キーで切り替えるモード: 1回ヒットで破壊 → 2回 → 3回 → 非破壊(すでに設定されていれば何もしない)
        static void SetupImpactModes(TumikiImpactModeToggle toggle)
        {
            var so = new SerializedObject(toggle);
            var modes = so.FindProperty("m_modeList");
            if (modes.arraySize > 0) return;

            var impact = DurabilityImpactAsset();
            var list = new (string name, int durability)[]
            {
                ("1回ヒットで破壊", 1),
                ("2回ヒットで破壊", 2),
                ("3回ヒットで破壊", 3),
                ("非破壊", 0),
            };
            modes.arraySize = list.Length;
            for (int i = 0; i < list.Length; i++)
            {
                var m = modes.GetArrayElementAtIndex(i);
                m.FindPropertyRelative("Name").stringValue = list[i].name;
                m.FindPropertyRelative("Impact").objectReferenceValue = impact;
                m.FindPropertyRelative("Durability").intValue = list[i].durability;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 積み木のプレハブに耐久度(ヒビの見た目つき)を付ける。付いていれば何もしない
        [MenuItem("Nakagawa/Tumiki/Setup Tumiki Prefab")]
        public static void SetupTumikiPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(kTumikiPrefab);
            try
            {
                bool changed = false;
                if (root.GetComponent<TumikiEdgeSlip>() == null)
                {
                    root.AddComponent<TumikiEdgeSlip>();
                    changed = true;
                }
                var durability = root.GetComponent<TumikiDurability>();
                if (durability == null)
                {
                    durability = root.AddComponent<TumikiDurability>();
                    var so = new SerializedObject(durability);
                    so.FindProperty("m_crackMaterial").objectReferenceValue = CrackMaterial();
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
                // 損傷の見た目(シェーダー)
                var dso = new SerializedObject(durability);
                var damage = dso.FindProperty("m_damageMaterial");
                if (damage.objectReferenceValue == null)
                {
                    damage.objectReferenceValue = DamageMaterial();
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
                if (!changed) return;
                PrefabUtility.SaveAsPrefabAsset(root, kTumikiPrefab);
                Debug.Log("[StageBuilder] 積み木のプレハブに耐久度(損傷の見た目)・縁から落ちる処理を付けました");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 積み木の損傷の見た目(ヒビ・汚れ・欠けを重ねて描くシェーダー)
        static Material DamageMaterial()
        {
            var mat = Load<Material>(kDamageMaterial);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Nakagawa/TumikiDamage"));
            EnsureFolder(kMaterialDir);
            AssetDatabase.CreateAsset(mat, kDamageMaterial);
            return mat;
        }

        // ヒビの色(光の影響を受けない暗い色)
        static Material CrackMaterial()
        {
            var mat = Load<Material>(kCrackMaterial);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.08f, 0.06f, 0.05f, 1.0f));
            EnsureFolder(kMaterialDir);
            AssetDatabase.CreateAsset(mat, kCrackMaterial);
            return mat;
        }

        static TumikiImpactAsset DurabilityImpactAsset()
        {
            var asset = Load<TumikiDurabilityImpact>(kDurabilityImpactPath);
            if (asset != null) return asset;
            EnsureFolder(Path.GetDirectoryName(kDurabilityImpactPath).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<TumikiDurabilityImpact>();
            var so = new SerializedObject(asset);
            so.FindProperty("m_displayName").stringValue = "耐久度つき";
            so.FindProperty("m_onSurvive").objectReferenceValue = BounceImpactAsset();
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(asset, kDurabilityImpactPath);
            return asset;
        }

        // 既存のステージのシーンに、カメラの演出・壁の透過・デバッグ用の切り替えを付ける(ステージは作り直さない)
        [MenuItem("Nakagawa/Stages/Update Existing Stages (Camera, Walls, Debug)")]
        public static void UpdateExistingStages()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SetupTumikiPrefab();
            Scene original = SceneManager.GetActiveScene();
            int count = 0;
            foreach (var file in Directory.GetFiles(kStageDir, "Stage*.unity"))
            {
                string path = file.Replace('\\', '/');
                Scene loaded = SceneManager.GetSceneByPath(path);
                bool wasLoaded = loaded.IsValid() && loaded.isLoaded;
                Scene scene = wasLoaded ? loaded : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SetupCameraAndDebugTools(scene);
                EditorSceneManager.SaveScene(scene);
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
                count++;
            }
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            Debug.Log($"[StageBuilder] {count}ステージにカメラの演出・壁の透過・デバッグ用の切り替えを付けました");
        }

        // 裏が透けて見える壁のマテリアル(色などは元の壁のマテリアルから写す)
        static Material SeeThroughWallMaterial()
        {
            var mat = Load<Material>(kSeeThroughWallMaterial);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Nakagawa/SeeThroughWall"));
            var src = Load<Material>(kWallMaterial);
            if (src != null)
            {
                foreach (var prop in new[] { "_BaseColor", "_Smoothness", "_Metallic" })
                {
                    if (!src.HasProperty(prop)) continue;
                    if (prop == "_BaseColor") mat.SetColor(prop, src.GetColor(prop));
                    else mat.SetFloat(prop, src.GetFloat(prop));
                }
                if (src.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            }
            EnsureFolder(kMaterialDir);
            AssetDatabase.CreateAsset(mat, kSeeThroughWallMaterial);
            return mat;
        }

        static TumikiImpactAsset BounceImpactAsset()
        {
            var asset = Load<TumikiBounceImpact>(kBounceImpactPath);
            if (asset != null) return asset;
            EnsureFolder(Path.GetDirectoryName(kBounceImpactPath).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<TumikiBounceImpact>();
            var so = new SerializedObject(asset);
            so.FindProperty("m_displayName").stringValue = "非破壊(跳ね返る)";
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(asset, kBounceImpactPath);
            return asset;
        }

        //========================================
        // 検証・説明
        //========================================

        static string Validate(Ctx c)
        {
            var problems = new List<string>();
            if (c.SpawnPoints.Count != 4) problems.Add($"出現位置が{c.SpawnPoints.Count}か所");
            if (c.AreaList.Count < kMinSpawnAreas) problems.Add($"生成エリアが{c.AreaList.Count}か所({kMinSpawnAreas}か所以上にする)");
            if (c.GimmickTypes.Count > c.MaxGimmickTypes) problems.Add($"ギミックが{c.GimmickTypes.Count}種類({c.MaxGimmickTypes}種類まで)");

            // 出現位置: 床の上で、ギミック・生成エリアと重ならない
            foreach (var p in c.SpawnPoints)
            {
                float r = DarumaCharacter.kBodyRadius;
                if (!CircleOnFloor(c, p, r)) problems.Add($"出現位置{p}が床からはみ出している");
                foreach (var b in c.Blockers)
                {
                    if (Vector2.Distance(p, new Vector2(b.x, b.y)) < b.z + r) problems.Add($"出現位置{p}がギミックと重なる");
                }
                foreach (var area in c.AreaList)
                {
                    if (area.Contains(new Vector3(p.x, 0, p.y), -r)) problems.Add($"出現位置{p}が生成エリア{area.name}の中");
                }
            }

            // 回転台: 円全体が床の上(台には当たり判定がないので、床がないと落ちる)
            foreach (var t in c.Tables)
            {
                if (!CircleOnFloor(c, new Vector2(t.x, t.y), t.z)) problems.Add($"回転台({t.x},{t.y})が床からはみ出している");
            }

            // 生成エリア: 積み木を置ける場所の割合(床の上で、ギミックと重ならない)
            var capacity = new List<string>();
            foreach (var area in c.AreaList)
            {
                int total = 0, ok = 0;
                Vector3 o = area.transform.position;
                for (float x = -16; x <= 16; x += 0.5f)
                {
                    for (float z = -16; z <= 16; z += 0.5f)
                    {
                        var wp = new Vector3(o.x + x, 0, o.z + z);
                        if (!area.Contains(wp, TumikiBlock.kRadius)) continue;
                        total++;
                        var p = new Vector2(wp.x, wp.z);
                        if (!CircleOnFloor(c, p, TumikiBlock.kRadius)) continue;
                        bool blocked = false;
                        foreach (var b in c.Blockers)
                        {
                            if (Vector2.Distance(p, new Vector2(b.x, b.y)) < b.z + TumikiBlock.kRadius + 0.1f) blocked = true;
                        }
                        if (!blocked) ok++;
                    }
                }
                float ratio = total > 0 ? (float)ok / total : 0.0f;
                capacity.Add($"{ratio:P0}");
                // エリアは床の外・ギミックにかぶせて置いてよい(床の上で空いている所にだけ湧く)。ほとんど置けないものだけ知らせる
                if (ratio < kMinSpawnCapacity) problems.Add($"生成エリア{area.name}で置ける場所が{ratio:P0}しかない");
            }

            string result = problems.Count == 0 ? "OK" : "要確認: " + string.Join(" / ", problems);
            return $"{result} (生成エリアの置ける割合 {string.Join(", ", capacity)})";
        }

        // 円の中心と外周8点がすべて床の上か
        static bool CircleOnFloor(Ctx c, Vector2 p, float r)
        {
            if (!c.OnFloor(p.x, p.y)) return false;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                if (!c.OnFloor(p.x + Mathf.Cos(a) * r, p.y + Mathf.Sin(a) * r)) return false;
            }
            return true;
        }

        // 説明の「ギミック」の欄に並べる順
        static readonly string[] kGimmickOrder =
        {
            "扇風機", "バンパー", "回転台", "せり上がり塀", "加速床", "減速床", "移動床", "ワープゲート", "シューターレーン", "回転砲台",
        };

        static string Layout(Ctx c, Rect bounds, int maxBlocks)
        {
            // 決まった順に並べ、順番の決まっていない種類は後ろに足す
            var gimmicks = new List<string>();
            var order = new List<string>(kGimmickOrder);
            foreach (var name in c.GimmickCounts.Keys) if (!order.Contains(name)) order.Add(name);
            foreach (var name in order)
            {
                if (c.GimmickCounts.TryGetValue(name, out int n) && n > 0) gimmicks.Add($"{name}×{n}");
            }
            return $"広さ: {bounds.width}×{bounds.height}m\n" +
                   $"ギミック: {(gimmicks.Count == 0 ? "なし" : string.Join("、", gimmicks))}\n" +
                   $"生成エリア: {c.AreaList.Count}か所({string.Join("、", c.AreaDesc)})\n" +
                   $"盤面の積み木の上限: {maxBlocks}個";
        }

        // 今回作ったステージ(Layout のあるもの)は書き直し、作らなかったステージは前の一覧の内容を残す
        static void WriteDocument(List<StageDef> defs)
        {
            ReadExistingDocument(out var oldRows, out var oldSections);

            var sb = new StringBuilder();
            sb.AppendLine("# ステージ一覧");
            sb.AppendLine();
            sb.AppendLine("`Nakagawa > Stages > Build All Stages` で生成したステージの一覧です。定義は `Assets/Nakagawa/Editor/StageBuilder.cs` にあります。");
            sb.AppendLine("各シーンの `StageInfo` オブジェクトにも同じ内容が入っています。");
            sb.AppendLine();
            sb.AppendLine("| No. | 名前 | ギミック | 生成エリア | 背景 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var d in defs)
            {
                if (d.Layout == null)
                {
                    if (oldRows.TryGetValue(d.Id, out var row)) sb.AppendLine(row);
                    continue;
                }
                string[] lines = d.Layout.Split('\n');
                sb.AppendLine($"| {d.Id} | {d.Title} | {lines[1].Replace("ギミック: ", "")} | {lines[2].Replace("生成エリア: ", "")} | {(string.IsNullOrEmpty(d.Backdrop) ? "なし" : d.Backdrop)} |");
            }
            sb.AppendLine();
            foreach (var d in defs)
            {
                if (d.Layout == null)
                {
                    if (oldSections.TryGetValue(d.Id, out var section)) sb.Append(section);
                    continue;
                }
                sb.AppendLine($"## {d.Id} {d.Title}");
                sb.AppendLine();
                foreach (var line in d.Layout.Split('\n')) sb.AppendLine($"- {line}");
                if (!string.IsNullOrEmpty(d.Backdrop)) sb.AppendLine($"- 背景: {d.Backdrop}");
                sb.AppendLine();
                sb.AppendLine($"**コンセプト**: {d.Concept}");
                sb.AppendLine();
                sb.AppendLine($"**予想される試合展開**: {d.Flow}");
                sb.AppendLine();
            }
            File.WriteAllText(kDocPath, sb.ToString(), new UTF8Encoding(false));
        }

        // 前の一覧から、ステージごとの表の行と説明(## から次の ## の前まで)を読む
        static void ReadExistingDocument(out Dictionary<string, string> rows, out Dictionary<string, string> sections)
        {
            rows = new Dictionary<string, string>();
            sections = new Dictionary<string, string>();
            if (!File.Exists(kDocPath)) return;

            string currentId = null;
            var current = new StringBuilder();
            foreach (var raw in File.ReadAllLines(kDocPath))
            {
                string line = raw.TrimEnd();
                // ステージの行(見出しの行・区切りの行は除く)
                if (line.StartsWith("| ") && !line.StartsWith("| No.") && !line.StartsWith("|---"))
                {
                    string[] cells = line.Split('|');
                    string id = cells[1].Trim();
                    // 背景の列がない古い行には足す
                    rows[id] = cells.Length >= 7 ? line : line + " なし |";
                    continue;
                }
                if (line.StartsWith("## "))
                {
                    if (currentId != null) sections[currentId] = current.ToString();
                    currentId = line.Substring(3).Split(' ')[0];
                    current.Clear();
                }
                if (currentId != null) current.AppendLine(line);
            }
            if (currentId != null) sections[currentId] = current.ToString();
        }

        //========================================
        // 補助
        //========================================

        static Material AbyssMaterial()
        {
            string path = $"{kMaterialDir}/Abyss.mat";
            var mat = Load<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Load<Material>(kStageMaterial));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.09f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.05f, 0.05f, 0.09f));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
