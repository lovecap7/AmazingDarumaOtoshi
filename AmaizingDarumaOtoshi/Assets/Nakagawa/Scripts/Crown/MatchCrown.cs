using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // 試合中の王冠
    //  ・前の試合の勝者(CrownKeeper の持ち主)が、ゲームを抜けずに次の試合にも出ていれば、その人の頭に王冠を乗せて始める
    //  ・王冠をかぶっている人が攻撃で積み木を崩される(DarumaCharacter.Damaged)か、やられると、王冠が飛んで床に落ちる
    //  ・落ちている王冠に生きているキャラクターが触れると、その人の頭に乗る(落ちた直後は少しの間だれも拾えない)
    //  ・場外に落ちたら、ステージの真ん中の上から降ってくる
    //  ・試合で勝者が決まったら、その人を次の王冠の持ち主にする(CrownKeeper)
    // 王冠は物理の当たり判定を持たない(積み木やキャラクターの動きには影響しない)。床は下向きの判定で探す
    // DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    [DefaultExecutionOrder(-30)]   // 開始のカウントダウン演出(キャラクターを隠す)より先に王冠をかぶせる
    public class MatchCrown : MonoBehaviour
    {
        public static bool AutoAttach = true;

        enum State { None, Worn, Loose }

        [Header("見た目")]
        [SerializeField, Min(0.1f)] float m_size = 0.7f;
        [SerializeField] Color m_gold = new Color(1.0f, 0.78f, 0.18f);
        [SerializeField] Color m_gem = new Color(0.9f, 0.15f, 0.25f);
        // 頭の上に乗せる高さ(頭の半径に対する割合)
        [SerializeField] float m_headOffset = 0.8f;
        // 少し傾けてかぶせる角度
        [SerializeField] float m_tilt = 10.0f;

        [Header("落ちる")]
        [SerializeField] float m_popUpSpeed = 7.0f;
        [SerializeField] float m_popSideSpeed = 2.5f;
        [SerializeField] float m_gravity = 22.0f;
        // 落ちてからだれも拾えない時間(秒)
        [SerializeField, Min(0.0f)] float m_pickupDelay = 0.7f;
        // 床に落ちている間の回転の速さ(度/秒)と上下の揺れ
        [SerializeField] float m_spinSpeed = 90.0f;
        [SerializeField] float m_bobHeight = 0.08f;

        [Header("拾う")]
        // 体の中心からこの距離(水平)以内に入ったら拾う
        [SerializeField, Min(0.1f)] float m_pickupRadius = 0.85f;

        [Header("場外")]
        // ステージの範囲の中心よりこれだけ下に落ちたら、ステージの真ん中の上から降らせる
        [SerializeField] float m_fallLimit = 8.0f;
        [SerializeField] float m_respawnHeight = 6.0f;

        [Header("ネット対戦")]
        [SerializeField] bool m_playInNetMatch = false;

        static readonly RaycastHit[] s_hits = new RaycastHit[16];
        static Material s_goldMat, s_gemMat;

        DarumaMatch m_match;
        State m_state = State.None;
        Transform m_crown;
        MatchPlayer m_ownerPlayer;
        DarumaCharacter m_owner;
        Vector3 m_velocity;
        // 落ちている王冠の位置(見た目の揺れを足す前)
        Vector3 m_loosePos;
        float m_looseTime;
        // 床に落ちて止まっている(動く足場の上なら一緒に動く)
        Transform m_ground;
        Vector3 m_groundLocal;
        bool m_grounded;
        bool m_started;
        readonly List<Transform> m_bodies = new List<Transform>();

        public bool IsWorn => m_state == State.Worn;
        // いま王冠をかぶっている人(いなければ null)
        public MatchPlayer Wearer => m_state == State.Worn ? m_ownerPlayer : null;

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
            if (match == null || FindAnyObjectByType<MatchCrown>() != null) return;
            match.gameObject.AddComponent<MatchCrown>();
        }

        private void OnEnable()
        {
            m_match = GetComponent<DarumaMatch>();
            if (m_match == null) m_match = DarumaMatch.Current;
            if (m_match == null) return;
            m_match.Finished += OnFinished;
            m_match.PlayerJoined += OnJoined;
        }

        private void OnDisable()
        {
            if (m_match != null)
            {
                m_match.Finished -= OnFinished;
                m_match.PlayerJoined -= OnJoined;
            }
            Unhook();
        }

        private void OnDestroy()
        {
            if (m_crown != null) Destroy(m_crown.gameObject);
        }

        private void Start()
        {
            if (m_match == null) { enabled = false; return; }
            if (m_match.IsNetworked && !m_playInNetMatch) { enabled = false; return; }
            m_started = true;
            GiveToHolder();
        }

        // 試合に出ている持ち主に王冠をかぶせる。セレクトから始めた試合に持ち主が出ていなければ、持ち主はいなくなる
        private void GiveToHolder()
        {
            if (!CrownKeeper.HasHolder || m_state != State.None) return;
            foreach (var p in m_match.Players)
            {
                if (!CrownKeeper.IsHolder(p)) continue;
                m_ownerPlayer = p;
                if (p.Character != null) Wear(p, p.Character);
                return;
            }
            if (m_match.IsConfigured) CrownKeeper.Clear();
        }

        // 直接再生で途中から参加した人が持ち主なら、かぶせる
        private void OnJoined(MatchPlayer p)
        {
            if (!m_started || m_state != State.None || !CrownKeeper.IsHolder(p)) return;
            m_ownerPlayer = p;
            if (p.Character != null) Wear(p, p.Character);
        }

        private void OnFinished(DarumaMatch.MatchResult result)
        {
            if (result != null && result.Winner != null) CrownKeeper.SetHolder(result.Winner);
        }

        private void LateUpdate()
        {
            if (m_state == State.None)
            {
                // 持ち主の体がまだ出ていなかった(復活待ちなど)
                if (m_ownerPlayer != null && m_ownerPlayer.Character != null) Wear(m_ownerPlayer, m_ownerPlayer.Character);
                return;
            }

            // ステージの切り替えなどで体ごと消えた: 持ち主の新しい体にかぶせ直す(いなければ真ん中に降らせる)
            if (m_crown == null)
            {
                Unhook();
                m_crown = null;
                var player = m_ownerPlayer;
                m_state = State.None;
                if (player != null && player.Character != null) Wear(player, player.Character);
                else DropAt(StageCenter() + Vector3.up * m_respawnHeight, Vector3.zero);
                return;
            }

            if (m_state == State.Worn) UpdateWorn();
            else UpdateLoose();
        }

        // ---------------- かぶっている ----------------
        private void Wear(MatchPlayer player, DarumaCharacter character)
        {
            if (character == null) return;
            Unhook();
            if (m_crown == null) m_crown = BuildCrown(m_size, m_gold, m_gem);
            m_ownerPlayer = player;
            m_owner = character;
            m_state = State.Worn;
            m_grounded = false;
            m_ground = null;
            m_owner.Damaged += OnOwnerDamaged;
            m_owner.Eliminated += OnOwnerEliminated;

            // 頭の子にする(体と一緒に隠れる・見える。開始の出現演出など)。位置は毎フレーム頭の上に合わせる
            Transform parent = HeadOf(character);
            m_crown.SetParent(parent, true);
            m_crown.localScale = DivideScale(Vector3.one * m_size, parent.lossyScale);
            UpdateWorn();
        }

        private void UpdateWorn()
        {
            if (m_owner == null) return;
            Transform head = HeadOf(m_owner);
            float radius = m_owner.Stack != null ? m_owner.Stack.HeadRadius : 0.45f;
            Vector3 top = head == m_owner.transform
                ? m_owner.transform.position + Vector3.up * (m_owner.Stack != null ? m_owner.Stack.Height : 1.0f)
                : head.position + Vector3.up * (radius * m_headOffset);
            float yaw = m_owner.transform.eulerAngles.y;
            m_crown.SetPositionAndRotation(top, Quaternion.Euler(0.0f, yaw, 0.0f) * Quaternion.Euler(-m_tilt, 0.0f, 0.0f));
        }

        private void OnOwnerDamaged(DarumaCharacter c)
        {
            if (c != m_owner) return;
            Vector3 away = -c.transform.forward;
            KnockOff(away);
        }

        private void OnOwnerEliminated(DarumaCharacter c)
        {
            if (c != m_owner) return;
            KnockOff(c.EliminationDirection);
        }

        // 頭から飛ばして落とす
        private void KnockOff(Vector3 direction)
        {
            if (m_state != State.Worn || m_crown == null) return;
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Random.insideUnitSphere;
            direction.y = 0.0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            DropAt(m_crown.position, direction * m_popSideSpeed + Vector3.up * m_popUpSpeed);
        }

        // ---------------- 落ちている ----------------
        private void DropAt(Vector3 position, Vector3 velocity)
        {
            Unhook();
            if (m_crown == null) m_crown = BuildCrown(m_size, m_gold, m_gem);
            m_crown.SetParent(null, true);
            m_crown.localScale = Vector3.one * m_size;
            m_crown.position = position;
            m_loosePos = position;
            m_owner = null;
            m_state = State.Loose;
            m_velocity = velocity;
            m_looseTime = 0.0f;
            m_grounded = false;
            m_ground = null;
        }

        private void UpdateLoose()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0f) return;
            m_looseTime += dt;

            if (m_grounded && m_ground != null && m_ground.gameObject.activeInHierarchy)
            {
                // 足場と一緒に動く。足場が無くなった(下がった・離れた)ら落ちる
                Vector3 rest = m_ground.TransformPoint(m_groundLocal);
                if (FindFloor(rest + Vector3.up * 0.3f, 0.45f, out RaycastHit hit)) SetGround(hit);
                else m_grounded = false;
            }
            else m_grounded = false;

            Vector3 basePos = m_grounded ? m_ground.TransformPoint(m_groundLocal) : m_loosePos;
            if (!m_grounded)
            {
                m_velocity.y -= m_gravity * dt;
                Vector3 next = basePos + m_velocity * dt;
                if (m_velocity.y <= 0.0f && FindFloor(basePos + Vector3.up * 0.2f, 0.2f + basePos.y - next.y, out RaycastHit hit))
                {
                    SetGround(hit);
                    m_velocity = Vector3.zero;
                    basePos = m_ground.TransformPoint(m_groundLocal);
                }
                else basePos = next;
            }

            // 場外に落ちたら真ん中の上から降らせる
            Vector3 center = StageCenter();
            if (basePos.y < center.y - m_fallLimit)
            {
                DropAt(center + Vector3.up * m_respawnHeight, Vector3.zero);
                return;
            }

            m_loosePos = basePos;
            float bob = m_grounded ? m_bobHeight * (0.5f + 0.5f * Mathf.Sin(Time.time * 3.0f)) : 0.0f;
            float spin = m_crown.eulerAngles.y + m_spinSpeed * dt * (m_grounded ? 1.0f : 4.0f);
            m_crown.SetPositionAndRotation(basePos + Vector3.up * (0.05f + bob), Quaternion.Euler(0.0f, spin, 0.0f));

            if (m_looseTime >= m_pickupDelay) TryPickup();
        }

        private void SetGround(RaycastHit hit)
        {
            m_grounded = true;
            m_ground = hit.collider.transform;
            m_groundLocal = m_ground.InverseTransformPoint(hit.point);
        }

        // 生きているキャラクターが王冠に触れたら、その人にかぶせる
        private void TryPickup()
        {
            Vector3 pos = m_crown.position;
            MatchPlayer best = null;
            float bestDist = float.MaxValue;
            foreach (var p in m_match.Players)
            {
                if (p == null || p.Character == null || !p.IsAlive) continue;
                var c = p.Character;
                Vector3 d = pos - c.transform.position;
                float height = c.Stack != null ? c.Stack.Height : 1.0f;
                if (d.y < -0.4f || d.y > height + 0.4f) continue;
                d.y = 0.0f;
                float dist = d.magnitude;
                if (dist > m_pickupRadius || dist >= bestDist) continue;
                best = p;
                bestDist = dist;
            }
            if (best != null) Wear(best, best.Character);
        }

        // 下向きに床(積み木・キャラクター・触れるだけの判定以外)を探す
        private bool FindFloor(Vector3 origin, float length, out RaycastHit floor)
        {
            floor = default;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, Mathf.Max(0.01f, length), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var hit = s_hits[i];
                Collider c = hit.collider;
                if (c.GetComponentInParent<TumikiBlock>() != null || c.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (c.GetComponentInParent<DarumaGhost>() != null) continue;
                if (hit.normal.y < 0.5f || hit.distance >= best) continue;
                best = hit.distance;
                floor = hit;
                found = true;
            }
            return found;
        }

        private Vector3 StageCenter()
        {
            var area = m_match != null ? m_match.StageArea : null;
            return area != null ? area.transform.position : Vector3.zero;
        }

        private void Unhook()
        {
            if (m_owner == null) return;
            m_owner.Damaged -= OnOwnerDamaged;
            m_owner.Eliminated -= OnOwnerEliminated;
        }

        private static Transform HeadOf(DarumaCharacter c)
        {
            var head = c.Stack != null ? c.Stack.Head : null;
            return head != null ? head : c.transform;
        }

        private static Vector3 DivideScale(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x / NonZero(b.x), a.y / NonZero(b.y), a.z / NonZero(b.z));
        }

        private static float NonZero(float v) => Mathf.Abs(v) < 0.0001f ? 0.0001f : v;

        // ---------------- 見た目 ----------------
        // 金の輪に5本のとがりと宝石を付けた王冠(大きさ1で幅およそ1。底が原点)
        public static Transform BuildCrown(float size, Color gold, Color gem)
        {
            if (s_goldMat == null)
            {
                s_goldMat = ToyKit.Lit(gold, 0.85f);
                s_goldMat.SetFloat("_Metallic", 0.6f);
                s_goldMat.EnableKeyword("_EMISSION");
                s_goldMat.SetColor("_EmissionColor", gold * 0.25f);
            }
            if (s_gemMat == null)
            {
                s_gemMat = ToyKit.Lit(gem, 0.95f);
                s_gemMat.EnableKeyword("_EMISSION");
                s_gemMat.SetColor("_EmissionColor", gem * 0.4f);
            }

            var root = new GameObject("Crown").transform;
            var band = Part(PrimitiveType.Cylinder, "Band", root);
            band.localScale = new Vector3(0.8f, 0.11f, 0.8f);
            band.localPosition = new Vector3(0.0f, 0.11f, 0.0f);
            SetMat(band, s_goldMat);

            const int spikes = 5;
            for (int i = 0; i < spikes; i++)
            {
                float a = i * Mathf.PI * 2.0f / spikes;
                var dir = new Vector3(Mathf.Sin(a), 0.0f, Mathf.Cos(a));
                var spike = Part(PrimitiveType.Cube, "Spike" + i, root);
                spike.localScale = new Vector3(0.2f, 0.2f, 0.06f);
                spike.localPosition = dir * 0.34f + Vector3.up * 0.3f;
                spike.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0.0f, 0.0f, 45.0f);
                SetMat(spike, s_goldMat);

                var tip = Part(PrimitiveType.Sphere, "Tip" + i, root);
                tip.localScale = Vector3.one * 0.1f;
                tip.localPosition = dir * 0.34f + Vector3.up * 0.46f;
                SetMat(tip, s_goldMat);

                var stone = Part(PrimitiveType.Sphere, "Gem" + i, root);
                stone.localScale = new Vector3(0.11f, 0.11f, 0.06f);
                stone.localPosition = dir * 0.405f + Vector3.up * 0.11f;
                stone.localRotation = Quaternion.LookRotation(dir);
                SetMat(stone, s_gemMat);
            }
            root.localScale = Vector3.one * size;
            return root;
        }

        // 当たり判定のない形(すぐに消さないと、消えるまでの間にキャラクターを押してしまう)
        private static Transform Part(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void SetMat(Transform t, Material mat)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
