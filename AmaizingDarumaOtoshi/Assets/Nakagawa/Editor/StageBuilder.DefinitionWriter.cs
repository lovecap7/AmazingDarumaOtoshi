using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Nakahira;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nakagawa.EditorTools
{
    // まとめたシーン(AllStages)で手直ししたステージを読み取り、StageBuilder.cs の定義(Add(...) の中)を書き換える
    //  ・書き換えるのは、定義と違っていた部分だけ: 床・壁 / ギミック / 生成エリア / 出現位置 / 積み木の上限(MaxBlocks)
    //    (同じだった部分の行・コメントはそのまま残す)
    //  ・ギミックの値を手で変えた所(塀の周期・生成エリアの数 など)は、置いた直後の c.Tune("項目名", 値) で書く
    //  ・自分で作った形のギミック(氷・麻雀卓など。c.Mahjong(...) のような呼び出し)があるステージでは、
    //    それが作る物と見分けられない部分(床・壁、ギミック)は書き換えず、書き出した文をコンソールに出すだけにする
    //  ・説明(Concept / Flow)・背景・床の作り込み(Dress)・カメラは書き換えない
    // 書き換えたあと、ステージを作り直す(「AllStages に ... を作る」のメニュー)と、床の飾り・背景・カメラも新しい形に合う
    public static partial class StageBuilder
    {
        const string kBuilderSourcePath = "Assets/Nakagawa/Editor/StageBuilder.cs";

        // 書き換える部分の種類(定義の中に並べる順)
        const int kGeometry = 0, kGimmick = 1, kArea = 2, kSpawn = 3, kCategoryCount = 4;
        static readonly string[] kCategoryNames = { "床・壁", "ギミック", "生成エリア", "出現位置" };

        // 種類ごとの、定義に書く Ctx のメソッド(Tune は直前の文と同じ種類)
        static readonly Dictionary<string, int> kCallCategory = new Dictionary<string, int>
        {
            { "Arena", kGeometry }, { "Floor", kGeometry }, { "Wall", kGeometry },
            { "Bumper", kGimmick }, { "Fan", kGimmick }, { "Turntable", kGimmick }, { "RisingWall", kGimmick },
            { "SpeedFloor", kGimmick }, { "MovingFloor", kGimmick }, { "WarpGate", kGimmick }, { "ShooterLane", kGimmick }, { "Cannon", kGimmick },
            { "AreaRect", kArea }, { "AreaCircle", kArea },
            { "Spawns", kSpawn },
        };
        const string kTuneCall = "Tune";

        // 読み取れるギミック(プレハブの名前)
        static readonly HashSet<string> kKnownGimmickPrefabs = new HashSet<string>
        {
            "Bumper", "Fan", "Turntable",
            AdditionalGimmickBuilder.kRisingWall, AdditionalGimmickBuilder.kAccelerateFloor, AdditionalGimmickBuilder.kDecelerateFloor,
            AdditionalGimmickBuilder.kMovingFloor, AdditionalGimmickBuilder.kWarpGate, AdditionalGimmickBuilder.kShooterLane, AdditionalGimmickBuilder.kCannon,
        };

        //========================================
        // メニュー
        //========================================

        // 選んだステージ(なければ表示中のステージ)の定義を書き換える
        [MenuItem("Nakagawa/Stages/AllStages から定義を書き換える(選んだステージ)")]
        static void WriteDefinitionMenu()
        {
            foreach (var id in TargetStageIds()) Debug.Log(WriteDefinitionFromAllStages(id, true, true));
        }

        // 書き換えずに、違っている所と書き出す文をコンソールに出す
        [MenuItem("Nakagawa/Stages/AllStages と定義の違いを確かめる(選んだステージ)")]
        static void PreviewDefinitionMenu()
        {
            foreach (var id in TargetStageIds()) Debug.Log(WriteDefinitionFromAllStages(id, false, false));
        }

        // 選んでいる物が入っているステージ。なければ AllStages で表示しているステージ
        static List<string> TargetStageIds()
        {
            var ids = new List<string>();
            foreach (var t in Selection.transforms)
            {
                var entry = t.GetComponentInParent<StageEntry>(true);
                if (entry != null && !ids.Contains(entry.name)) ids.Add(entry.name);
            }
            if (ids.Count > 0) return ids;

            Scene combined = SceneManager.GetSceneByPath(kCombinedPath);
            if (combined.IsValid() && combined.isLoaded)
            {
                foreach (var root in combined.GetRootGameObjects())
                {
                    if (root.activeInHierarchy && root.GetComponent<StageEntry>() != null) ids.Add(root.name);
                }
            }
            if (ids.Count != 1)
            {
                EditorUtility.DisplayDialog("定義の書き出し", "AllStages を開き、書き出すステージ(StageXX の中の物)をヒエラルキーで選んでください。", "OK");
                ids.Clear();
            }
            return ids;
        }

        //========================================
        // 本体
        //========================================

        // id のステージを AllStages から読み取り、定義と違う所を調べる。write なら StageBuilder.cs を書き換える
        // confirm なら書き換える前に確認のダイアログを出す。結果の説明を返す
        public static string WriteDefinitionFromAllStages(string id, bool write, bool confirm)
        {
            var log = new StringBuilder();
            log.AppendLine($"[StageBuilder] {id} の定義の書き出し{(write ? "" : "(確認だけ)")}");

            Scene combined = SceneManager.GetSceneByPath(kCombinedPath);
            if (!combined.IsValid() || !combined.isLoaded) return log.AppendLine($"  {kCombinedPath} を開いてください").ToString();
            GameObject root = combined.GetRootGameObjects().FirstOrDefault(r => r.name == id && r.GetComponent<StageEntry>() != null);
            if (root == null) return log.AppendLine($"  AllStages に {id} がありません").ToString();
            StageDef def = Definitions().Find(d => d.Id == id);
            if (def == null) return log.AppendLine($"  {id} は StageBuilder の定義から作るステージではありません(専用のビルダー・手で作ったステージ)").ToString();

            // 定義のソース
            string sourcePath = Path.GetFullPath(kBuilderSourcePath);
            string text = File.ReadAllText(sourcePath, Encoding.UTF8);
            var src = SourceStage.Find(text, def.Title, out string findError);
            if (src == null) return log.AppendLine("  " + findError).ToString();

            Scene original = SceneManager.GetActiveScene();
            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temp);
            try
            {
                // 今の定義どおりに組み立てた物と、AllStages の物を比べる
                var fresh = NewScratch(temp, id);
                def.Build(fresh);
                StageGeometryFixer.Fix(fresh.Stage);
                var defSnap = Snapshot.From(fresh.Stage, fresh.Gimmicks, fresh.Areas, fresh.SpawnPoints, def.MaxBlocks);
                var sceneSnap = Snapshot.FromStageRoot(root.transform);

                // 注意は種類ごとに集め、定義と違っていた種類の分だけ出す
                var warnings = new List<string>[kCategoryCount];
                for (int cat = 0; cat < kCategoryCount; cat++) warnings[cat] = new List<string>();
                var sceneLines = new List<DefLine>[kCategoryCount];
                sceneLines[kGeometry] = GeometryLines(sceneSnap, temp, id, warnings[kGeometry]);
                sceneLines[kGimmick] = sceneSnap.Gimmicks.Select(g => WithTunes(GimmickLine(g, warnings[kGimmick]), g, temp, id, warnings[kGimmick])).Where(l => l != null).ToList();
                sceneLines[kArea] = sceneSnap.Areas.Select(a => WithTunes(AreaLine(a), a.gameObject, temp, id, warnings[kArea])).ToList();
                sceneLines[kSpawn] = new List<DefLine> { SpawnLine(sceneSnap.Spawns) };

                var defGimmicks = defSnap.Gimmicks.Select(g => WithTunes(GimmickLine(g, null), g, temp, id, null)).Where(l => l != null).ToList();
                var defAreas = defSnap.Areas.Select(a => WithTunes(AreaLine(a), a.gameObject, temp, id, null)).ToList();

                var changed = new bool[kCategoryCount];
                changed[kGeometry] = !SameRects(sceneSnap.Floors, defSnap.Floors) || !SameRects(sceneSnap.Walls, defSnap.Walls);
                changed[kGimmick] = !SameCode(sceneLines[kGimmick], defGimmicks);
                changed[kArea] = !SameCode(sceneLines[kArea], defAreas);
                changed[kSpawn] = !SameCode(sceneLines[kSpawn], new List<DefLine> { SpawnLine(defSnap.Spawns) });
                bool maxChanged = sceneSnap.MaxBlocks != src.MaxBlocks;

                foreach (var name in sceneSnap.UnknownGimmicks.Except(defSnap.UnknownGimmicks))
                {
                    warnings[kGimmick].Add($"Gimmicks の下の「{name}」は読み取れないギミックなので、定義に書けません");
                    changed[kGimmick] = true;
                }

                // 書き換えられるか(定義の中の書き方で決まる)
                var writable = new bool[kCategoryCount];
                var reasons = new string[kCategoryCount];
                for (int cat = 0; cat < kCategoryCount; cat++)
                {
                    writable[cat] = src.CanReplace(cat, out reasons[cat]);
                }
                // 自分で作った形のギミックの呼び出しがあると、その中で置いた物と見分けられないので、数が合うときだけ書き換える
                if (src.HasCustomCalls && writable[kGeometry]) { writable[kGeometry] = false; reasons[kGeometry] = $"定義に {string.Join(", ", src.CustomCalls)} があり、それが作る床・壁と見分けられない"; }
                if (writable[kGimmick] && defSnap.Gimmicks.Count != src.StatementCount(kGimmick))
                {
                    writable[kGimmick] = false;
                    reasons[kGimmick] = $"定義の呼び出し以外で置かれたギミックがある({string.Join(", ", src.CustomCalls)})";
                }
                if (writable[kArea] && defSnap.Areas.Count != src.StatementCount(kArea))
                {
                    writable[kArea] = false;
                    reasons[kArea] = $"定義の呼び出し以外で置かれた生成エリアがある({string.Join(", ", src.CustomCalls)})";
                }

                // 結果
                var toWrite = new List<int>();
                var summary = new List<string>();
                for (int cat = 0; cat < kCategoryCount; cat++)
                {
                    if (!changed[cat]) continue;
                    if (writable[cat])
                    {
                        toWrite.Add(cat);
                        summary.Add(kCategoryNames[cat]);
                    }
                    else
                    {
                        log.AppendLine($"  ×{kCategoryNames[cat]}は定義と違いますが、書き換えられません: {reasons[cat]}。次の文を手で反映してください");
                        foreach (var line in sceneLines[cat]) log.AppendLine(Indent(line.Code, "      "));
                    }
                }
                if (maxChanged) summary.Add($"積み木の上限 {src.MaxBlocks} → {sceneSnap.MaxBlocks}");

                if (summary.Count == 0)
                {
                    log.AppendLine("  定義と AllStages は同じです(書き換える所はありません)");
                }
                else
                {
                    log.AppendLine("  定義と違う所: " + string.Join(" / ", summary));
                    foreach (int cat in toWrite)
                    {
                        log.AppendLine($"  ・{kCategoryNames[cat]}");
                        foreach (var line in sceneLines[cat]) log.AppendLine(Indent(line.Code, "      "));
                    }
                }
                for (int cat = 0; cat < kCategoryCount; cat++)
                {
                    if (!changed[cat]) continue;
                    foreach (var w in warnings[cat].Distinct()) log.AppendLine("  ！" + w);
                }

                // 書き換えた定義で組み立てたときの検証(自分で作った形のギミックがないときだけ、ここで確かめられる)
                if (!src.HasCustomCalls)
                {
                    var check = NewScratch(temp, id);
                    check.MaxGimmickTypes = fresh.MaxGimmickTypes;
                    for (int cat = 0; cat < kCategoryCount; cat++)
                    {
                        foreach (var line in sceneLines[cat]) line.Run(check);
                    }
                    log.AppendLine("  書き換えたあとの検証: " + Validate(check));
                }
                else
                {
                    log.AppendLine("  検証: 自分で作った形のギミックがあるので、ステージを作り直したときの結果を見てください");
                }

                if (!write || (toWrite.Count == 0 && !maxChanged)) return log.ToString();

                if (confirm && !EditorUtility.DisplayDialog("定義の書き出し",
                        $"{id} {def.Title} の定義を AllStages に合わせて書き換えます。\n\n書き換える所: {string.Join(" / ", summary)}\n\n" +
                        "説明(Concept / Flow)は書き換えません。詳しくはコンソールを見てください。", "書き換える", "やめる"))
                {
                    return log.AppendLine("  やめました").ToString();
                }

                var replacements = new Dictionary<int, List<string>>();
                foreach (int cat in toWrite) replacements[cat] = sceneLines[cat].Select(l => l.Code).ToList();
                string newText = src.Rewrite(replacements, maxChanged ? sceneSnap.MaxBlocks : (int?)null);
                File.WriteAllText(sourcePath, newText, new UTF8Encoding(false));
                log.AppendLine($"  {kBuilderSourcePath} を書き換えました。コンパイルのあと、ステージを作り直してください" +
                               "(床の飾り・背景・カメラも新しい形に合います)。説明(Concept / Flow)は必要なら手で直してください");
                AssetDatabase.ImportAsset(kBuilderSourcePath);
                return log.ToString();
            }
            finally
            {
                if (original.IsValid()) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        static string Indent(string code, string indent) => string.Join("\n", code.Split('\n').Select(l => indent + l));

        // 組み立てを試すための Ctx(一時的なシーンに、ステージ1つ分の入れ物を作る)
        static Ctx NewScratch(Scene scene, string id)
        {
            var c = new Ctx { Scene = scene, Id = id };
            var root = new GameObject("Scratch").transform;
            c.Stage = new GameObject("Stage").transform;
            c.Stage.SetParent(root, false);
            c.Gimmicks = new GameObject("Gimmicks").transform;
            c.Gimmicks.SetParent(root, false);
            c.Areas = new GameObject("TumikiSpawner").transform;
            c.Areas.SetParent(root, false);
            return c;
        }

        //========================================
        // ステージの中身の読み取り
        //========================================

        class Snapshot
        {
            public readonly List<Rect> Floors = new List<Rect>();
            public readonly List<Rect> Walls = new List<Rect>();
            public readonly List<GameObject> Gimmicks = new List<GameObject>();
            public readonly List<string> UnknownGimmicks = new List<string>();
            public readonly List<TumikiSpawnArea> Areas = new List<TumikiSpawnArea>();
            public readonly List<Vector2> Spawns = new List<Vector2>();
            public int MaxBlocks;

            // まとめたシーンのステージ(StageXX)から
            public static Snapshot FromStageRoot(Transform root)
            {
                var spawner = root.GetComponentInChildren<TumikiSpawner>(true);
                var points = new List<Vector2>();
                var spawnRoot = root.Find("SpawnPoints");
                if (spawnRoot != null)
                {
                    foreach (Transform t in spawnRoot) points.Add(new Vector2(t.position.x, t.position.z));
                }
                int max = 0;
                if (spawner != null) max = new SerializedObject(spawner).FindProperty("m_maxBlocksOnBoard").intValue;
                return From(root.Find("Stage"), root.Find("Gimmicks"), spawner != null ? spawner.transform : null, points, max);
            }

            public static Snapshot From(Transform stage, Transform gimmicks, Transform areas, IEnumerable<Vector2> spawns, int maxBlocks)
            {
                var s = new Snapshot { MaxBlocks = maxBlocks };
                if (stage != null)
                {
                    foreach (Transform t in stage)
                    {
                        if (!IsFlatCube(t)) continue;
                        if (t.GetComponent<ReflectWall>() != null)
                        {
                            // 見えない壁(InvWall)は、まとめたシーンで壁を高くしたときに足したもの
                            if (t.name == "Wall") s.Walls.Add(XZRect(t));
                            continue;
                        }
                        if (t.name.StartsWith("Floor") && t.GetComponent<BoxCollider>() != null) s.Floors.Add(XZRect(t));
                    }
                }
                if (gimmicks != null)
                {
                    foreach (Transform t in gimmicks)
                    {
                        // 見えない壁(InvWall)は、まとめたシーンで壁を高くしたときに足したもの
                        if (t.name == "InvWall" && t.GetComponent<ReflectWall>() != null) continue;
                        if (IsKnownGimmick(t.gameObject))
                        {
                            s.Gimmicks.Add(t.gameObject);
                            continue;
                        }
                        // 扇風機・移動床は、往復の地点と一緒にまとめた入れ物の中
                        bool found = false;
                        if (t.name.StartsWith("Fan_Group_") || t.name.StartsWith("MovingFloor_Group_"))
                        {
                            foreach (Transform child in t)
                            {
                                if (!IsKnownGimmick(child.gameObject)) continue;
                                s.Gimmicks.Add(child.gameObject);
                                found = true;
                            }
                        }
                        if (!found) s.UnknownGimmicks.Add(t.name);
                    }
                }
                if (areas != null)
                {
                    foreach (Transform t in areas)
                    {
                        if (t.TryGetComponent<TumikiSpawnArea>(out var area)) s.Areas.Add(area);
                    }
                }
                s.Spawns.AddRange(spawns);
                return s;
            }
        }

        static bool IsFlatCube(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube") return false;
            return Quaternion.Angle(t.rotation, Quaternion.identity) < 0.01f;
        }

        static Rect XZRect(Transform t)
        {
            Vector3 s = t.lossyScale, p = t.position;
            float w = Mathf.Abs(s.x), d = Mathf.Abs(s.z);
            return new Rect(p.x - w * 0.5f, p.z - d * 0.5f, w, d);
        }

        static string PrefabName(GameObject go)
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return null;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null ? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source)) : null;
        }

        static bool IsKnownGimmick(GameObject go)
        {
            string name = PrefabName(go);
            return name != null && kKnownGimmickPrefabs.Contains(name);
        }

        static bool SameRects(List<Rect> a, List<Rect> b)
        {
            if (a.Count != b.Count) return false;
            var rest = new List<Rect>(b);
            foreach (var r in a)
            {
                int i = rest.FindIndex(o => Near(o.xMin, r.xMin) && Near(o.yMin, r.yMin) && Near(o.xMax, r.xMax) && Near(o.yMax, r.yMax));
                if (i < 0) return false;
                rest.RemoveAt(i);
            }
            return true;
        }

        static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        static bool SameCode(List<DefLine> a, List<DefLine> b)
        {
            return a.Select(l => l.Code).SequenceEqual(b.Select(l => l.Code));
        }

        //========================================
        // 定義の文を作る
        //========================================

        // 定義の1文(c.Bumper(...); など。値を変えた所の c.Tune(...); を含む)と、同じことを Ctx に行う処理
        class DefLine
        {
            public string Code;
            public Action<Ctx> Run;
            public DefLine(string code, Action<Ctx> run)
            {
                Code = code;
                Run = run;
            }
        }

        // 数値は小数3桁に丸めて書く(組み立てにも丸めた値を使う)
        static float R(float v)
        {
            float r = (float)Math.Round(v, 3);
            return Mathf.Abs(r) < 0.0005f ? 0.0f : r;
        }

        static string F(float v)
        {
            double r = Math.Round(v, 3);
            if (Math.Abs(r) < 0.0005) r = 0;
            if (Math.Abs(r - Math.Round(r)) < 1e-6) return ((long)Math.Round(r)).ToString(CultureInfo.InvariantCulture);
            return r.ToString("0.###", CultureInfo.InvariantCulture) + "f";
        }

        static string B(bool v) => v ? "true" : "false";
        static string V2(Vector2 v) => $"new Vector2({F(v.x)}, {F(v.y)})";
        static string V3(Vector3 v) => $"new Vector3({F(v.x)}, {F(v.y)}, {F(v.z)})";
        static Vector2 R2(Vector2 v) => new Vector2(R(v.x), R(v.y));
        static Vector3 R3(Vector3 v) => new Vector3(R(v.x), R(v.y), R(v.z));

        // 向き(Y の角度)。0〜360 にそろえる
        static float Yaw(Transform t)
        {
            float y = (float)Math.Round(t.eulerAngles.y, 2);
            if (y >= 360.0f || y < 0.005f) y = 0.0f;
            return y;
        }

        static string EnumName(SerializedProperty p)
        {
            int i = p.enumValueIndex;
            return i >= 0 && i < p.enumNames.Length ? p.enumNames[i] : p.enumNames[0];
        }

        static T ParseEnum<T>(string name) => (T)Enum.Parse(typeof(T), name);

        // 床・壁。Arena(床1枚と、その辺の壁)で書けるなら Arena、書けなければ Floor / Wall を並べる
        // どちらも組み立てて確かめ、AllStages と同じ形になるものを使う
        static List<DefLine> GeometryLines(Snapshot s, Scene temp, string id, List<string> warnings)
        {
            if (s.Floors.Count == 1)
            {
                Rect f = s.Floors[0];
                bool north = s.Walls.Any(w => Near(w.yMax, f.yMax) && Near(w.height, 1.0f) && w.width > 1.0f + 0.01f);
                bool south = s.Walls.Any(w => Near(w.yMin, f.yMin) && Near(w.height, 1.0f) && w.width > 1.0f + 0.01f);
                bool east = s.Walls.Any(w => Near(w.xMax, f.xMax) && Near(w.width, 1.0f) && w.height > 1.0f + 0.01f);
                bool west = s.Walls.Any(w => Near(w.xMin, f.xMin) && Near(w.width, 1.0f) && w.height > 1.0f + 0.01f);
                float xMin = f.xMin + (west ? 1 : 0), xMax = f.xMax - (east ? 1 : 0);
                float zMin = f.yMin + (south ? 1 : 0), zMax = f.yMax - (north ? 1 : 0);
                float cx = R((xMin + xMax) * 0.5f), cz = R((zMin + zMax) * 0.5f), w2 = R(xMax - xMin), d2 = R(zMax - zMin);
                var arena = new List<DefLine>
                {
                    new DefLine($"c.Arena({F(cx)}, {F(cz)}, {F(w2)}, {F(d2)}, {B(north)}, {B(south)}, {B(east)}, {B(west)});",
                        c => c.Arena(cx, cz, w2, d2, north, south, east, west)),
                };
                if (BuildsSameGeometry(arena, s, temp, id)) return arena;
            }

            var lines = new List<DefLine>();
            foreach (var r in s.Floors)
            {
                float cx = R(r.center.x), cz = R(r.center.y), w = R(r.width), d = R(r.height);
                lines.Add(new DefLine($"c.Floor({F(cx)}, {F(cz)}, {F(w)}, {F(d)});", c => c.Floor(cx, cz, w, d)));
            }
            foreach (var r in s.Walls)
            {
                float cx = R(r.center.x), cz = R(r.center.y), w = R(r.width), d = R(r.height);
                lines.Add(new DefLine($"c.Wall({F(cx)}, {F(cz)}, {F(w)}, {F(d)});", c => c.Wall(cx, cz, w, d)));
            }
            if (!BuildsSameGeometry(lines, s, temp, id)) warnings.Add("床・壁を書き出した文で組み立てても、AllStages と同じ形になりません(重なりの直しで形が変わる所があります)");
            return lines;
        }

        static bool BuildsSameGeometry(List<DefLine> lines, Snapshot s, Scene temp, string id)
        {
            var c = NewScratch(temp, id);
            foreach (var line in lines) line.Run(c);
            StageGeometryFixer.Fix(c.Stage);
            var built = Snapshot.From(c.Stage, null, null, Array.Empty<Vector2>(), 0);
            return SameRects(built.Floors, s.Floors) && SameRects(built.Walls, s.Walls);
        }

        // ギミック1つ分(値を変えた所の Tune は WithTunes で足す)
        static DefLine GimmickLine(GameObject go, List<string> warnings)
        {
            string kind = PrefabName(go);
            Transform t = go.transform;
            float x = R(t.position.x), z = R(t.position.z), yaw = Yaw(t);
            switch (kind)
            {
                case "Bumper":
                {
                    float scale = R(t.localScale.x);
                    return new DefLine($"c.Bumper({F(x)}, {F(z)}, {F(scale)});", c => c.Bumper(x, z, scale));
                }
                case "Fan":
                {
                    var so = new SerializedObject(go.GetComponent<FanGimmick>());
                    var pa = so.FindProperty("m_pointA").objectReferenceValue as Transform;
                    var pb = so.FindProperty("m_pointB").objectReferenceValue as Transform;
                    Vector2 a = pa != null ? R2(new Vector2(pa.position.x, pa.position.z)) : new Vector2(x, z);
                    Vector2 b = pb != null ? R2(new Vector2(pb.position.x, pb.position.z)) : a;
                    Vector3 dir = R3(so.FindProperty("m_windDirection").vector3Value);
                    float length = R(so.FindProperty("m_windLength").floatValue);
                    float width = R(so.FindProperty("m_windSize").vector2Value.x);
                    float force = R(so.FindProperty("m_windForce").floatValue);
                    float speed = R(so.FindProperty("m_moveSpeed").floatValue);
                    return new DefLine($"c.Fan({V2(a)}, {V2(b)}, {V3(dir)}, {F(length)}, {F(width)}, {F(force)}, {F(speed)});",
                        c => c.Fan(a, b, dir, length, width, force, speed));
                }
                case "Turntable":
                {
                    var so = new SerializedObject(go.GetComponent<TurntableGimmick>());
                    bool clockwise = EnumName(so.FindProperty("m_direction")) == nameof(TurntableGimmick.Direction.Clockwise);
                    float radius = R(so.FindProperty("m_radius").floatValue);
                    float speed = R(so.FindProperty("m_speed").floatValue);
                    return new DefLine($"c.Turntable({F(x)}, {F(z)}, {F(radius)}, {B(clockwise)}, {F(speed)});", c => c.Turntable(x, z, radius, clockwise, speed));
                }
                case AdditionalGimmickBuilder.kRisingWall:
                {
                    var so = new SerializedObject(go.GetComponent<RisingWallGimmick>());
                    Vector2 size = R2(so.FindProperty("m_size").vector2Value);
                    float phase = R(so.FindProperty("m_phaseOffset").floatValue);
                    float height = R(so.FindProperty("m_wallHeight").floatValue);
                    // 高さが既定(跳んでも登れない高さ)なら省く
                    string code = Near(height, kRisingWallHeight, 0.0005f)
                        ? $"c.RisingWall({F(x)}, {F(z)}, {V2(size)}, {F(phase)});"
                        : $"c.RisingWall({F(x)}, {F(z)}, {V2(size)}, {F(phase)}, {F(height)});";
                    return new DefLine(code, c => c.RisingWall(x, z, size, phase, height));
                }
                case AdditionalGimmickBuilder.kAccelerateFloor:
                case AdditionalGimmickBuilder.kDecelerateFloor:
                {
                    bool accelerate = kind == AdditionalGimmickBuilder.kAccelerateFloor;
                    Vector2 size = R2(new SerializedObject(go.GetComponent<SpeedFloorGimmick>()).FindProperty("m_size").vector2Value);
                    return new DefLine($"c.SpeedFloor({F(x)}, {F(z)}, {V2(size)}, {B(accelerate)});", c => c.SpeedFloor(x, z, size, accelerate));
                }
                case AdditionalGimmickBuilder.kMovingFloor:
                {
                    var so = new SerializedObject(go.GetComponent<MovingFloorGimmick>());
                    var list = so.FindProperty("m_waypoints");
                    var points = new List<Vector2>();
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var p = list.GetArrayElementAtIndex(i).FindPropertyRelative("Point").objectReferenceValue as Transform;
                        if (p != null) points.Add(R2(new Vector2(p.position.x, p.position.z)));
                        else warnings?.Add($"移動床 {go.name} の {i + 1} 番目の地点がないので、飛ばしました");
                    }
                    if (points.Count == 0)
                    {
                        warnings?.Add($"移動床 {go.name} に地点がないので、書き出せません");
                        return null;
                    }
                    Vector2 size = R2(so.FindProperty("m_size").vector2Value);
                    string mode = EnumName(so.FindProperty("m_loopMode"));
                    float speed = R(so.FindProperty("m_speed").floatValue);
                    float wait = R(so.FindProperty("m_waitTime").floatValue);
                    var pts = points.ToArray();
                    return new DefLine($"c.MovingFloor({V2(size)}, MovingFloorGimmick.LoopMode.{mode}, {F(speed)}, {F(wait)}, {string.Join(", ", pts.Select(V2))});",
                        c => c.MovingFloor(size, ParseEnum<MovingFloorGimmick.LoopMode>(mode), speed, wait, pts));
                }
                case AdditionalGimmickBuilder.kWarpGate:
                {
                    var so = new SerializedObject(go.GetComponent<WarpGateGimmick>());
                    int group = so.FindProperty("m_group").intValue;
                    string target = EnumName(so.FindProperty("m_target"));
                    string selection = EnumName(so.FindProperty("m_selection"));
                    string orientation = EnumName(so.FindProperty("m_orientation"));
                    float radius = R(so.FindProperty("m_radius").floatValue);
                    return new DefLine($"c.WarpGate({F(x)}, {F(z)}, {F(yaw)}, {group}, WarpGateGimmick.TargetType.{target}, WarpGateGimmick.Selection.{selection}, WarpGateGimmick.Orientation.{orientation}, {F(radius)});",
                        c => c.WarpGate(x, z, yaw, group, ParseEnum<WarpGateGimmick.TargetType>(target), ParseEnum<WarpGateGimmick.Selection>(selection),
                            ParseEnum<WarpGateGimmick.Orientation>(orientation), radius));
                }
                case AdditionalGimmickBuilder.kShooterLane:
                {
                    Vector2 size = R2(new SerializedObject(go.GetComponent<ShooterLaneGimmick>()).FindProperty("m_size").vector2Value);
                    return new DefLine($"c.ShooterLane({F(x)}, {F(z)}, {F(yaw)}, {V2(size)});", c => c.ShooterLane(x, z, yaw, size));
                }
                case AdditionalGimmickBuilder.kCannon:
                {
                    var so = new SerializedObject(go.GetComponent<TumikiCannon>());
                    string mode = EnumName(so.FindProperty("m_rotationMode"));
                    int min = so.FindProperty("m_minAmount").intValue;
                    int max = so.FindProperty("m_maxAmount").intValue;
                    return new DefLine($"c.Cannon({F(x)}, {F(z)}, {F(yaw)}, TumikiCannon.RotationMode.{mode}, {min}, {max});",
                        c => c.Cannon(x, z, yaw, ParseEnum<TumikiCannon.RotationMode>(mode), min, max));
                }
            }
            warnings?.Add($"{go.name} は読み取れないギミックなので、書き出せません");
            return null;
        }

        static DefLine AreaLine(TumikiSpawnArea area)
        {
            var so = new SerializedObject(area);
            Vector3 p = area.transform.position;
            float x = R(p.x), z = R(p.z);
            if ((TumikiSpawnArea.Shape)so.FindProperty("m_shape").enumValueIndex == TumikiSpawnArea.Shape.Circle)
            {
                float r = R(so.FindProperty("m_radius").floatValue);
                return new DefLine($"c.AreaCircle({F(x)}, {F(z)}, {F(r)});", c => c.AreaCircle(x, z, r));
            }
            Vector2 size = R2(so.FindProperty("m_size").vector2Value);
            return new DefLine($"c.AreaRect({F(x)}, {F(z)}, {F(size.x)}, {F(size.y)});", c => c.AreaRect(x, z, size.x, size.y));
        }

        static DefLine SpawnLine(List<Vector2> points)
        {
            var pts = points.Select(R2).ToArray();
            return new DefLine($"c.Spawns({string.Join(", ", pts.Select(V2))});", c => c.Spawns(pts));
        }

        // line で組み立てた物と actual を比べ、値が違う項目を c.Tune(...) で足す
        // (位置・向き・大きさが違うのに定義で書けないときは warnings に足す)
        static DefLine WithTunes(DefLine line, GameObject actual, Scene temp, string id, List<string> warnings)
        {
            if (line == null) return null;
            var c = NewScratch(temp, id);
            line.Run(c);
            GameObject built = c.LastPlaced;
            if (built == null) return line;

            Transform a = actual.transform, b = built.transform;
            if (Vector3.Distance(a.position, b.position) > 0.01f || Quaternion.Angle(a.rotation, b.rotation) > 0.5f || Vector3.Distance(a.lossyScale, b.lossyScale) > 0.01f)
            {
                warnings?.Add($"{actual.name} の位置・向き・大きさの一部(高さ・傾き など)は定義で書けないので、書き出した定義では元に戻ります");
            }

            var codes = new List<string>();
            var runs = new List<Action<Ctx>>();
            foreach (var actualComp in actual.GetComponents<MonoBehaviour>())
            {
                if (actualComp == null) continue;
                var builtComp = built.GetComponent(actualComp.GetType()) as MonoBehaviour;
                if (builtComp == null) continue;
                var aso = new SerializedObject(actualComp);
                var bso = new SerializedObject(builtComp);
                var it = aso.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    var other = bso.FindProperty(it.propertyPath);
                    if (other == null) continue;
                    string path = it.propertyPath;
                    switch (it.propertyType)
                    {
                        case SerializedPropertyType.Float:
                        {
                            float v = it.floatValue;
                            if (Mathf.Abs(v - other.floatValue) <= 1e-4f * Mathf.Max(1.0f, Mathf.Abs(v))) break;
                            float rv = R(v);
                            codes.Add($"c.Tune(\"{path}\", {F(rv)});");
                            runs.Add(cc => cc.Tune(path, rv));
                            break;
                        }
                        case SerializedPropertyType.Integer:
                        {
                            int v = it.intValue;
                            if (v == other.intValue) break;
                            codes.Add($"c.Tune(\"{path}\", {v});");
                            runs.Add(cc => cc.Tune(path, v));
                            break;
                        }
                        case SerializedPropertyType.Enum:
                        {
                            int v = it.enumValueIndex;
                            if (v == other.enumValueIndex) break;
                            codes.Add($"c.Tune(\"{path}\", {v}); // {EnumName(it)}");
                            runs.Add(cc => cc.Tune(path, v));
                            break;
                        }
                        case SerializedPropertyType.Boolean:
                        {
                            bool v = it.boolValue;
                            if (v == other.boolValue) break;
                            codes.Add($"c.Tune(\"{path}\", {B(v)});");
                            runs.Add(cc => cc.Tune(path, v));
                            break;
                        }
                        case SerializedPropertyType.Vector2:
                        {
                            Vector2 v = it.vector2Value;
                            if (Vector2.Distance(v, other.vector2Value) <= 1e-4f) break;
                            Vector2 rv = R2(v);
                            codes.Add($"c.Tune(\"{path}\", {V2(rv)});");
                            runs.Add(cc => cc.Tune(path, rv));
                            break;
                        }
                        case SerializedPropertyType.Vector3:
                        {
                            Vector3 v = it.vector3Value;
                            if (Vector3.Distance(v, other.vector3Value) <= 1e-4f) break;
                            Vector3 rv = R3(v);
                            codes.Add($"c.Tune(\"{path}\", {V3(rv)});");
                            runs.Add(cc => cc.Tune(path, rv));
                            break;
                        }
                        case SerializedPropertyType.ObjectReference:
                            if (RefKey(it.objectReferenceValue, a) != RefKey(other.objectReferenceValue, b))
                            {
                                warnings?.Add($"{actual.name} の {path} の参照を変えていますが、参照は定義に書けません");
                            }
                            break;
                        case SerializedPropertyType.Color:
                        case SerializedPropertyType.String:
                            if (!SerializedProperty.DataEquals(it, other)) warnings?.Add($"{actual.name} の {path} を変えていますが、色・文字は定義に書けません(色は GimmickPalette で決めます)");
                            break;
                    }
                }
            }
            if (codes.Count == 0) return line;
            return new DefLine(line.Code + "\n" + string.Join("\n", codes), cc =>
            {
                line.Run(cc);
                foreach (var run in runs) run(cc);
            });
        }

        // 参照を比べるための名前。シーンの中の物は、ギミックの入れ物(親)からの道筋(自分の子・往復の地点など)、アセットはパス
        static string RefKey(Object obj, Transform root)
        {
            if (obj == null) return "null";
            Transform t = obj is Component comp ? comp.transform : obj is GameObject go ? go.transform : null;
            if (t == null || !t.gameObject.scene.IsValid()) return AssetDatabase.GetAssetPath(obj) + ":" + obj.name + ":" + obj.GetType().Name;
            Transform top = root.parent != null ? root.parent : root;
            var names = new List<string>();
            for (Transform x = t; x != null && x != top; x = x.parent) names.Insert(0, x == root ? "." : x.name);
            return string.Join("/", names) + ":" + obj.GetType().Name;
        }

        //========================================
        // 値を変える(定義の中で、置いた直後に c.Tune("項目名", 値) と書く)
        //========================================

        partial class Ctx
        {
            // 最後に置いたギミック・生成エリア(Place で入る)
            public GameObject LastPlaced;

            // 最後に置いた物の、名前が property の項目(Inspector の項目のスクリプト上の名前。m_floorTime など)を value にする
            // 数値・列挙(番号)・真偽・Vector2・Vector3 に使える。値を変えたあと、形・見た目を合わせ直す
            public Ctx Tune(string property, float value) => TuneProperty(property, p =>
            {
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Float: p.floatValue = value; return true;
                    case SerializedPropertyType.Integer: p.intValue = Mathf.RoundToInt(value); return true;
                    case SerializedPropertyType.Enum: p.enumValueIndex = Mathf.RoundToInt(value); return true;
                    case SerializedPropertyType.Boolean: p.boolValue = value != 0.0f; return true;
                }
                return false;
            });

            public Ctx Tune(string property, bool value) => TuneProperty(property, p =>
            {
                if (p.propertyType != SerializedPropertyType.Boolean) return false;
                p.boolValue = value;
                return true;
            });

            public Ctx Tune(string property, Vector2 value) => TuneProperty(property, p =>
            {
                if (p.propertyType != SerializedPropertyType.Vector2) return false;
                p.vector2Value = value;
                return true;
            });

            public Ctx Tune(string property, Vector3 value) => TuneProperty(property, p =>
            {
                if (p.propertyType != SerializedPropertyType.Vector3) return false;
                p.vector3Value = value;
                return true;
            });

            // 形・見た目を値に合わせ直すメソッド(あるものだけ呼ぶ)
            static readonly string[] kRefreshMethods = { "ApplyShape", "ApplyParticleSettings" };

            Ctx TuneProperty(string property, Func<SerializedProperty, bool> set)
            {
                if (LastPlaced == null) throw new InvalidOperationException($"Tune(\"{property}\"): 値を変える物がありません(ギミック・生成エリアを置いた直後に書いてください)");
                foreach (var mb in LastPlaced.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    var so = new SerializedObject(mb);
                    var p = so.FindProperty(property);
                    if (p == null) continue;
                    if (!set(p)) throw new InvalidOperationException($"Tune(\"{property}\"): {LastPlaced.name} の {property} は {p.propertyType} なので、この値は入れられません");
                    so.ApplyModifiedPropertiesWithoutUndo();
                    foreach (var name in kRefreshMethods)
                    {
                        var method = mb.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        method?.Invoke(mb, null);
                    }
                    return this;
                }
                throw new InvalidOperationException($"Tune(\"{property}\"): {LastPlaced.name} に {property} という項目がありません");
            }
        }

        //========================================
        // 定義のソースの読み書き
        //========================================

        // StageBuilder.cs の中の、ステージ1つ分の定義(Add("名前", 上限, 説明, 展開, c => { ... }))
        class SourceStage
        {
            string m_text;
            int m_maxStart, m_maxLength;
            int m_bodyStart, m_bodyEnd;   // { の次 〜 } の位置
            List<string> m_lines;         // 本体の行(\n で分けた。最初は { の行の残り、最後は } の前の字下げ)
            readonly List<Statement> m_statements = new List<Statement>();
            readonly bool[] m_nested = new bool[kCategoryCount];

            public int MaxBlocks;
            public readonly List<string> CustomCalls = new List<string>();
            public bool HasCustomCalls => CustomCalls.Count > 0;

            class Statement
            {
                public string Name;
                public int Category;      // -1 なら書き換えない文
                public int FirstLine, LastLine;
            }

            public static SourceStage Find(string text, string title, out string error)
            {
                error = null;
                string key = "Add(\"" + title + "\",";
                int at = text.IndexOf(key, StringComparison.Ordinal);
                if (at < 0 || text.IndexOf(key, at + 1, StringComparison.Ordinal) >= 0)
                {
                    error = at < 0 ? $"StageBuilder.cs に Add(\"{title}\", ...) が見つかりません" : $"StageBuilder.cs に Add(\"{title}\", ...) が2つ以上あります";
                    return null;
                }
                var m = new Regex(@"\G\s*(\d+)\s*,").Match(text, at + key.Length);
                if (!m.Success)
                {
                    error = $"Add(\"{title}\", ...) の積み木の上限が数値ではありません";
                    return null;
                }
                var s = new SourceStage { m_text = text, MaxBlocks = int.Parse(m.Groups[1].Value) };
                s.m_maxStart = m.Groups[1].Index;
                s.m_maxLength = m.Groups[1].Length;

                int lambda = FindInCode(text, m.Index + m.Length, "c =>");
                int open = lambda >= 0 ? FindInCode(text, lambda, "{") : -1;
                int close = open >= 0 ? MatchingBrace(text, open) : -1;
                if (close < 0)
                {
                    error = $"Add(\"{title}\", ...) の c => {{ ... }} が見つかりません";
                    return null;
                }
                s.m_bodyStart = open + 1;
                s.m_bodyEnd = close;
                s.Parse();
                return s;
            }

            void Parse()
            {
                string body = m_text.Substring(m_bodyStart, m_bodyEnd - m_bodyStart).Replace("\r\n", "\n");
                m_lines = body.Split('\n').ToList();
                var code = CodeMask(body, out int[] depth);

                // 行の始まりの位置
                var lineStart = new int[m_lines.Count];
                for (int i = 1; i < m_lines.Count; i++) lineStart[i] = lineStart[i - 1] + m_lines[i - 1].Length + 1;

                // 文の始まり: 本体の直下で、行の先頭が c.名前(
                var starts = new HashSet<int>();
                int lastCategory = -1;
                for (int i = 0; i < m_lines.Count; i++)
                {
                    string line = m_lines[i];
                    int indent = line.Length - line.TrimStart().Length;
                    int pos = lineStart[i] + indent;
                    if (pos >= body.Length || !code[pos] || depth[pos] != 0) continue;
                    var m = Regex.Match(line.Substring(indent), @"^c\.(\w+)\s*\(");
                    if (!m.Success) continue;
                    string name = m.Groups[1].Value;
                    int end = StatementEnd(body, code, pos);
                    int last = i;
                    while (last + 1 < m_lines.Count && lineStart[last + 1] <= end) last++;
                    int category = name == kTuneCall ? lastCategory : kCallCategory.TryGetValue(name, out int cat) ? cat : -1;
                    if (name != kTuneCall) lastCategory = category;
                    m_statements.Add(new Statement { Name = name, Category = category, FirstLine = i, LastLine = last });
                    starts.Add(pos);
                    i = last;
                }

                // 文の始まり以外にある c.名前( (ループの中・式の中など)と、自分で作った形のギミックの呼び出し
                foreach (Match m in Regex.Matches(body, @"\bc\.(\w+)\s*\("))
                {
                    if (!code[m.Index]) continue;
                    string name = m.Groups[1].Value;
                    if (kCallCategory.TryGetValue(name, out int cat))
                    {
                        if (!starts.Contains(m.Index)) m_nested[cat] = true;
                    }
                    else if (name != kTuneCall && !CustomCalls.Contains(name))
                    {
                        CustomCalls.Add(name);
                    }
                }
            }

            public int StatementCount(int category) => m_statements.Count(s => s.Category == category && s.Name != kTuneCall);

            public bool CanReplace(int category, out string reason)
            {
                reason = null;
                if (m_nested[category])
                {
                    reason = "定義の中で、ループや式の中から呼んでいる";
                    return false;
                }
                return true;
            }

            // categories の文を入れ替え(なければ足し)、maxBlocks があれば積み木の上限も変えた、ファイル全体の文字列を返す
            public string Rewrite(Dictionary<int, List<string>> replacements, int? maxBlocks)
            {
                string nl = m_text.Contains("\r\n") ? "\r\n" : "\n";
                var remove = new bool[m_lines.Count];
                var insertAt = new Dictionary<int, List<string>>();

                // 字下げは本体の最初の文に合わせる
                var first = m_statements.FirstOrDefault();
                string indent = first != null
                    ? m_lines[first.FirstLine].Substring(0, m_lines[first.FirstLine].Length - m_lines[first.FirstLine].TrimStart().Length)
                    : m_lines[m_lines.Count - 1] + "    ";

                foreach (var pair in replacements.OrderBy(p => p.Key))
                {
                    int cat = pair.Key;
                    var olds = m_statements.Where(s => s.Category == cat).ToList();
                    int at;
                    if (olds.Count > 0)
                    {
                        at = int.MaxValue;
                        foreach (var s in olds)
                        {
                            int top = s.FirstLine;
                            // 文のすぐ上のコメントも一緒に消す(古い値の説明が残らないように)
                            while (top - 1 >= 0 && !remove[top - 1] && m_lines[top - 1].TrimStart().StartsWith("//") && !IsStatementLine(top - 1)) top--;
                            for (int i = top; i <= s.LastLine; i++) remove[i] = true;
                            at = Mathf.Min(at, top);
                        }
                    }
                    else
                    {
                        // 前の種類の文のあと(なければ本体の始め)に足す
                        var before = m_statements.Where(s => s.Category >= 0 && s.Category < cat).ToList();
                        at = before.Count > 0 ? before.Max(s => s.LastLine) + 1 : 1;
                    }
                    var lines = pair.Value.SelectMany(code => code.Split('\n')).Select(l => indent + l).ToList();
                    if (!insertAt.TryGetValue(at, out var list)) insertAt[at] = list = new List<string>();
                    list.AddRange(lines);
                }

                var result = new List<string>();
                for (int i = 0; i < m_lines.Count; i++)
                {
                    if (insertAt.TryGetValue(i, out var add)) result.AddRange(add);
                    if (!remove[i]) result.Add(m_lines[i]);
                }
                string body = string.Join(nl, result);

                var sb = new StringBuilder();
                sb.Append(m_text, 0, m_maxStart);
                sb.Append(maxBlocks.HasValue ? maxBlocks.Value.ToString(CultureInfo.InvariantCulture) : m_text.Substring(m_maxStart, m_maxLength));
                sb.Append(m_text, m_maxStart + m_maxLength, m_bodyStart - (m_maxStart + m_maxLength));
                sb.Append(body);
                sb.Append(m_text, m_bodyEnd, m_text.Length - m_bodyEnd);
                return sb.ToString();
            }

            bool IsStatementLine(int line) => m_statements.Any(s => line >= s.FirstLine && line <= s.LastLine);

            // start の文の終わり(かっこの外の ;)の位置
            static int StatementEnd(string body, bool[] code, int start)
            {
                int paren = 0;
                for (int i = start; i < body.Length; i++)
                {
                    if (!code[i]) continue;
                    char ch = body[i];
                    if (ch == '(' || ch == '{' || ch == '[') paren++;
                    else if (ch == ')' || ch == '}' || ch == ']') paren--;
                    else if (ch == ';' && paren == 0) return i;
                }
                return body.Length - 1;
            }

            // 文字列・コメントの外(コード)の文字か。depth は文字の前の { } の深さ
            static bool[] CodeMask(string s, out int[] depth)
            {
                var code = new bool[s.Length];
                depth = new int[s.Length];
                int d = 0;
                int i = 0;
                while (i < s.Length)
                {
                    char ch = s[i];
                    char next = i + 1 < s.Length ? s[i + 1] : '\0';
                    if (ch == '/' && next == '/')
                    {
                        while (i < s.Length && s[i] != '\n') { depth[i] = d; i++; }
                        continue;
                    }
                    if (ch == '/' && next == '*')
                    {
                        int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        end = end < 0 ? s.Length : end + 2;
                        for (; i < end; i++) depth[i] = d;
                        continue;
                    }
                    if (ch == '"' || ch == '\'' || ((ch == '@' || ch == '$') && (next == '"' || (next == '@' || next == '$'))))
                    {
                        int end = SkipLiteral(s, i);
                        for (; i < end; i++) depth[i] = d;
                        continue;
                    }
                    code[i] = true;
                    depth[i] = d;
                    if (ch == '{') d++;
                    else if (ch == '}') d--;
                    i++;
                }
                return code;
            }

            // i から始まる文字列・文字のリテラルの次の位置
            static int SkipLiteral(string s, int i)
            {
                bool verbatim = false;
                while (i < s.Length && (s[i] == '@' || s[i] == '$'))
                {
                    if (s[i] == '@') verbatim = true;
                    i++;
                }
                if (i >= s.Length) return s.Length;
                char quote = s[i++];
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (!verbatim && ch == '\\') { i += 2; continue; }
                    if (ch == quote)
                    {
                        if (verbatim && i + 1 < s.Length && s[i + 1] == quote) { i += 2; continue; }
                        return i + 1;
                    }
                    if (!verbatim && ch == '\n') return i;
                    i++;
                }
                return s.Length;
            }

            // from から、コードの中で最初に token が出てくる位置
            static int FindInCode(string text, int from, string token)
            {
                int i = from;
                while (i < text.Length)
                {
                    char ch = text[i];
                    char next = i + 1 < text.Length ? text[i + 1] : '\0';
                    if (ch == '/' && next == '/') { i = text.IndexOf('\n', i); if (i < 0) return -1; continue; }
                    if (ch == '/' && next == '*') { i = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (i < 0) return -1; i += 2; continue; }
                    if (ch == '"' || ch == '\'' || ((ch == '@' || ch == '$') && (next == '"' || next == '@' || next == '$'))) { i = SkipLiteral(text, i); continue; }
                    if (string.CompareOrdinal(text, i, token, 0, token.Length) == 0) return i;
                    i++;
                }
                return -1;
            }

            // open の { に対応する } の位置
            static int MatchingBrace(string text, int open)
            {
                int d = 0;
                int i = open;
                while (i < text.Length)
                {
                    char ch = text[i];
                    char next = i + 1 < text.Length ? text[i + 1] : '\0';
                    if (ch == '/' && next == '/') { i = text.IndexOf('\n', i); if (i < 0) return -1; continue; }
                    if (ch == '/' && next == '*') { i = text.IndexOf("*/", i + 2, StringComparison.Ordinal); if (i < 0) return -1; i += 2; continue; }
                    if (ch == '"' || ch == '\'' || ((ch == '@' || ch == '$') && (next == '"' || next == '@' || next == '$'))) { i = SkipLiteral(text, i); continue; }
                    if (ch == '{') d++;
                    else if (ch == '}' && --d == 0) return i;
                    i++;
                }
                return -1;
            }
        }
    }
}
