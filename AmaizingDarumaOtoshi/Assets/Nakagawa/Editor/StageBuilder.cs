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
    // 対戦ステージ(Stage01〜20)を Assets/Nakagawa/Stages に生成する
    // 各ステージはこのファイルの定義(床・壁・ギミック・積み木の生成エリア・出現位置・説明)から作るので、数値を直して作り直せる
    // 床はすべて同じ高さ(段差なし)。床の穴・壁のない縁は奈落(落ちると脱落)
    // 開いているシーンには触れない(別のシーンを追加で開いて組み立て、保存して閉じる)
    public static class StageBuilder
    {
        const string kStageDir = "Assets/Nakagawa/Stages";
        const string kDocPath = kStageDir + "/StageList.md";
        const string kPrefabDir = "Assets/Nakagawa/Prefabs";
        const string kMaterialDir = "Assets/Nakagawa/Materials";
        const string kRandomPatternPath = "Assets/Nakagawa/SpawnPatterns/SpawnPattern_Random.asset";
        const string kEqualPatternPath = "Assets/Nakagawa/SpawnPatterns/SpawnPattern_Equal.asset";

        const string kNakahira = "Assets/Nakahira/Player";
        const string kCharacterPrefab = kNakahira + "/Prefabs/DarumaCharacter.prefab";
        const string kGhostPrefab = kNakahira + "/Prefabs/DarumaGhost.prefab";
        const string kPlayerPrefab = kNakahira + "/Prefabs/DarumaPlayer.prefab";
        const string kTumikiPrefab = kNakahira + "/Prefabs/Tumiki.prefab";
        const string kStageMaterial = kNakahira + "/Materials/Stage.mat";
        const string kWallMaterial = kNakahira + "/Materials/Wall.mat";

        const float kWallHeight = 1.5f;
        const float kBumperBaseRadius = 0.8f;   // Bumper.prefab の円盤の半径(スケール1のとき)

        [MenuItem("Nakagawa/Stages/Build All Stages")]
        public static void BuildAll()
        {
            if (Load<GameObject>($"{kPrefabDir}/TumikiSpawnArea.prefab") == null || Load<TumikiSpawnPattern>(kRandomPatternPath) == null)
            {
                GimmickTestFieldBuilder.BuildSpawnAssets();
            }
            EnsureFolder(kStageDir);

            Scene original = SceneManager.GetActiveScene();
            var report = new StringBuilder();
            var defs = Definitions();
            foreach (var def in defs)
            {
                report.AppendLine(BuildStage(def));
            }
            if (original.IsValid()) SceneManager.SetActiveScene(original);

            WriteDocument(defs);
            AssetDatabase.Refresh();
            Debug.Log("[StageBuilder] 20ステージを作成しました\n" + report);
        }

        //========================================
        // ステージの定義
        //========================================

        class StageDef
        {
            public string Id, Title, Concept, Flow;
            public int MaxBlocks = 24;
            public Action<Ctx> Build;
            public string Layout; // 生成後に埋める
        }

        static List<StageDef> Definitions()
        {
            var list = new List<StageDef>();
            void Add(string title, int maxBlocks, string concept, string flow, Action<Ctx> build)
            {
                list.Add(new StageDef { Id = $"Stage{list.Count + 1:00}", Title = title, MaxBlocks = maxBlocks, Concept = concept, Flow = flow, Build = build });
            }

            Add("基本の闘技場", 24,
                "ギミックなしの正方形の闘技場。全ステージの基準となる、操作と駆け引きの基本だけで戦う舞台。",
                "中央の補給所に積み木が湧くたびに全員が中央へ集まり、拾い合いと打ち返しの乱戦になる。四方の壁で弾が跳ね返るので、中央で戦いながら壁からの跳弾にも気を配る展開。積み木を多く抱えた人ほど遅くなるため、序盤に積みすぎた人が狙われやすい。",
                c =>
                {
                    c.Arena(0, 0, 24, 24, true, true, true, true);
                    c.AreaCircle(0, 0, 4);
                    c.Spawns(new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8));
                });

            Add("四隅の補給所", 28,
                "四隅に補給所がある闘技場。出現位置は各辺の中央で、どの補給所にも同じ距離。",
                "序盤は最寄りの補給所に分かれて積み木を集める静かな立ち上がり。補給所の中に立っているとそのエリアには湧かないので、居座りは損になり、自然と隣の補給所へ遠征する流れが生まれる。遠征中の背中を狙う撃ち合いが中盤の山場。",
                c =>
                {
                    c.Arena(0, 0, 26, 26, true, true, true, true);
                    c.AreaRect(-9, -9, 5, 5);
                    c.AreaRect(9, -9, 5, 5);
                    c.AreaRect(-9, 9, 5, 5);
                    c.AreaRect(9, 9, 5, 5);
                    c.Spawns(new Vector2(0, -10), new Vector2(0, 10), new Vector2(-10, 0), new Vector2(10, 0));
                });

            Add("奈落の舞台", 20,
                "壁のない小さな正方形の舞台。縁から落ちれば即脱落。",
                "打ち返された積み木に押し出されるより、避けた拍子に落ちる事故が怖いステージ。全員が中央に寄りたがるので、中央の補給所を巡る近距離戦になる。弾も場外へ消えていくので積み木は少なめで推移し、一つひとつの積み木の価値が高い。",
                c =>
                {
                    c.Arena(0, 0, 22, 22, false, false, false, false);
                    c.AreaCircle(0, 0, 5);
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
                    c.AreaRect(0, -11.5f, 12, 4);
                    c.AreaRect(0, 11.5f, 12, 4);
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
                    c.AreaRect(0, 8, 5, 2.4f);
                    c.AreaRect(0, -8, 5, 2.4f);
                    c.AreaRect(8, 0, 2.4f, 5);
                    c.AreaRect(-8, 0, 2.4f, 5);
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
                    c.AreaRect(12.5f, 0, 4, 14);
                    c.AreaRect(-12.5f, 0, 4, 14);
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
                    c.AreaRect(0, 10.5f, 8, 4);
                    c.AreaRect(0, -10.5f, 8, 4);
                    c.AreaRect(10.5f, 0, 4, 8);
                    c.AreaRect(-10.5f, 0, 4, 8);
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
                    c.Spawns(new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8), new Vector2(8, 8));
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
            public int Bumpers, Fans, Turntables;
            int m_fanIndex;

            public void Floor(float cx, float cz, float w, float d)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Floor_{Floors.Count}";
                go.transform.SetParent(Stage, false);
                go.transform.localPosition = new Vector3(cx, -0.5f, cz);
                go.transform.localScale = new Vector3(w, 1.0f, d);
                go.GetComponent<Renderer>().sharedMaterial = Load<Material>(kStageMaterial);
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
                go.GetComponent<Renderer>().sharedMaterial = Load<Material>(kWallMaterial);
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
                GimmickTypes.Add("バンパー");
                Bumpers++;
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
                GimmickTypes.Add("扇風機");
                Fans++;
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
                GimmickTypes.Add("回転台");
                Turntables++;
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
                var so = new SerializedObject(area);
                so.FindProperty("m_shape").enumValueIndex = (int)shape;
                so.FindProperty("m_size").vector2Value = size;
                so.FindProperty("m_radius").floatValue = radius;
                so.ApplyModifiedPropertiesWithoutUndo();
                area.ApplyShape();
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

        static string BuildStage(StageDef def)
        {
            string path = $"{kStageDir}/{def.Id}.unity";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var c = new Ctx { Scene = scene };
            c.Stage = new GameObject("Stage").transform;
            c.Gimmicks = new GameObject("Gimmicks").transform;
            var spawnerGo = new GameObject("TumikiSpawner");
            c.Areas = spawnerGo.transform;
            def.Build(c);

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

            string layout = Layout(c, bounds, def.MaxBlocks);
            def.Layout = layout;
            new GameObject("StageInfo").AddComponent<StageInfo>().Set(def.Id, def.Title, def.Concept, def.Flow, layout);

            string validation = Validate(c);
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            return $"{def.Id} {def.Title}: {validation}";
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
            var areaProp = so.FindProperty("m_areas");
            areaProp.arraySize = areas.Count;
            for (int i = 0; i < areas.Count; i++) areaProp.GetArrayElementAtIndex(i).objectReferenceValue = areas[i];
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

        //========================================
        // 検証・説明
        //========================================

        static string Validate(Ctx c)
        {
            var problems = new List<string>();
            if (c.SpawnPoints.Count != 4) problems.Add($"出現位置が{c.SpawnPoints.Count}か所");
            if (c.AreaList.Count == 0) problems.Add("生成エリアがない");
            if (c.GimmickTypes.Count > 2) problems.Add("ギミックが3種類以上");

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
                if (ratio < 0.5f) problems.Add($"生成エリア{area.name}で置ける場所が{ratio:P0}しかない");
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

        static string Layout(Ctx c, Rect bounds, int maxBlocks)
        {
            var gimmicks = new List<string>();
            if (c.Fans > 0) gimmicks.Add($"扇風機×{c.Fans}");
            if (c.Bumpers > 0) gimmicks.Add($"バンパー×{c.Bumpers}");
            if (c.Turntables > 0) gimmicks.Add($"回転台×{c.Turntables}");
            return $"広さ: {bounds.width}×{bounds.height}m\n" +
                   $"ギミック: {(gimmicks.Count == 0 ? "なし" : string.Join("、", gimmicks))}\n" +
                   $"生成エリア: {c.AreaList.Count}か所({string.Join("、", c.AreaDesc)})\n" +
                   $"盤面の積み木の上限: {maxBlocks}個";
        }

        static void WriteDocument(List<StageDef> defs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ステージ一覧");
            sb.AppendLine();
            sb.AppendLine("`Nakagawa > Stages > Build All Stages` で生成したステージの一覧です。定義は `Assets/Nakagawa/Editor/StageBuilder.cs` にあります。");
            sb.AppendLine("各シーンの `StageInfo` オブジェクトにも同じ内容が入っています。");
            sb.AppendLine();
            sb.AppendLine("| No. | 名前 | ギミック | 生成エリア |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var d in defs)
            {
                string[] lines = d.Layout.Split('\n');
                sb.AppendLine($"| {d.Id} | {d.Title} | {lines[1].Replace("ギミック: ", "")} | {lines[2].Replace("生成エリア: ", "")} |");
            }
            sb.AppendLine();
            foreach (var d in defs)
            {
                sb.AppendLine($"## {d.Id} {d.Title}");
                sb.AppendLine();
                foreach (var line in d.Layout.Split('\n')) sb.AppendLine($"- {line}");
                sb.AppendLine();
                sb.AppendLine($"**コンセプト**: {d.Concept}");
                sb.AppendLine();
                sb.AppendLine($"**予想される試合展開**: {d.Flow}");
                sb.AppendLine();
            }
            File.WriteAllText(kDocPath, sb.ToString(), new UTF8Encoding(false));
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
