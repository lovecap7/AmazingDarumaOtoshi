using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Nakagawa
{
    // 麻雀卓(Stage22)の牌の見た目
    //  ・牌は角の丸い厚みのある箱。上が白い面(東・南・西・北の文字)、下が濃い緑の背
    //  ・種類は4つ(東・南・西・北)。効果の違いはない(役の判定に使うだけ)
    //  ・文字はゲームの既定のフォント(PresentationSettings.DefaultFont)で書く。牌は寝かせて置くので、文字は上から読める向き
    //  ・同じ形のメッシュは使い回す
    public static class MahjongTileArt
    {
        public const int KindCount = 4;
        public static readonly string[] KindNames = { "東", "南", "西", "北" };

        public static readonly Color Ivory = new Color(0.96f, 0.94f, 0.88f, 1.0f);
        public static readonly Color Back = new Color(0.17f, 0.34f, 0.26f, 1.0f);
        public static readonly Color Ink = new Color(0.1f, 0.12f, 0.2f, 1.0f);

        // 牌1枚の大きさ(寝かせたとき。X: 幅 / Y: 厚み / Z: 縦)と、白い面の厚みの割合
        // (見下ろしのカメラから文字が読めるよう、実物の比率のまま積み木と同じくらいの大きさにする)
        public static readonly Vector3 Size = new Vector3(0.78f, 0.52f, 1.05f);
        const float kFaceRatio = 0.66f;

        static Mesh s_faceMesh, s_backMesh;

        public static float HalfHeight => Size.y * 0.5f;

        // 寝かせた牌を作る(中心が箱の中心。scale: 大きさの倍率)。影を落とす
        public static Transform Create(string name, Transform parent, int kind, float scale, Material face, Material back)
        {
            kind = Mathf.Clamp(kind, 0, KindCount - 1);
            var root = new GameObject(name).transform;
            if (parent != null) root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            float faceH = Size.y * kFaceRatio, backH = Size.y - faceH;
            if (s_faceMesh == null) s_faceMesh = ToyKit.RoundedBox(new Vector3(Size.x, faceH, Size.z), 0.08f, 3);
            if (s_backMesh == null) s_backMesh = ToyKit.RoundedBox(new Vector3(Size.x * 0.985f, backH + 0.04f, Size.z * 0.985f), 0.08f, 3);
            Part(root, "Face", s_faceMesh, face, new Vector3(0, Size.y * 0.5f - faceH * 0.5f, 0));
            Part(root, "Back", s_backMesh, back, new Vector3(0, -Size.y * 0.5f + backH * 0.5f - 0.02f, 0));

            var font = PresentationSettings.DefaultFont;
            if (font != null)
            {
                var text = ToyKit.Text3D(font, font.material, KindNames[kind], root, new Vector2(Size.x * 0.82f, Size.z * 0.8f), 7.0f);
                text.color = Ink;
                text.fontStyle = FontStyles.Bold;
                text.transform.localPosition = new Vector3(0, Size.y * 0.5f + 0.004f, 0);
                // 上を向けて寝かせる(文字の上が +Z)
                text.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                var r = text.GetComponent<MeshRenderer>();
                if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return root;
        }

        static void Part(Transform root, string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        // HUD に並べる牌(白い面に文字、下に緑の背)
        public static RectTransform CreateIcon(Transform parent, Vector2 size)
        {
            var root = ToyKit.UIRounded("Tile", parent, Back, size.x * 0.18f);
            var face = ToyKit.UIRounded("Face", root, Ivory, size.x * 0.18f);
            face.anchorMin = new Vector2(0, 0.16f);
            face.anchorMax = Vector2.one;
            face.offsetMin = face.offsetMax = Vector2.zero;
            var font = PresentationSettings.DefaultFont;
            var text = ToyKit.UIText("Kind", face, font, "", size.y * 0.62f, Ink);
            text.fontStyle = FontStyles.Bold;
            ToyKit.Stretch(text.rectTransform);
            root.gameObject.AddComponent<Outline>().effectColor = ToyKit.Ink;
            return root;
        }

        public static void SetIcon(RectTransform icon, int kind)
        {
            var text = icon.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = KindNames[Mathf.Clamp(kind, 0, KindCount - 1)];
        }
    }
}
