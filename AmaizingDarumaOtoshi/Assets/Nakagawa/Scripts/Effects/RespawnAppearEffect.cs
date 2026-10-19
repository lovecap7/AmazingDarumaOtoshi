using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // 復活の出現演出: DarumaMatch が復活させる位置に光の柱と床の輪を出し、キャラクターが出る瞬間にぱっと光って消える
    //  ・DarumaMatch.PlayerRespawnAppearing を受け取って出す(出るまでの時間は DarumaMatch の「復活の出現演出の時間」)
    //  ・色はプレイヤーの色を白に寄せたもの
    //  ・演出の間は、カメラがプレイヤーと同じように画面に収める(CameraFocusTarget)
    //  ・DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    //  ・Play を呼べば、試合と関係なく好きな位置で出せる
    public class RespawnAppearEffect : MonoBehaviour
    {
        // シーンに無いとき、DarumaMatch に自動で付けるか
        public static bool AutoAttach = true;

        [Header("大きさ")]
        [SerializeField, Min(0.1f)] float m_radius = 0.7f;
        [SerializeField, Min(0.1f)] float m_height = 3.2f;

        [Header("色")]
        // プレイヤーの色に混ぜる白さ(1 で真っ白)
        [SerializeField, Range(0.0f, 1.0f)] float m_whiteness = 0.45f;

        [Header("出た瞬間の閃光")]
        [SerializeField, Min(0.01f)] float m_flashTime = 0.3f;
        // 閃光で輪が広がる大きさ(半径の倍率)
        [SerializeField, Min(1.0f)] float m_flashSpread = 3.0f;

        [Header("カメラ")]
        // 演出の間、カメラがプレイヤーと同じように画面に収める(CameraFocusTarget)
        [SerializeField] bool m_cameraFocus = true;

        [Header("光源")]
        [SerializeField, Min(0.0f)] float m_lightIntensity = 6.0f;
        [SerializeField, Min(0.1f)] float m_lightRange = 4.0f;

        class Instance
        {
            public Transform root;
            public Renderer pillar, core, glow, ring;
            public Light light;
            public Color color;
            public float time, duration;
        }

        readonly List<Instance> m_instances = new List<Instance>();
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
            if (match == null || FindAnyObjectByType<RespawnAppearEffect>() != null) return;
            match.gameObject.AddComponent<RespawnAppearEffect>();
        }

        private void OnEnable()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
            if (m_match != null) m_match.PlayerRespawnAppearing += OnAppearing;
        }

        private void OnDisable()
        {
            if (m_match != null) m_match.PlayerRespawnAppearing -= OnAppearing;
            foreach (var inst in m_instances) if (inst.root != null) Destroy(inst.root.gameObject);
            m_instances.Clear();
        }

        private void OnAppearing(MatchPlayer player, Vector3 pos, float duration)
        {
            Play(pos, player != null ? player.Color : Color.white, duration);
        }

        // pos(足元)に出す。duration 秒かけて光が強まり、そのあと閃光を出して消える
        public void Play(Vector3 pos, Color playerColor, float duration)
        {
            var root = new GameObject("RespawnAppearEffect").transform;
            root.position = pos;
            // カメラはこれから出るプレイヤーとして映しておく
            if (m_cameraFocus) root.gameObject.AddComponent<CameraFocusTarget>();
            Color color = EffectMeshes.Brighten(playerColor, m_whiteness);
            color.a = 1.0f;

            var inst = new Instance
            {
                root = root,
                pillar = EffectMeshes.Create("Pillar", root, EffectMeshes.Tube, true),
                core = EffectMeshes.Create("Core", root, EffectMeshes.Tube, true),
                glow = EffectMeshes.Create("Glow", root, EffectMeshes.Disc, true),
                ring = EffectMeshes.Create("Ring", root, EffectMeshes.Ring, true),
                color = color,
                duration = Mathf.Max(0.01f, duration),
            };
            // 床とちらつかないよう少し浮かせる
            inst.glow.transform.localPosition = Vector3.up * 0.02f;
            inst.ring.transform.localPosition = Vector3.up * 0.03f;

            if (m_lightIntensity > 0.0f)
            {
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = Vector3.up * 0.8f;
                inst.light = lightGo.AddComponent<Light>();
                inst.light.type = LightType.Point;
                inst.light.color = color;
                inst.light.range = m_lightRange;
                inst.light.intensity = 0.0f;
                inst.light.shadows = LightShadows.None;
            }

            m_instances.Add(inst);
            Apply(inst);
        }

        private void Update()
        {
            for (int i = m_instances.Count - 1; i >= 0; i--)
            {
                var inst = m_instances[i];
                if (inst.root == null) { m_instances.RemoveAt(i); continue; }
                inst.time += Time.deltaTime;
                if (inst.time >= inst.duration + m_flashTime)
                {
                    Destroy(inst.root.gameObject);
                    m_instances.RemoveAt(i);
                    continue;
                }
                Apply(inst);
            }
        }

        private void Apply(Instance inst)
        {
            float r = m_radius;
            if (inst.time < inst.duration)
            {
                // 溜め: 柱が伸びながら細く集まり、明るさが増していく(ちらつきながら)
                float t = inst.time / inst.duration;
                float grow = 1.0f - Mathf.Pow(1.0f - Mathf.Clamp01(t / 0.35f), 3.0f);
                float flicker = 0.85f + 0.15f * Mathf.Sin(inst.time * 40.0f);
                float a = Mathf.Lerp(0.25f, 0.9f, t) * flicker;

                SetTube(inst.pillar, r * Mathf.Lerp(1.25f, 0.9f, t), m_height * grow, inst.color, a * 0.6f);
                SetTube(inst.core, r * 0.35f, m_height * grow * 1.1f, Color.white, a * Mathf.Lerp(0.2f, 0.9f, t));
                SetFlat(inst.glow, r * Mathf.Lerp(0.6f, 1.3f, t), inst.color, a * 0.7f);
                // 輪は外から足元へ縮んでくる
                SetFlat(inst.ring, r * Mathf.Lerp(2.4f, 1.0f, EaseOut(t)), inst.color, Mathf.Clamp01(t * 3.0f) * 0.9f);
                if (inst.light != null) inst.light.intensity = m_lightIntensity * t * flicker;
            }
            else
            {
                // 閃光: 柱が太くなって消え、輪が外へ広がる
                float t = Mathf.Clamp01((inst.time - inst.duration) / m_flashTime);
                float fade = 1.0f - t;
                SetTube(inst.pillar, r * Mathf.Lerp(0.9f, 1.8f, EaseOut(t)), m_height * Mathf.Lerp(1.0f, 1.3f, t), inst.color, fade * fade);
                SetTube(inst.core, r * Mathf.Lerp(0.35f, 1.0f, EaseOut(t)), m_height * 1.1f, Color.white, fade * fade);
                SetFlat(inst.glow, r * Mathf.Lerp(1.3f, 2.0f, t), Color.white, fade);
                SetFlat(inst.ring, r * Mathf.Lerp(1.0f, m_flashSpread, EaseOut(t)), inst.color, fade);
                if (inst.light != null) inst.light.intensity = m_lightIntensity * 2.0f * fade;
            }
        }

        private static void SetTube(Renderer r, float radius, float height, Color c, float alpha)
        {
            r.transform.localScale = new Vector3(radius, Mathf.Max(0.001f, height), radius);
            c.a = alpha;
            EffectMeshes.SetColor(r, c);
        }

        private static void SetFlat(Renderer r, float radius, Color c, float alpha)
        {
            r.transform.localScale = new Vector3(radius, 1.0f, radius);
            c.a = alpha;
            EffectMeshes.SetColor(r, c);
        }

        private static float EaseOut(float t) => 1.0f - (1.0f - t) * (1.0f - t);
    }
}
