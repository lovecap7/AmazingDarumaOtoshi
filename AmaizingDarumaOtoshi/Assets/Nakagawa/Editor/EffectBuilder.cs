using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa.EditorTools
{
    // 演出用エフェクトのプレハブを Assets/Nakagawa/Prefabs に作る
    // どのエフェクトも同じ部品(閃光・光の柱・衝撃波・火花・きらめき・点光源・文字)の組み合わせで、色・文字・大きさだけが違う
    // 数値はこのファイルにまとめてあるので、直して作り直せる(作った後にプレハブを直接調整してもよい)
    public static class EffectBuilder
    {
        public const string kCatchEffectPath = "Assets/Nakagawa/Prefabs/Effect_TumikiCatch.prefab";
        public const string kEliminationEffectPath = "Assets/Nakagawa/Prefabs/Effect_Elimination.prefab";
        const string kMaterialDir = "Assets/Nakagawa/Materials";
        const string kBaseParticleMaterial = kMaterialDir + "/GimmickParticle.mat";
        const string kAdditiveMaterial = kMaterialDir + "/EffectAdditive.mat";

        // エフェクトごとの色・文字・大きさ
        class Style
        {
            public string Name;
            // 閃光・火花の芯の色 / 主な色 / 消えていくときの色
            public Color Light, Main, Accent;
            public string Text;
            public Color TextColor, TextShadow;
            // 全体の大きさ・勢いの倍率
            public float Scale = 1.0f;
            // 光の柱を出すか
            public bool Pillar = true;
        }

        // 飛んできた積み木に乗って止めたときの成功エフェクト(金色)
        static readonly Style kCatch = new Style
        {
            Name = "Effect_TumikiCatch",
            Light = new Color(1.0f, 1.0f, 0.95f, 1.0f),
            Main = new Color(1.0f, 0.82f, 0.2f, 1.0f),
            Accent = new Color(1.0f, 0.45f, 0.1f, 1.0f),
            Text = "NICE CATCH!",
            TextColor = new Color(1.0f, 0.9f, 0.3f, 1.0f),
            TextShadow = new Color(0.35f, 0.12f, 0.0f, 1.0f),
        };

        // プレイヤーが脱落したときの爆発(赤。スロー中に見せるので大きめ)
        static readonly Style kElimination = new Style
        {
            Name = "Effect_Elimination",
            Light = new Color(1.0f, 0.95f, 0.9f, 1.0f),
            Main = new Color(1.0f, 0.3f, 0.15f, 1.0f),
            Accent = new Color(0.6f, 0.05f, 0.25f, 1.0f),
            Text = "K.O.!",
            TextColor = new Color(1.0f, 0.25f, 0.2f, 1.0f),
            TextShadow = new Color(0.15f, 0.0f, 0.05f, 1.0f),
            Scale = 1.3f,
            Pillar = false,
        };

        //  閃光 → 光の柱 → 地面を走る衝撃波 → 尾を引いて弾ける火花 → 舞い上がるきらめき、点光源、「NICE CATCH!」の文字
        [MenuItem("Nakagawa/Effects/Build Catch Effect Prefab")]
        public static GameObject BuildCatchEffect() => Build(kCatch, kCatchEffectPath);

        //  閃光 → 地面を走る衝撃波 → 尾を引いて弾ける火花 → 舞い上がる火の粉、点光源、「K.O.!」の文字
        [MenuItem("Nakagawa/Effects/Build Elimination Effect Prefab")]
        public static GameObject BuildEliminationEffect() => Build(kElimination, kEliminationEffectPath);

        static GameObject Build(Style style, string path)
        {
            var material = AdditiveMaterial();
            // 開いているシーンを変更しないよう、作業用の別シーンで組み立てる
            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(style.Name);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                Flash(root.transform, material, "Flash_Light", style.Light, 3.4f * style.Scale, 0.16f);
                Flash(root.transform, material, "Flash_Main", style.Main, 2.0f * style.Scale, 0.4f);
                if (style.Pillar) Pillar(root.transform, material, style);
                Shockwave(root.transform, material, style);
                Sparks(root.transform, material, style);
                Glitter(root.transform, material, style);
                var light = PointLight(root.transform, style);
                var text = Text(root.transform, style, out var meshes);

                var popup = root.AddComponent<EffectPopup>();
                var so = new SerializedObject(popup);
                so.FindProperty("m_light").objectReferenceValue = light;
                so.FindProperty("m_text").objectReferenceValue = text;
                var meshProp = so.FindProperty("m_textMeshes");
                meshProp.arraySize = meshes.Length;
                for (int i = 0; i < meshes.Length; i++) meshProp.GetArrayElementAtIndex(i).objectReferenceValue = meshes[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[EffectBuilder] {path} を作りました");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        //========================================
        // 各層
        //========================================

        // 中心で一瞬大きく光る
        static void Flash(Transform parent, Material material, string name, Color color, float size, float lifetime)
        {
            var ps = NewParticleSystem(name, parent, material, lifetime);
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSpeed = 0.0f;
            main.startSize = size;
            main.startColor = color;
            Burst(ps, 0.0f, 1);
            SizeOverLifetime(ps, AnimationCurve.EaseInOut(0.0f, 0.35f, 1.0f, 1.2f));
            FadeOut(ps, color, color, 0.0f);
        }

        // 縦に伸びる光の柱(細くなりながら消える)
        static void Pillar(Transform parent, Material material, Style style)
        {
            var ps = NewParticleSystem("Pillar", parent, material, 0.5f);
            ps.transform.localPosition = new Vector3(0.0f, 2.2f * style.Scale, 0.0f);
            var main = ps.main;
            main.startLifetime = 0.45f;
            main.startSpeed = 0.0f;
            main.startSize3D = true;
            main.startSizeX = 1.1f * style.Scale;
            main.startSizeY = 5.0f * style.Scale;
            main.startSizeZ = 1.0f;
            main.startColor = style.Main;
            Burst(ps, 0.0f, 1);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            size.x = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.0f));
            size.y = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 0.6f, 1.0f, 1.3f));
            size.z = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Constant(0.0f, 1.0f, 1.0f));
            FadeOut(ps, style.Light, style.Main, 0.0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        }

        // 地面を水平に広がる衝撃波(光の筋が輪になって走る)
        static void Shockwave(Transform parent, Material material, Style style)
        {
            var ps = NewParticleSystem("Shockwave", parent, material, 0.5f);
            ps.transform.localPosition = new Vector3(0.0f, 0.1f, 0.0f);
            var main = ps.main;
            main.startLifetime = 0.4f;
            main.startSpeed = 11.0f * style.Scale;
            main.startSize = 0.35f * style.Scale;
            main.startColor = style.Main;
            Burst(ps, 0.0f, 56);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;
            shape.radiusThickness = 0.0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            // 円は XY 平面にあるので、寝かせて地面と平行にする
            shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);

            // 走りながら減速する
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.0f;
            limit.dampen = 0.08f;

            SizeOverLifetime(ps, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.0f));
            FadeOut(ps, style.Light, style.Accent, 0.0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.5f;
        }

        // 上へ弾けて、尾を引きながら落ちる火花
        static void Sparks(Transform parent, Material material, Style style)
        {
            var ps = NewParticleSystem("Sparks", parent, material, 1.0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.0f * style.Scale, 11.0f * style.Scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * style.Scale, 0.22f * style.Scale);
            main.startColor = new ParticleSystem.MinMaxGradient(style.Light, style.Main);
            main.gravityModifier = 1.6f;
            Burst(ps, 0.0f, Mathf.RoundToInt(44 * style.Scale));

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.25f;
            // 半球は +Z 向きなので、上(+Y)へ向ける
            shape.rotation = new Vector3(-90.0f, 0.0f, 0.0f);

            SizeOverLifetime(ps, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.2f));
            FadeOut(ps, style.Light, style.Accent, 0.0f);

            var trails = ps.trails;
            trails.enabled = true;
            trails.ratio = 1.0f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.25f);
            trails.minVertexDistance = 0.05f;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.0f));
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = material;
        }

        // 少し遅れて舞い上がり、瞬きながら消えるきらめき
        static void Glitter(Transform parent, Material material, Style style)
        {
            var ps = NewParticleSystem("Glitter", parent, material, 1.5f);
            ps.transform.localPosition = new Vector3(0.0f, 0.5f, 0.0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * style.Scale, 0.18f * style.Scale);
            main.startColor = new ParticleSystem.MinMaxGradient(style.Light, style.Main);
            main.gravityModifier = -0.15f;
            Burst(ps, 0.08f, 30);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.0f * style.Scale;

            // 瞬く
            SizeOverLifetime(ps, new AnimationCurve(
                new Keyframe(0.0f, 0.0f), new Keyframe(0.15f, 1.0f), new Keyframe(0.35f, 0.3f),
                new Keyframe(0.55f, 1.0f), new Keyframe(0.75f, 0.3f), new Keyframe(1.0f, 0.0f)));
            FadeOut(ps, style.Light, style.Main, 0.0f);
        }

        static Light PointLight(Transform parent, Style style)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.0f, 1.0f, 0.0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = style.Main;
            light.range = 7.0f * style.Scale;
            light.intensity = 8.0f;
            light.shadows = LightShadows.None;
            return light;
        }

        // 文字と、少し後ろにずらした濃い色の影
        static Transform Text(Transform parent, Style style, out TextMesh[] meshes)
        {
            var root = new GameObject("Text").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(0.0f, 1.8f * style.Scale, 0.0f);
            root.localScale = Vector3.one * style.Scale;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var shadow = NewText(root, "Shadow", style.Text, font, style.TextShadow, new Vector3(0.04f, -0.04f, 0.02f));
            var main = NewText(root, "Main", style.Text, font, style.TextColor, Vector3.zero);
            meshes = new[] { main, shadow };
            return root;
        }

        static TextMesh NewText(Transform parent, string name, string value, Font font, Color color, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var text = go.AddComponent<TextMesh>();
            text.text = value;
            text.font = font;
            text.fontSize = 96;
            text.characterSize = 0.045f;
            text.fontStyle = FontStyle.Bold;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return text;
        }

        //========================================
        // 補助
        //========================================

        static ParticleSystem NewParticleSystem(string name, Transform parent, Material material, float duration)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startRotation = new ParticleSystem.MinMaxCurve(0.0f, Mathf.PI * 2.0f);

            var emission = ps.emission;
            emission.rateOverTime = 0.0f;

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void Burst(ParticleSystem ps, float time, int count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        static void SizeOverLifetime(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.0f, curve);
        }

        // 色を from → to に変えながら、後半で透明になる
        static void FadeOut(ParticleSystem ps, Color from, Color to, float endAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0.0f), new GradientColorKey(to, 1.0f) },
                new[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 0.45f), new GradientAlphaKey(endAlpha, 1.0f) });
            col.color = gradient;
        }

        // 光が重なるほど明るくなる(加算)パーティクル用マテリアル。テクスチャは既存のパーティクル用マテリアルと同じ
        static Material AdditiveMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kAdditiveMaterial);
            if (mat != null) return mat;
            var src = AssetDatabase.LoadAssetAtPath<Material>(kBaseParticleMaterial);
            mat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 2.0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0.0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(mat, kAdditiveMaterial);
            return mat;
        }
    }
}
