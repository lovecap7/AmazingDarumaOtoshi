using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// キャラの3Dモデル（CharacterData.model）を正面から撮って、セレクト画面・ずかんで使う顔写真（portrait）にする。
// メニュー「Fujihara > キャラの顔写真を撮る」。SelectRoster のキャラのうち、model が入っているものだけ撮る
// 写真は Data/Portraits/<id>.png（背景は透明）。モデルを直したら撮り直す
public static class CharacterPortraitBaker
{
    const string RosterPath = "Assets/Fujihara/Data/SelectRoster.asset";
    const string OutFolder = "Assets/Fujihara/Data/Portraits";
    const int Size = 512;
    // モデルの顔は +Z 側を向いているので、カメラは +Z 側から少し見下ろして撮る
    const float Pitch = 8f, Fov = 30f, Margin = 1.35f;

    [MenuItem("Fujihara/キャラの顔写真を撮る")]
    public static void BakeAll()
    {
        var roster = AssetDatabase.LoadAssetAtPath<SelectRoster>(RosterPath);
        if (roster == null) { Debug.LogError("SelectRoster が見つかりません：" + RosterPath); return; }
        if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Fujihara/Data", "Portraits");

        int count = 0;
        foreach (var c in roster.characters)
        {
            if (c == null || c.model == null) continue;
            string id = string.IsNullOrEmpty(c.id) ? c.name : c.id;
            string path = $"{OutFolder}/{id}.png";
            File.WriteAllBytes(path, Render(c.model, ExtrasOf(c)));
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();

            Undo.RecordObject(c, "キャラの顔写真");
            c.portrait = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            EditorUtility.SetDirty(c);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[CharacterPortraitBaker] {count} キャラの顔写真を撮りました（{OutFolder}）");
    }

    // 頭と一緒に写すもの：マントなど（extraModels）と、いつもの鉢巻
    public static GameObject[] ExtrasOf(CharacterData c)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        if (c.extraModels != null) list.AddRange(c.extraModels);
        var band = GameModelSettings.HachimakiFor(DarumaColor.Wood);
        if (band != null) list.Add(band);
        return list.ToArray();
    }

    // extras：頭と一緒に写すモデル（マント・アクセサリー。頭と同じ原点で作ったもの）
    public static byte[] Render(GameObject model, GameObject[] extras = null)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)Object.Instantiate(model);
            SceneManager_Move(go, scene);
            // 写す範囲は頭のモデルだけで決める（マントなどで広げると、ほかのキャラより小さく写ってしまうので）
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (extras != null)
                foreach (var e in extras)
                    if (e != null) Object.Instantiate(e, go.transform, false);
            AddReflections();

            var cam = new GameObject("Camera").AddComponent<Camera>();
            SceneManager_Move(cam.gameObject, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            // 幅と高さの大きい方が、写真いっぱい（Margin の分だけ余白）に入るように
            float radius = Mathf.Max(b.extents.x, b.extents.y) * Margin;
            float dist = radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad);
            var rot = Quaternion.Euler(Pitch, 180f, 0f);   // +Z 側から -Z を向く
            cam.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * dist, rot);

            // キーライト（右上前）と、柔らかい補助光
            var key = new GameObject("Key").AddComponent<Light>();
            SceneManager_Move(key.gameObject, scene);
            key.type = LightType.Directional;
            key.intensity = 1.3f;
            key.color = new Color(1f, 0.97f, 0.9f);
            key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            SceneManager_Move(fill.gameObject, scene);
            fill.type = LightType.Directional;
            fill.intensity = 0.6f;
            fill.transform.rotation = Quaternion.Euler(15f, 220f, 0f);

            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
            cam.Render();
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (s_envCube != null)
            {
                RenderSettings.defaultReflectionMode = s_oldReflMode;
                RenderSettings.customReflectionTexture = s_oldReflTex;
                Object.DestroyImmediate(s_envCube);
            }
            s_envCube = null;
        }
    }

    // 写真の中は周りに映す景色が無いので、金属（だるきんぐの金など）は暗く写ってしまう。
    // 上が明るく下が暗い、スタジオのような景色（キューブマップ）を作って、映り込みに使う。
    // 金属でないキャラは映り込みが弱いので、写りはほぼ変わらない
    static Cubemap s_envCube;
    static void AddReflections()
    {
        const int n = 64;
        s_envCube = new Cubemap(n, TextureFormat.RGBA32, false);
        var top = new Color(1.0f, 0.98f, 0.94f);
        var horizon = new Color(0.5f, 0.42f, 0.33f);
        var bottom = new Color(0.14f, 0.1f, 0.07f);
        var px = new Color[n * n];
        foreach (CubemapFace face in System.Enum.GetValues(typeof(CubemapFace)))
        {
            if (face == CubemapFace.Unknown) continue;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    Vector3 d;
                    switch (face)
                    {
                        case CubemapFace.PositiveX: d = new Vector3(1f, -v, -u); break;
                        case CubemapFace.NegativeX: d = new Vector3(-1f, -v, u); break;
                        case CubemapFace.PositiveY: d = new Vector3(u, 1f, v); break;
                        case CubemapFace.NegativeY: d = new Vector3(u, -1f, -v); break;
                        case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1f); break;
                        default: d = new Vector3(-u, -v, -1f); break;
                    }
                    float h = d.normalized.y;
                    var c = h >= 0f ? Color.Lerp(horizon, top, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(h * 1.6f))) : Color.Lerp(horizon, bottom, Mathf.Pow(-h, 0.5f));
                    // 斜め上に明るい窓（ハイライト）を1つ
                    var dn = d.normalized;
                    float win = Mathf.Clamp01((Vector3.Dot(dn, new Vector3(0.5f, 0.6f, 0.6f).normalized) - 0.9f) * 12f);
                    px[y * n + x] = Color.Lerp(c, Color.white * 1.0f, win);
                }
            s_envCube.SetPixels(px, face);
        }
        s_envCube.Apply();

        // 写真用のシーンでは Reflection Probe が効かず、空の映り込み（青）が使われて金が緑っぽくなるので、
        // 撮る間だけ全体の映り込みをこの景色にする（撮り終わったら Render の finally で元に戻す）
        s_oldReflMode = RenderSettings.defaultReflectionMode;
        s_oldReflTex = RenderSettings.customReflectionTexture;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = s_envCube;
    }
    static DefaultReflectionMode s_oldReflMode;
    static Texture s_oldReflTex;

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
}
