using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // 撃墜演出: プレイヤーが脱落した位置から、吹き飛んだ向きへ光の柱が伸び、衝撃の輪・火花・閃光が弾ける(復活の出現演出を派手にしたもの)
    //  ・向き(DarumaCharacter.EliminationDirection): 落下は真上、ハンマーは叩いた相手からやられた側への向き、積み木は当たった積み木の進む向き
    //  ・位置(DarumaCharacter.EliminationPosition): 落下は床の縁を越えたあたり、それ以外は脱落した位置
    //  ・色はやられたプレイヤーの色を白に寄せたもの
    //  ・DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    //  ・Play を呼べば、好きな位置・向きで出せる
    public class KOBlastEffect : MonoBehaviour
    {
        // シーンに無いとき、DarumaMatch に自動で付けるか
        public static bool AutoAttach = true;

        [Header("出す脱落")]
        [SerializeField] bool m_onFall = true;
        [SerializeField] bool m_onAttack = true;
        [SerializeField] bool m_onOther = true;

        [Header("時間")]
        // 全体の長さ(秒)
        [SerializeField, Min(0.1f)] float m_duration = 1.3f;
        // スロー演出中でもこの速さ以上で進める(時間の速さに対する下限。止まっているときも進む)
        [SerializeField, Range(0.0f, 1.0f)] float m_minSpeed = 0.35f;

        [Header("大きさ")]
        // 光の柱の長さと太さ
        [SerializeField, Min(0.5f)] float m_beamLength = 9.0f;
        [SerializeField, Min(0.05f)] float m_beamRadius = 0.9f;
        // 横向き(ハンマー・積み木)のときに出す高さ(体の真ん中あたり)
        [SerializeField] float m_sideHeight = 0.7f;
        // 衝撃の輪の数と、広がる大きさ
        [SerializeField, Range(0, 6)] int m_ringCount = 3;
        [SerializeField, Min(0.5f)] float m_ringSpread = 3.5f;
        // 火花の数と飛ぶ速さ・広がる角度
        [SerializeField, Range(0, 64)] int m_sparkCount = 24;
        [SerializeField, Min(0.1f)] float m_sparkSpeed = 14.0f;
        [SerializeField, Range(0.0f, 90.0f)] float m_sparkSpreadAngle = 35.0f;

        [Header("色")]
        // プレイヤーの色に混ぜる白さ(1 で真っ白)
        [SerializeField, Range(0.0f, 1.0f)] float m_whiteness = 0.35f;

        [Header("光源")]
        [SerializeField, Min(0.0f)] float m_lightIntensity = 14.0f;
        [SerializeField, Min(0.1f)] float m_lightRange = 8.0f;

        class Spark
        {
            public Renderer renderer;
            public Vector3 velocity;
            public float length;
        }

        class Instance
        {
            public Transform root;
            public Renderer beam, core, flash, glow;
            public readonly List<Renderer> rings = new List<Renderer>();
            public readonly List<Spark> sparks = new List<Spark>();
            public Light light;
            public Color color;
            public float time;
        }

        readonly List<Instance> m_instances = new List<Instance>();
        // 脱落したキャラクターの色(脱落の通知の時点では MatchPlayer からキャラクターが外れているので、前のフレームまでに覚えておく)
        readonly Dictionary<DarumaCharacter, Color> m_colors = new Dictionary<DarumaCharacter, Color>();
        DarumaMatch m_match;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachToMatch();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToMatch();

        static void AttachToMatch()
        {
            if (!AutoAttach) return;
            var match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
            if (match == null || FindAnyObjectByType<KOBlastEffect>() != null) return;
            match.gameObject.AddComponent<KOBlastEffect>();
        }

        private void OnEnable()
        {
            m_match = GetComponent<DarumaMatch>();
            DarumaCharacter.AnyEliminated += OnEliminated;
        }

        private void OnDisable()
        {
            DarumaCharacter.AnyEliminated -= OnEliminated;
            foreach (var inst in m_instances) if (inst.root != null) Destroy(inst.root.gameObject);
            m_instances.Clear();
        }

        private void OnEliminated(DarumaCharacter character)
        {
            if (!ShouldPlay(character.EliminationCause)) return;
            if (!m_colors.TryGetValue(character, out Color color)) color = Color.white;
            Play(character.EliminationPosition, character.EliminationDirection, color);
        }

        private bool ShouldPlay(EliminationCause cause)
        {
            switch (cause)
            {
                case EliminationCause.Fall: return m_onFall;
                case EliminationCause.Projectile:
                case EliminationCause.Hammer: return m_onAttack;
                default: return m_onOther;
            }
        }

        // position(足元)から direction の向きに出す
        public void Play(Vector3 position, Vector3 direction, Color playerColor)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.up;
            direction.Normalize();
            bool vertical = Vector3.Dot(direction, Vector3.up) > 0.7f;
            Vector3 origin = vertical ? position : position + Vector3.up * m_sideHeight;

            var root = new GameObject("KOBlastEffect").transform;
            root.SetPositionAndRotation(origin, Quaternion.FromToRotation(Vector3.up, direction));
            root.gameObject.AddComponent<CameraFocusTarget>();

            Color color = EffectMeshes.Brighten(playerColor, m_whiteness);
            color.a = 1.0f;
            var inst = new Instance
            {
                root = root,
                color = color,
                beam = EffectMeshes.Create("Beam", root, EffectMeshes.Tube, true),
                core = EffectMeshes.Create("Core", root, EffectMeshes.Tube, true),
                flash = EffectMeshes.Create("Flash", root, EffectMeshes.Disc, true),
                glow = EffectMeshes.Create("Glow", root, EffectMeshes.Disc, true),
            };
            for (int i = 0; i < m_ringCount; i++) inst.rings.Add(EffectMeshes.Create($"Ring{i}", root, EffectMeshes.Ring, true));
            for (int i = 0; i < m_sparkCount; i++)
            {
                // 向きのまわりに円錐状に散らす
                Vector3 local = Quaternion.Euler(Random.Range(0.0f, m_sparkSpreadAngle), 0.0f, 0.0f) * Vector3.up;
                local = Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f) * local;
                inst.sparks.Add(new Spark
                {
                    renderer = EffectMeshes.Create($"Spark{i}", root, EffectMeshes.Tube, true),
                    velocity = local * (m_sparkSpeed * Random.Range(0.4f, 1.0f)),
                    length = Random.Range(0.5f, 1.2f),
                });
            }

            if (m_lightIntensity > 0.0f)
            {
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = Vector3.up * 0.5f;
                inst.light = lightGo.AddComponent<Light>();
                inst.light.type = LightType.Point;
                inst.light.color = color;
                inst.light.range = m_lightRange;
                inst.light.shadows = LightShadows.None;
            }

            m_instances.Add(inst);
            Apply(inst, 0.0f);
        }

        private void LateUpdate()
        {
            // 生きているキャラクターの色を覚えておく
            m_colors.Clear();
            var match = m_match != null ? m_match : DarumaMatch.Current;
            if (match != null)
            {
                foreach (var p in match.Players)
                {
                    if (p != null && p.Character != null) m_colors[p.Character] = p.Color;
                }
            }

            float dt = Time.unscaledDeltaTime * Mathf.Max(Time.timeScale, m_minSpeed);
            for (int i = m_instances.Count - 1; i >= 0; i--)
            {
                var inst = m_instances[i];
                if (inst.root == null) { m_instances.RemoveAt(i); continue; }
                inst.time += dt;
                if (inst.time >= m_duration)
                {
                    Destroy(inst.root.gameObject);
                    m_instances.RemoveAt(i);
                    continue;
                }
                Apply(inst, dt);
            }
        }

        private void Apply(Instance inst, float dt)
        {
            float t = inst.time / m_duration;
            float fade = 1.0f - Smooth(Mathf.InverseLerp(0.35f, 1.0f, t));
            float flicker = 0.85f + 0.15f * Mathf.Sin(inst.time * 50.0f);

            // 光の柱: 一気に伸び、太くなってから細く消えていく
            float extend = 1.0f - Mathf.Pow(1.0f - Mathf.Clamp01(t / 0.12f), 3.0f);
            float width = m_beamRadius * Mathf.Lerp(1.4f, 0.25f, Smooth(t));
            SetTube(inst.beam, width, m_beamLength * extend, inst.color, fade * flicker);
            SetTube(inst.core, width * 0.35f, m_beamLength * extend * 1.05f, Color.white, fade * flicker);

            // 閃光: 根元で一瞬大きく光る
            float flashT = Mathf.Clamp01(t / 0.18f);
            SetFlat(inst.flash, m_beamRadius * Mathf.Lerp(1.0f, 4.0f, EaseOut(flashT)), Color.white, 1.0f - flashT);
            SetFlat(inst.glow, m_beamRadius * Mathf.Lerp(1.5f, 2.5f, t), inst.color, fade * 0.8f);

            // 衝撃の輪: 少しずつ遅れて出て、柱に沿って進みながら広がる
            for (int i = 0; i < inst.rings.Count; i++)
            {
                var ring = inst.rings[i];
                float rt = Mathf.Clamp01((t - i * 0.07f) / 0.45f);
                ring.transform.localPosition = Vector3.up * (m_beamLength * 0.12f * (i + 1) * EaseOut(rt));
                SetFlat(ring, m_beamRadius * Mathf.Lerp(0.6f, m_ringSpread * (1.0f - i * 0.15f), EaseOut(rt)),
                    i % 2 == 0 ? inst.color : Color.white, rt > 0.0f ? 1.0f - rt : 0.0f);
            }

            // 火花: 円錐状に飛び散って減速しながら消える
            foreach (var s in inst.sparks)
            {
                s.velocity *= Mathf.Exp(-3.0f * dt);
                var tr = s.renderer.transform;
                tr.localPosition += s.velocity * dt;
                float speed = s.velocity.magnitude;
                if (speed > 0.001f) tr.localRotation = Quaternion.FromToRotation(Vector3.up, s.velocity / speed);
                float len = s.length * Mathf.Clamp(speed / m_sparkSpeed * 1.5f, 0.15f, 1.0f);
                SetTube(s.renderer, 0.05f, len, EffectMeshes.Brighten(inst.color, 0.5f), fade);
            }

            if (inst.light != null) inst.light.intensity = m_lightIntensity * (1.0f - Smooth(t)) * flicker;
        }

        private static void SetTube(Renderer r, float radius, float height, Color c, float alpha)
        {
            r.transform.localScale = new Vector3(radius, Mathf.Max(0.001f, height), radius);
            c.a = Mathf.Clamp01(alpha);
            EffectMeshes.SetColor(r, c);
        }

        private static void SetFlat(Renderer r, float radius, Color c, float alpha)
        {
            r.transform.localScale = new Vector3(radius, 1.0f, radius);
            c.a = Mathf.Clamp01(alpha);
            EffectMeshes.SetColor(r, c);
        }

        private static float EaseOut(float t) => 1.0f - (1.0f - t) * (1.0f - t);

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
