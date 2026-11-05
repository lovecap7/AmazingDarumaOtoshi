using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // キャラクターと攻撃判定のある積み木に、プレイヤー番号ごとの色の輪郭を付ける
    //  ・キャラクター(生存中の体。積まれている積み木も含む)は、そのプレイヤーの番号の色
    //  ・攻撃判定のある(飛んでいる)積み木は、撃った人(TumikiBlock.Owner)の番号の色
    //    ギミックが撃った(持ち主がいない・だれのものか分からない)積み木は、だれにも割り振られない色(m_neutralColor)
    //  ・攻撃判定がなくなったら輪郭は消える
    // 積み木は毎フレーム状態(TumikiBlock.IsHarmful)を見て決めるので、どのギミック・技で飛ばされても(今後増えても)
    // 攻撃判定があれば輪郭が付く。ギミック側で何かする必要はない
    // 積み木以外の攻撃する物(新しい弾など)に付けたいときは、IPlayerOutlineTarget を実装したコンポーネントを付ける(自動で探す)
    // 輪郭は描画中のレンダラーのマテリアルの最後に輪郭用のマテリアルを足して描く(元のマテリアル・プレハブは変えない)
    // レンダラーの表示・非表示(無敵の点滅・開始演出で隠す など)にはそのまま従う
    // 輪郭を付けたくない物は、その物か親に OutlineExclude を付ける(王冠など)
    // DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    [DefaultExecutionOrder(100)]
    public class PlayerOutlines : MonoBehaviour
    {
        public static bool AutoAttach = true;
        public static PlayerOutlines Current { get; private set; }

        const string ShaderResource = "NakagawaOutline";
        static readonly int s_colorId = Shader.PropertyToID("_OutlineColor");
        static readonly int s_widthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int s_blendId = Shader.PropertyToID("_RadialBlend");
        static readonly int s_centerId = Shader.PropertyToID("_Center");

        [Header("色")]
        // プレイヤー番号(1P, 2P, …)ごとの色。足りなければ最初から繰り返す
        [SerializeField] Color[] m_playerColors =
        {
            new Color(0.93f, 0.27f, 0.25f),   // 1P 赤
            new Color(0.22f, 0.55f, 0.95f),   // 2P 青
            new Color(0.98f, 0.76f, 0.12f),   // 3P 黄
            new Color(0.30f, 0.74f, 0.35f),   // 4P 緑
            new Color(0.96f, 0.55f, 0.15f),
            new Color(0.40f, 0.85f, 0.85f),
            new Color(0.85f, 0.45f, 0.80f),
            new Color(0.55f, 0.45f, 0.35f),
        };
        // だれにも割り振られない色(ギミックが撃った積み木など)。明るい床・炎の上でも見えるよう暗い色にする
        [SerializeField] Color m_neutralColor = new Color(0.12f, 0.1f, 0.16f);

        [Header("太さ(画面の高さ1080でのピクセル)")]
        [SerializeField, Range(0.0f, 20.0f)] float m_characterWidth = 5.0f;
        // 攻撃判定のある積み木はキャラクターより太くして目立たせる
        [SerializeField, Range(0.0f, 20.0f)] float m_blockWidth = 9.0f;
        // 0 = 法線の向きに広げる / 1 = 形の中心から外向きに広げる(角のある形の隙間を埋める)
        [SerializeField, Range(0.0f, 1.0f)] float m_radialBlend = 0.6f;

        [Header("距離による太さ")]
        // カメラから遠いほど細く、近いほど太くする
        [SerializeField] bool m_scaleByDistance = true;
        // 上の太さになるカメラからの距離(m)。0 ならカメラからステージの中心までの距離
        [SerializeField, Min(0.0f)] float m_referenceDistance = 0.0f;
        // 太さの倍率の下限・上限
        [SerializeField, Min(0.0f)] float m_minDistanceScale = 0.4f;
        [SerializeField, Min(0.0f)] float m_maxDistanceScale = 1.8f;

        [Header("キャラクターの輪郭")]
        // 体の外側だけに輪郭を出す(積んだ積み木どうし・頭との境目には出さない)
        [SerializeField] bool m_characterSilhouetteOnly = true;

        [Header("対象")]
        // キャラクターに積まれている積み木にも、キャラクターと同じ色の輪郭を付ける
        [SerializeField] bool m_includeStackedBlocks = true;
        // 幽霊にも付ける
        [SerializeField] bool m_includeGhosts = false;
        // 攻撃判定のある積み木に付ける
        [SerializeField] bool m_includeHarmfulBlocks = true;
        // これ以上の描画順(半透明)のマテリアルを使うレンダラーには付けない
        [SerializeField] int m_maxRenderQueue = 2500;

        struct Applied
        {
            public Color color;
            public float width;
            // キャラクターの体(外側だけに輪郭を出す)
            public bool silhouette;

            public bool Same(Applied o) => color == o.color && width == o.width && silhouette == o.silhouette;
        }

        const string MaskShaderResource = "NakagawaOutlineMask";
        static readonly int s_refDistanceId = Shader.PropertyToID("_RefDistance");
        static readonly int s_minScaleId = Shader.PropertyToID("_MinScale");
        static readonly int s_maxScaleId = Shader.PropertyToID("_MaxScale");
        static readonly int s_stencilCompId = Shader.PropertyToID("_StencilComp");

        // 輪郭(いつも描く) / 輪郭(体の印が無いところだけ描く) / 体の印を書く
        static Material s_material, s_silhouetteMaterial, s_maskMaterial;
        static MaterialPropertyBlock s_mpb;

        DarumaMatch m_match;
        readonly Dictionary<Renderer, Applied> m_applied = new Dictionary<Renderer, Applied>();
        readonly Dictionary<Renderer, Applied> m_desired = new Dictionary<Renderer, Applied>();
        readonly List<Renderer> m_remove = new List<Renderer>();
        readonly List<Renderer> m_renderers = new List<Renderer>();
        readonly List<Material> m_mats = new List<Material>();
        // 体(DarumaCharacter / DarumaGhost)→ プレイヤー番号。体が消えたあとに残った積み木の持ち主も分かるよう、試合の間は覚えておく
        readonly Dictionary<object, int> m_bodyIndex = new Dictionary<object, int>(RefComparer.Instance);

        // ---- 外から読む ----
        public Color NeutralColor => m_neutralColor;

        // プレイヤー番号(0 = 1P)の色。負なら だれにも割り振られない色
        public Color ColorForIndex(int index)
        {
            if (index < 0 || m_playerColors == null || m_playerColors.Length == 0) return m_neutralColor;
            return m_playerColors[index % m_playerColors.Length];
        }

        // この積み木を撃った人のプレイヤー番号(分からなければ -1)
        public int OwnerIndexOf(TumikiBlock block) => block != null ? IndexOfAttacker(block.Owner) : -1;

        // 攻撃した人(キャラクター・幽霊)のプレイヤー番号(分からなければ -1)。消えた体でも、この試合で見た体なら分かる
        public int IndexOfAttacker(IDarumaAttacker attacker)
        {
            if (attacker == null) return -1;
            if (m_bodyIndex.TryGetValue(attacker, out int index)) return index;
            // 体の子に付いた部品が持ち主になっている場合
            if (DarumaAttackerUtil.IsAlive(attacker))
            {
                var c = attacker.transform.GetComponentInParent<DarumaCharacter>();
                if (c != null && m_bodyIndex.TryGetValue(c, out index)) return index;
                var g = attacker.transform.GetComponentInParent<DarumaGhost>();
                if (g != null && m_bodyIndex.TryGetValue(g, out index)) return index;
            }
            return -1;
        }

        // 攻撃した人の色(分からなければ だれにも割り振られない色)
        public Color ColorOfAttacker(IDarumaAttacker attacker) => ColorForIndex(IndexOfAttacker(attacker));

        // ---------------- 自動で付ける ----------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
        }

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
            if (match == null || match.GetComponent<PlayerOutlines>() != null) return;
            match.gameObject.AddComponent<PlayerOutlines>();
        }

        // ---------------- 毎フレーム ----------------
        private void OnEnable()
        {
            Current = this;
            m_match = GetComponent<DarumaMatch>();
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            foreach (var kv in m_applied) RemoveOutline(kv.Key);
            m_applied.Clear();
        }

        private void LateUpdate()
        {
            if (m_match == null) m_match = DarumaMatch.Current;
            if (m_match == null || !EnsureMaterial()) return;
            UpdateDistanceScale();

            m_desired.Clear();
            CollectCharacters();
            if (m_includeHarmfulBlocks) CollectBlocks();
            CollectTargets();

            // 付ける・色を変える(他の処理でマテリアルが差し替えられていたら付け直す)
            foreach (var kv in m_desired)
            {
                bool changed = !m_applied.TryGetValue(kv.Key, out Applied prev) || !prev.Same(kv.Value);
                ApplyOutline(kv.Key, kv.Value, changed);
                m_applied[kv.Key] = kv.Value;
            }

            // 対象から外れたものの輪郭を消す
            m_remove.Clear();
            foreach (var kv in m_applied)
            {
                if (!m_desired.ContainsKey(kv.Key)) m_remove.Add(kv.Key);
            }
            foreach (var r in m_remove)
            {
                RemoveOutline(r);
                m_applied.Remove(r);
            }
        }

        private void CollectCharacters()
        {
            foreach (var p in m_match.Players)
            {
                if (p == null) continue;
                Applied a = new Applied { color = ColorForIndex(p.Index), width = m_characterWidth, silhouette = m_characterSilhouetteOnly };
                if (p.Character != null)
                {
                    m_bodyIndex[p.Character] = p.Index;
                    AddRenderers(p.Character.transform, a, p.Character);
                }
                if (p.Ghost != null)
                {
                    m_bodyIndex[p.Ghost] = p.Index;
                    if (m_includeGhosts) AddRenderers(p.Ghost.transform, a, null);
                }
            }
        }

        private void CollectBlocks()
        {
            foreach (var b in TumikiBlock.All)
            {
                if (b == null || b.CurrentState != TumikiBlock.State.Flying || !b.IsHarmful) continue;
                Applied a = new Applied { color = ColorForIndex(OwnerIndexOf(b)), width = m_blockWidth };
                AddRenderers(b.transform, a, null);
            }
        }

        // IPlayerOutlineTarget を実装した物(積み木以外の弾など)
        private void CollectTargets()
        {
            PlayerOutlineTargets.Prune();
            foreach (var t in PlayerOutlineTargets.All)
            {
                if (!t.WantsOutline) continue;
                var c = t as Component;
                if (c == null) continue;
                Applied a = new Applied
                {
                    color = ColorForIndex(IndexOfAttacker(t.OutlineOwner)),
                    width = t.OutlineWidth > 0.0f ? t.OutlineWidth : m_blockWidth,
                };
                AddRenderers(c.transform, a, null);
            }
        }

        // character: キャラクターの体を集めるとき(積まれている積み木の扱いを決める)
        private void AddRenderers(Transform root, Applied a, DarumaCharacter character)
        {
            root.GetComponentsInChildren(false, m_renderers);
            foreach (var r in m_renderers)
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<OutlineExclude>(true) != null) continue;
                if (!IsOpaque(r)) continue;
                if (character != null)
                {
                    var block = r.GetComponentInParent<TumikiBlock>();
                    if (block != null && !m_includeStackedBlocks) continue;
                }
                m_desired[r] = a;
            }
        }

        private bool IsOpaque(Renderer r)
        {
            r.GetSharedMaterials(m_mats);
            for (int i = 0; i < m_mats.Count; i++)
            {
                var m = m_mats[i];
                if (m == null || IsOurs(m)) continue;
                return m.renderQueue < m_maxRenderQueue;
            }
            return false;
        }

        // ---------------- 輪郭のマテリアル ----------------
        private static bool IsOurs(Material m) => m != null && (m == s_material || m == s_silhouetteMaterial || m == s_maskMaterial);

        private static bool EnsureMaterial()
        {
            if (s_material != null && s_silhouetteMaterial != null && s_maskMaterial != null) return true;
            var shader = Resources.Load<Shader>(ShaderResource);
            var maskShader = Resources.Load<Shader>(MaskShaderResource);
            if (shader == null || !shader.isSupported || maskShader == null || !maskShader.isSupported) return false;
            s_material = new Material(shader) { name = "PlayerOutline (Runtime)", hideFlags = HideFlags.DontSave };
            s_material.SetFloat(s_stencilCompId, (float)UnityEngine.Rendering.CompareFunction.Always);
            s_silhouetteMaterial = new Material(shader) { name = "PlayerOutline Silhouette (Runtime)", hideFlags = HideFlags.DontSave };
            s_silhouetteMaterial.SetFloat(s_stencilCompId, (float)UnityEngine.Rendering.CompareFunction.NotEqual);
            s_maskMaterial = new Material(maskShader) { name = "PlayerOutline Mask (Runtime)", hideFlags = HideFlags.DontSave };
            s_mpb = new MaterialPropertyBlock();
            return true;
        }

        // 距離による太さの設定を輪郭のマテリアルに渡す(基準の距離が 0 ならカメラからステージの中心まで)
        private void UpdateDistanceScale()
        {
            float reference = 0.0f;
            if (m_scaleByDistance)
            {
                reference = m_referenceDistance;
                if (reference <= 0.0f)
                {
                    var cam = Camera.main;
                    Vector3 center = m_match.StageArea != null ? m_match.StageArea.transform.position : Vector3.zero;
                    reference = cam != null ? Vector3.Distance(cam.transform.position, center) : 0.0f;
                }
            }
            foreach (var m in new[] { s_material, s_silhouetteMaterial })
            {
                m.SetFloat(s_refDistanceId, reference);
                m.SetFloat(s_minScaleId, m_minDistanceScale);
                m.SetFloat(s_maxScaleId, Mathf.Max(m_minDistanceScale, m_maxDistanceScale));
            }
        }

        private void ApplyOutline(Renderer r, Applied a, bool changed)
        {
            if (r == null) return;
            Material outline = a.silhouette ? s_silhouetteMaterial : s_material;
            r.GetSharedMaterials(m_mats);
            int count = m_mats.Count;
            // 最後が [体の印, 輪郭](外側だけ) / [輪郭] になっているか
            bool ok = a.silhouette
                ? count >= 2 && m_mats[count - 1] == outline && m_mats[count - 2] == s_maskMaterial && CountOurs() == 2
                : count >= 1 && m_mats[count - 1] == outline && CountOurs() == 1;
            if (!ok)
            {
                // 途中に残っていたら外してから最後に足す(他の処理でマテリアルの並びが変わった・種類が変わった)
                ClearOurBlocks(r);
                m_mats.RemoveAll(IsOurs);
                if (a.silhouette) m_mats.Add(s_maskMaterial);
                m_mats.Add(outline);
                r.SetSharedMaterials(m_mats);
                changed = true;
            }
            if (!changed) return;

            s_mpb.Clear();
            s_mpb.SetColor(s_colorId, a.color);
            s_mpb.SetFloat(s_widthId, a.width);
            s_mpb.SetFloat(s_blendId, m_radialBlend);
            s_mpb.SetVector(s_centerId, MeshCenter(r));
            r.SetPropertyBlock(s_mpb, m_mats.Count - 1);
        }

        private int CountOurs()
        {
            int n = 0;
            foreach (var m in m_mats) if (IsOurs(m)) n++;
            return n;
        }

        // m_mats(いまの並び)のうち、輪郭のマテリアルに付けた個別の設定を消す
        private void ClearOurBlocks(Renderer r)
        {
            for (int i = 0; i < m_mats.Count; i++)
            {
                if (IsOurs(m_mats[i])) r.SetPropertyBlock(null, i);
            }
        }

        private void RemoveOutline(Renderer r)
        {
            if (r == null || s_material == null) return;
            r.GetSharedMaterials(m_mats);
            if (CountOurs() == 0) return;
            ClearOurBlocks(r);
            m_mats.RemoveAll(IsOurs);
            r.SetSharedMaterials(m_mats);
        }

        private static Vector3 MeshCenter(Renderer r)
        {
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (r.TryGetComponent(out MeshFilter mf)) mesh = mf.sharedMesh;
            return mesh != null ? mesh.bounds.center : Vector3.zero;
        }

        // 消えた体も見分けられるよう、Unity の == ではなく参照で比べる
        sealed class RefComparer : IEqualityComparer<object>
        {
            public static readonly RefComparer Instance = new RefComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
