using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだるの弾速を目に見える形にする演出。
    // ・太く長い残光トレイル
    // ・弾のまわりに燃える火の粉(パーティクル)
    // ・弾が照らす点光源
    // どれも弾速が速いほど大きく・明るくなる。
    [RequireComponent(typeof(TumikiBlock))]
    public class AkadaruShotTrail : MonoBehaviour
    {
        [Tooltip("トレイルの寿命(秒) 遅い→速い")]
        [SerializeField] float m_minTime = 1.15f;
        [SerializeField] float m_maxTime = 2.55f;
        [Tooltip("トレイルの太さ 遅い→速い")]
        [SerializeField] float m_minWidth = 1.12f;
        [SerializeField] float m_maxWidth = 2.55f;
        [Tooltip("火の粉の量(個/秒) 遅い→速い")]
        [SerializeField] float m_minCometRate = 130.0f;
        [SerializeField] float m_maxCometRate = 290.0f;
        [Tooltip("光の強さ 遅い→速い")]
        [SerializeField] float m_minLight = 11.5f;
        [SerializeField] float m_maxLight = 26.0f;

        [Header("必殺技(トロン)用の強調")]
        [Tooltip("レーザーの外側グロー(帯)の太さ")]
        [SerializeField] float m_specialGlowWidth = 4.0f;
        [Tooltip("レーザーの中心(白い芯)の太さ")]
        [SerializeField] float m_specialCoreWidth = 1.0f;
        [Tooltip("必殺技レーザーの残光の長さ(秒)。長いほど古いレーザーが長く残る")]
        [SerializeField] float m_specialTime = 4.0f;
        [Tooltip("必殺技中の火の粉の量(個/秒)")]
        [SerializeField] float m_specialCometRate = 600.0f;
        [Tooltip("必殺技中の火の粉の大きさ")]
        [SerializeField] float m_specialCometSize = 1.4f;
        [Tooltip("必殺技中の光の強さ")]
        [SerializeField] float m_specialLight = 48.0f;
        [Tooltip("必殺技中の光の届く距離")]
        [SerializeField] float m_specialLightRange = 12.0f;
        [Tooltip("弾が止まった後も、その場にビーム(残像)を残す")]
        [SerializeField] bool m_specialFreeze = true;
        [Tooltip("時間切れでビームを消すとき、徐々に消えるまでの時間(秒)")]
        [SerializeField] float m_specialFadeOut = 1.2f;

        [Header("必殺技レーザーの輪郭(アウトライン)")]
        [Tooltip("レーザーの輪郭の色。明るい背景でも沈まないよう、ほぼ黒に近い濃い色にする")]
        [SerializeField] Color m_specialOutlineColor = new Color(0.05f, 0.015f, 0.01f, 1.0f);
        [Tooltip("輪郭を白い芯(ビームの見えている線)よりどれだけ太くするか(m)")]
        [SerializeField] float m_specialOutlinePadding = 0.35f;
        [Tooltip("輪郭の帯の太さ(0〜1)。ビームの線のフチに沿わせる")]
        [SerializeField, Range(0.0f, 1.0f)] float m_specialOutlineThickness = 0.3f;

        TumikiBlock m_block;
        TrailRenderer m_trail;
        ParticleSystem m_comet;
        Light m_light;
        // 必殺技(トロン)のときだけ足す、太くて白いレーザーの帯
        TrailRenderer m_specialGlow;
        TrailRenderer m_specialCore;
        // レーザーの輪郭(背景が明るくても形が浮くように、外側に濃い帯を足す)
        TrailRenderer m_specialOutline;
        Material m_specialOutlineMaterial;
        Texture2D m_specialOutlineTexture;
        bool m_special;
        // 必殺技のビームをその場に凍結している(弾が止まった後も残す)
        bool m_specialHold;
        // 必殺技のビームを徐々に消している最中
        bool m_fadingOut;
        float m_fadeOutDuration;
        float m_fadeOutElapsed;
        Color m_fadeMainStart;
        Color m_fadeMainEnd;
        Color m_fadeGlowStart;
        Color m_fadeGlowEnd;
        Color m_fadeCoreStart;
        Color m_fadeCoreEnd;
        float m_fadeMainWidth;
        float m_fadeGlowWidth;
        float m_fadeCoreWidth;
        Color m_fadeOutlineStart;
        Color m_fadeOutlineEnd;
        float m_fadeOutlineWidth;
        // 積み木が元々持っている煙・炎のエフェクト(あかだるの弾の間だけ消す)
        ParticleSystem[] m_blockEffects;
        float m_minSpeed = 10.0f;
        float m_maxSpeed = 60.0f;

        // この発射で演出を付けたか(同じ積み木を何度も拾い直さないためのフラグ)
        public bool ConsumedLaunch { get; private set; }

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            enabled = false;
        }

        // あかだるのパッシブから、飛び始めた積み木に対して呼ばれる
        public void Begin(Material material, float minSpeed, float maxSpeed, Vector3 muzzlePosition)
        {
            m_minSpeed = Mathf.Max(0.01f, minSpeed);
            m_maxSpeed = Mathf.Max(m_minSpeed + 0.01f, maxSpeed);
            ConsumedLaunch = true;
            m_special = false;

            // 前回の残像が消えかけている場合は片付ける(再発射で新しい演出を出す)
            if (m_trail != null && !m_trail.emitting)
            {
                Destroy(m_trail.gameObject);
                m_trail = null;
                m_comet = null;
                m_light = null;
                ClearSpecialOverlay();
            }

            CreateEffect(material);
            HideBlockEffects();
            UpdateEffect();
            enabled = true;

            // 撃った瞬間だけマズルフラッシュ(打ち返しでは出さない)
            if (m_block != null && m_block.ReturnCount == 0)
            {
                // ネット対戦用(suehara): クライアントの積み木は止めてあるので、向き・速さは SpecialNet から読む
                AkadaruShotFlash.Spawn(muzzlePosition, SpecialNet.Direction(m_block), SpecialNet.Speed(m_block),
                    m_minSpeed, m_maxSpeed, material);
            }
        }

        // 必殺技「トロン」用。通常の射撃より太く・白く・長く残るレーザーにする。
        // (当たり判定に使う m_trail は通常どおりの太さのまま。見た目だけを足す)
        public void BeginSpecial(Material material, float minSpeed, float maxSpeed, Vector3 muzzlePosition)
        {
            Begin(material, minSpeed, maxSpeed, muzzlePosition);

            m_special = true;
            CreateSpecialOverlay(material);
            UpdateEffect();
        }

        // トロン用の「輪郭」「外側グロー」「白い芯」の3本のトレイル
        private void CreateSpecialOverlay(Material material)
        {
            if (m_specialGlow != null) return;

            // TrailRenderer は1つの GameObject に複数付けない(子に分ける)
            var root = new GameObject("TronLaserOverlay");
            root.transform.position = transform.position;

            // 最初に一回り太い「輪郭」を敷き、その上にグローと白い芯を重ねる。
            // 背景が白くても、濃い輪郭の線でレーザーの形が分かるようにする。
            m_specialOutline = CreateOutlinePart(root);

            m_specialGlow = CreateTrailPart(root, material, m_specialGlowWidth,
                new Color(1.0f, 0.30f, 0.05f, 0.9f));

            var coreObject = new GameObject("Core");
            coreObject.transform.SetParent(root.transform, false);
            m_specialCore = CreateTrailPart(coreObject, material, m_specialCoreWidth,
                new Color(1.0f, 0.98f, 0.90f, 1.0f));
        }

        // レーザーの輪郭。グローより一回り太い帯にして、幅の内側だけ透明にする。
        // マテリアルの描画順をグローより後ろにすることで、レーザーの外側に濃い縁が残る。
        private TrailRenderer CreateOutlinePart(GameObject root)
        {
            var outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(root.transform, false);

            m_specialOutlineTexture = CreateOutlineTexture(m_specialOutlineThickness);
            m_specialOutlineMaterial = CreateOutlineMaterial(m_specialOutlineTexture);

            var trail = outlineObject.AddComponent<TrailRenderer>();
            if (m_specialOutlineMaterial != null) trail.sharedMaterial = m_specialOutlineMaterial;
            trail.time = m_specialTime;
            trail.minVertexDistance = 0.05f;
            trail.autodestruct = false;
            trail.emitting = true;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startWidth = m_specialCoreWidth + m_specialOutlinePadding;
            trail.endWidth = 0.0f;
            trail.startColor = new Color(m_specialOutlineColor.r, m_specialOutlineColor.g,
                m_specialOutlineColor.b, 1.0f);
            trail.endColor = new Color(m_specialOutlineColor.r, m_specialOutlineColor.g,
                m_specialOutlineColor.b, 0.0f);
            return trail;
        }

        // 輪郭用のマテリアル。加算で光るグローより先に描いて、縁を暗く見せる。
        private static Material CreateOutlineMaterial(Texture2D texture)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var material = new Material(shader);
            material.name = "TronLaserOutline";
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            if (texture != null)
            {
                material.mainTexture = texture;
                material.SetTexture("_MainTex", texture);
            }
            return material;
        }

        // 輪郭用のテクスチャ。トレイルの幅方向(V)の中央を透明、外側を濃くする。
        private static Texture2D CreateOutlineTexture(float thickness)
        {
            const int height = 64;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false);
            texture.name = "TronLaserOutlineMask";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            // 内側をどこまで透明にするか(帯の太さ)
            float inner = Mathf.Clamp01(1.0f - Mathf.Clamp01(thickness));
            var pixels = new Color[height];
            for (int i = 0; i < height; i++)
            {
                float v = i / (float)(height - 1);
                float edge = Mathf.Abs(v - 0.5f) * 2.0f;   // 0 = 中心, 1 = 外側
                float alpha = Mathf.SmoothStep(0.0f, 1.0f, Mathf.InverseLerp(inner, 1.0f, edge));
                // 一番外側だけ少し薄くして、硬い板のフチに見えないようにする
                alpha *= 1.0f - 0.2f * Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.InverseLerp(0.9f, 1.0f, edge));
                pixels[i] = new Color(1.0f, 1.0f, 1.0f, alpha);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private TrailRenderer CreateTrailPart(GameObject go, Material material, float width, Color color)
        {
            var trail = go.AddComponent<TrailRenderer>();
            if (material != null) trail.sharedMaterial = material;
            trail.time = m_specialTime;
            trail.minVertexDistance = 0.05f;
            trail.autodestruct = false;
            trail.emitting = true;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startWidth = width;
            trail.endWidth = 0.0f;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0.0f);
            return trail;
        }

        private void ClearSpecialOverlay(bool fade = false)
        {
            if (m_specialGlow == null) return;

            float delay = 0.0f;
            if (fade)
            {
                m_specialGlow.emitting = false;
                if (m_specialCore != null) m_specialCore.emitting = false;
                if (m_specialOutline != null) m_specialOutline.emitting = false;
                // 点が寿命で消えるまで待ってから片付ける
                delay = m_specialTime + 0.3f;
                Destroy(m_specialGlow.gameObject, delay);
            }
            else
            {
                Destroy(m_specialGlow.gameObject);
            }

            m_specialGlow = null;
            m_specialCore = null;
            m_specialOutline = null;

            // 輪郭用に作ったマテリアル・テクスチャも一緒に片付ける
            if (m_specialOutlineMaterial != null)
            {
                Destroy(m_specialOutlineMaterial, delay);
                m_specialOutlineMaterial = null;
            }
            if (m_specialOutlineTexture != null)
            {
                Destroy(m_specialOutlineTexture, delay);
                m_specialOutlineTexture = null;
            }
        }

        private void Update()
        {
            if (m_block == null || m_trail == null) return;

            // 徐々に消している最中
            if (m_fadingOut)
            {
                UpdateFadeOut();
                return;
            }

            // 凍結中はトレイルの形をそのまま保つ(位置も太さも変えない)
            if (m_specialHold) return;

            // ネット対戦用(suehara): クライアントの積み木は止めてあるので、飛んでいるかは SpecialNet から読む
            if (!SpecialNet.IsFlying(m_block))
            {
                // 必殺技のビームは、弾が止まった後もその場に残す
                if (m_special && m_specialFreeze)
                {
                    FreezeSpecialBeam();
                    return;
                }

                // 落ちたら元の煙・炎を戻す(次に誰が投げても普通の積み木に戻る)
                StopEffect(true);
                return;
            }

            m_trail.transform.position = transform.position;
            if (m_specialGlow != null) m_specialGlow.transform.position = transform.position;
            UpdateEffect();
        }

        private void OnDestroy()
        {
            // 積み木ごと消えるときは、元のエフェクトを戻さない
            StopEffect(false);
        }

        // 積み木が元々持っている煙・炎を、あかだるの弾の間だけ消す。
        // (共有プレハブは触らず、この積み木のインスタンスだけ止める)
        private void HideBlockEffects()
        {
            if (m_blockEffects != null) return;

            var effects = GetComponentsInChildren<ParticleSystem>(true);
            if (effects == null || effects.Length == 0) return;

            m_blockEffects = effects;
            foreach (var ps in m_blockEffects)
            {
                if (ps == null) continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
            }
        }

        private void RestoreBlockEffects()
        {
            if (m_blockEffects == null) return;

            foreach (var ps in m_blockEffects)
            {
                if (ps == null) continue;
                ps.gameObject.SetActive(true);
                ps.Play();
            }
            m_blockEffects = null;
        }

        private void CreateEffect(Material material)
        {
            if (m_trail != null) return;

            var go = new GameObject("AkadaruShotFX");
            go.transform.position = transform.position;

            m_trail = go.AddComponent<TrailRenderer>();
            if (material != null) m_trail.sharedMaterial = material;
            m_trail.time = m_minTime;
            m_trail.minVertexDistance = 0.05f;
            m_trail.autodestruct = false;
            m_trail.emitting = true;
            m_trail.alignment = LineAlignment.View;
            m_trail.textureMode = LineTextureMode.Stretch;
            m_trail.numCapVertices = 2;
            m_trail.numCornerVertices = 2;
            m_trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_trail.receiveShadows = false;

            // 弾のあとを追いかける火の粉(ワールド空間に残るので尾になる)
            m_comet = go.AddComponent<ParticleSystem>();
            m_comet.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = m_comet.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = 0.0f;
            main.startSize = 0.4f;
            main.startColor = new Color(1.0f, 0.5f, 0.15f, 1.0f);
            main.maxParticles = 256;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = m_comet.emission;
            emission.enabled = true;
            emission.rateOverTime = m_minCometRate;

            var shape = m_comet.shape;
            shape.enabled = false;

            var colorOverLifetime = m_comet.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.8f, 0.4f), 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.25f, 0.05f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(0.7f, 0.3f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = m_comet.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, AnimationCurve.EaseInOut(0.0f, 1.0f, 1.0f, 0.4f));

            var renderer = m_comet.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_comet.Play();

            // 弾がまわりを照らす
            m_light = go.AddComponent<Light>();
            m_light.type = LightType.Point;
            m_light.color = new Color(1.0f, 0.55f, 0.2f);
            m_light.range = 4.0f;
            m_light.intensity = m_minLight;
            m_light.shadows = LightShadows.None;
        }

        // 速度に応じて長さ・太さ・色・光を変える
        private void UpdateEffect()
        {
            if (m_trail == null || m_block == null) return;

            float t = Mathf.InverseLerp(m_minSpeed, m_maxSpeed, SpecialNet.Speed(m_block));   // ネット対戦用(suehara)

            m_trail.time = Mathf.Lerp(m_minTime, m_maxTime, t);
            m_trail.startWidth = Mathf.Lerp(m_minWidth, m_maxWidth, t);
            m_trail.endWidth = 0.0f;
            m_trail.startColor = Color.Lerp(new Color(1.0f, 0.35f, 0.10f, 0.9f), new Color(1.0f, 0.95f, 0.80f, 1.0f), t);
            m_trail.endColor = new Color(1.0f, 0.30f, 0.05f, 0.0f);

            if (m_comet != null)
            {
                var main = m_comet.main;
                main.startSize = Mathf.Lerp(0.35f, 0.90f, t);
                main.startColor = Color.Lerp(new Color(1.0f, 0.45f, 0.10f, 1.0f), new Color(1.0f, 0.95f, 0.75f, 1.0f), t);
                var emission = m_comet.emission;
                emission.rateOverTime = Mathf.Lerp(m_minCometRate, m_maxCometRate, t);
            }

            if (m_light != null)
            {
                m_light.intensity = Mathf.Lerp(m_minLight, m_maxLight, t);
                m_light.range = Mathf.Lerp(3.5f, 7.0f, t);
            }

            // 必殺技(トロン)はさらに太く・白く・激しく
            if (m_special)
            {
                // 当たり判定に使う本体のトレイルも、見た目と同じ長さにそろえる
                m_trail.time = m_specialTime;
                if (m_specialGlow != null)
                {
                    m_specialGlow.time = m_specialTime;
                    m_specialGlow.startWidth = m_specialGlowWidth;
                }
                if (m_specialCore != null)
                {
                    m_specialCore.time = m_specialTime;
                    m_specialCore.startWidth = m_specialCoreWidth;
                }
                // 輪郭はビームの見えている線(白い芯)に沿わせる。グロー(4m)に合わせると太すぎる
                if (m_specialOutline != null)
                {
                    m_specialOutline.time = m_specialTime;
                    m_specialOutline.startWidth = m_specialCoreWidth + m_specialOutlinePadding;
                }
                if (m_comet != null)
                {
                    var main = m_comet.main;
                    main.startSize = m_specialCometSize;
                    main.startColor = new Color(1.0f, 0.92f, 0.75f, 1.0f);
                    var emission = m_comet.emission;
                    emission.rateOverTime = m_specialCometRate;
                }
                if (m_light != null)
                {
                    m_light.intensity = m_specialLight;
                    m_light.range = m_specialLightRange;
                }
            }
        }

        // いま表示されているトレイルの全点をコピーする(屈折しても判定を追従させるため)
        public int CopyTrailPositions(Vector3[] buffer)
        {
            if (m_trail == null || buffer == null || buffer.Length == 0) return 0;
            int count = m_trail.positionCount;
            if (count < 2 || count > buffer.Length) return 0;
            m_trail.GetPositions(buffer);
            return count;
        }

        public float TrailWidth
        {
            get
            {
                if (m_trail == null) return 0.0f;
                return Mathf.Max(0.05f, Mathf.Max(m_trail.startWidth, m_trail.endWidth));
            }
        }

        // 実際に表示されているトレイルの両端と幅を返す(必殺技の当たり判定を演出に合わせる)
        public bool TryGetTrailSegment(out Vector3 oldest, out Vector3 newest, out float width)
        {
            oldest = newest = Vector3.zero;
            width = 0.0f;
            if (m_trail == null || m_trail.positionCount < 2) return false;

            oldest = m_trail.GetPosition(0);
            newest = m_trail.GetPosition(m_trail.positionCount - 1);
            width = Mathf.Max(0.05f, Mathf.Max(m_trail.startWidth, m_trail.endWidth));
            return true;
        }

        // 弾が止まった位置でビームを凍結する。
        // TrailRenderer の点は寿命(time)で消えてしまうので、time を大きくして形を保つ。
        // 積み木が消える(=必殺技の時間切れ)まで、この状態でレーザーと当たり判定が残る。
        private void FreezeSpecialBeam()
        {
            m_specialHold = true;

            // 弾はもう止まっているので、積み木本来の煙・炎は戻す
            RestoreBlockEffects();

            FreezeTrail(m_trail);
            FreezeTrail(m_specialGlow);
            FreezeTrail(m_specialCore);
            FreezeTrail(m_specialOutline);

            if (m_comet != null)
            {
                var emission = m_comet.emission;
                emission.rateOverTime = 0.0f;
                m_comet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (m_light != null) m_light.enabled = false;
        }

        private static void FreezeTrail(TrailRenderer trail)
        {
            if (trail == null) return;
            trail.emitting = false;
            // 点が寿命で消えないようにする(形を凍結する)
            trail.time = 3600.0f;
        }

        // 凍結したビームを短い時間で消す(点が残り続けないようにする)
        private static void QuickFade(TrailRenderer trail)
        {
            if (trail == null) return;
            trail.emitting = false;
            trail.time = 0.2f;
        }

        // 必殺技のビームを、形を保ったまま徐々に消す(時間切れ用)。
        // 消え終わるまでの秒数を返す(呼び出し側が同じタイミングで積み木を片付ける)
        public float FadeOutSpecial()
        {
            if (m_trail == null) return 0.0f;
            if (m_fadingOut) return Mathf.Max(0.0f, m_fadeOutDuration - m_fadeOutElapsed);

            m_fadingOut = true;
            m_fadeOutDuration = Mathf.Max(0.05f, m_specialFadeOut);
            m_fadeOutElapsed = 0.0f;

            // 新しい点は出さない
            m_trail.emitting = false;
            if (m_specialGlow != null) m_specialGlow.emitting = false;
            if (m_specialCore != null) m_specialCore.emitting = false;
            if (m_specialOutline != null) m_specialOutline.emitting = false;

            // いまの色・太さを覚えておき、そこから薄く・細くしていく
            m_fadeMainStart = m_trail.startColor;
            m_fadeMainEnd = m_trail.endColor;
            m_fadeMainWidth = m_trail.startWidth;
            if (m_specialGlow != null)
            {
                m_fadeGlowStart = m_specialGlow.startColor;
                m_fadeGlowEnd = m_specialGlow.endColor;
                m_fadeGlowWidth = m_specialGlow.startWidth;
            }
            if (m_specialCore != null)
            {
                m_fadeCoreStart = m_specialCore.startColor;
                m_fadeCoreEnd = m_specialCore.endColor;
                m_fadeCoreWidth = m_specialCore.startWidth;
            }
            if (m_specialOutline != null)
            {
                m_fadeOutlineStart = m_specialOutline.startColor;
                m_fadeOutlineEnd = m_specialOutline.endColor;
                m_fadeOutlineWidth = m_specialOutline.startWidth;
            }

            return m_fadeOutDuration;
        }

        private void UpdateFadeOut()
        {
            m_fadeOutElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(m_fadeOutElapsed / m_fadeOutDuration);
            float keep = 1.0f - t;

            FadeTrail(m_trail, m_fadeMainStart, m_fadeMainEnd, m_fadeMainWidth, keep);
            FadeTrail(m_specialGlow, m_fadeGlowStart, m_fadeGlowEnd, m_fadeGlowWidth, keep);
            FadeTrail(m_specialCore, m_fadeCoreStart, m_fadeCoreEnd, m_fadeCoreWidth, keep);
            FadeTrail(m_specialOutline, m_fadeOutlineStart, m_fadeOutlineEnd, m_fadeOutlineWidth, keep);

            if (t < 1.0f) return;

            // 消し終わったら片付ける
            m_fadingOut = false;
            m_specialHold = false;
            m_special = false;
            enabled = false;

            if (m_trail != null)
            {
                Destroy(m_trail.gameObject);
                m_trail = null;
            }
            ClearSpecialOverlay(false);
        }

        // 色のアルファと太さをまとめて薄くする
        private static void FadeTrail(TrailRenderer trail, Color start, Color end, float width, float keep)
        {
            if (trail == null) return;
            start.a *= keep;
            end.a *= keep;
            trail.startColor = start;
            trail.endColor = end;
            trail.startWidth = width * keep;
        }

        private void StopEffect(bool restoreBlockEffects)
        {
            ConsumedLaunch = false;
            enabled = false;

            bool held = m_specialHold;
            bool fading = m_fadingOut;
            m_specialHold = false;
            m_special = false;
            m_fadingOut = false;

            if (restoreBlockEffects) RestoreBlockEffects();

            // 凍結中・徐々に消している途中に積み木が消えた場合は、残りを素早く消す
            if (held || fading)
            {
                QuickFade(m_trail);
                QuickFade(m_specialGlow);
                QuickFade(m_specialCore);
                QuickFade(m_specialOutline);
            }

            ClearSpecialOverlay(true);

            if (m_trail == null) return;

            m_trail.emitting = false;
            if (m_comet != null)
            {
                var emission = m_comet.emission;
                emission.rateOverTime = 0.0f;
            }
            if (m_light != null) m_light.enabled = false;

            float fade = (held || fading) ? 0.5f : Mathf.Max(m_trail.time, 0.4f) + 0.3f;
            Destroy(m_trail.gameObject, fade);
            m_comet = null;
            m_light = null;
        }
    }
}
