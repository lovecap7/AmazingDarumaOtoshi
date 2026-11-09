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
    const float Pitch = 8f, Fov = 30f, Margin = 0.82f;

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
            File.WriteAllBytes(path, Render(c.model));
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

    static byte[] Render(GameObject model)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)Object.Instantiate(model);
            SceneManager_Move(go, scene);

            var b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }

            var cam = new GameObject("Camera").AddComponent<Camera>();
            SceneManager_Move(cam.gameObject, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = Fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            float radius = b.extents.magnitude * Margin;
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
        }
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
}
