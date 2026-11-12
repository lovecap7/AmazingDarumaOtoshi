using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 割れる氷(Stage21「氷割りゲーム」)。六角形の氷のタイルを並べた床
    //  ・プレイヤーがジャンプして氷に着地すると、着地したタイルと隣の6枚の耐久が1ずつ減る
    //  ・耐久が減るほど色が暗くなり(白に近い薄い水色 → くすんだ青灰 → 暗い紺灰)、ひびが増える
    //  ・耐久が0になると、縁が黄色く点滅して小刻みに揺れる(予兆)。そのあと当たり判定が消えて落ちる(上の積み木とプレイヤーも落ちる)
    //  ・崩れ始めてから一定時間(既定20秒)で元の場所に戻る。戻る直前は、縁が黄色く点滅しながら下からせり出してくる(当たり判定は戻りきってから)
    //  ・試合のやり直し・ステージの切り替えでも、すべて元に戻る
    // タイルの形・メッシュ・隣の関係は StageBuilder が作って m_tiles に入れる(このコンポーネントは状態と見た目だけを扱う)
    // タイルは動かない床(Rigidbody なし)なので、積み木の生成エリアは床として扱う。落ちたタイルの上には湧かない
    // 開始のカウントダウン中は割れない。ネット対戦ではホストが判定し、クライアントは状態を受け取って見た目だけ合わせる
    public class IceFloorGimmick : MonoBehaviour, INetGimmickState
    {
        // Reviving: 落ちたタイルが元の場所に戻ってくる途中(当たり判定はまだない)
        public enum TileState { Intact, Warning, Falling, Gone, Reviving }

        [Serializable]
        public class Tile
        {
            // 六角形の格子の座標(軸座標)
            public int Q, R;
            // タイルの本体(この Transform を落とす)。当たり判定・見た目・縁・ひび
            public Transform Root;
            // 見た目(予兆で揺らすのはこちら。当たり判定は揺らさない)
            public Transform Visual;
            public MeshRenderer Body;
            public Collider Collider;
            public Renderer Edge;
            // ひび(耐久が減るごとに1つずつ見せる)
            public Renderer[] Cracks;
            // 隣のタイル(m_tiles の番号)
            public int[] Neighbors;
            // 割れない床(出現地点の床など)。着地しても自分にも隣にもダメージを入れない
            public bool Solid;
            // 上から見た形(凸多角形の組。Root からの相対の X・Z を並べたものと、多角形ごとの頂点の数)。継ぎ目の処理に使う
            public Vector2[] Points;
            public int[] PieceSizes;
            public float Depth = 1.0f;

            [NonSerialized] public int Hp;
            [NonSerialized] public TileState State;
            [NonSerialized] public float Timer;
            [NonSerialized] public Vector3 BasePosition;
            [NonSerialized] public float FallSpeed;
            [NonSerialized] public Vector3 FallSpin;
            [NonSerialized] public float Flash;
            // 崩れ始めてからの時間(復活に使う)
            [NonSerialized] public float DownTime;
        }

        [SerializeField] List<Tile> m_tiles = new List<Tile>();

        [Header("割れ方")]
        [SerializeField, Min(1)] int m_durability = 3;
        // 着地したタイル自身にもダメージを入れるか(入れると、自分の足元も割れていく)
        [SerializeField] bool m_damageLandedTile = true;
        // 隣のタイルにもダメージを入れるか
        [SerializeField] bool m_damageNeighbors = true;
        // この時間(秒)以上空中にいてから着地したものを「ジャンプの着地」とみなす(段差の小さな浮き上がりは数えない)
        [SerializeField, Min(0.0f)] float m_minAirTime = 0.2f;
        // 耐久が0になってから落ちるまでの予兆の時間(秒)
        [SerializeField, Min(0.0f)] float m_warningTime = 1.0f;
        // 落ちる見た目の時間(秒)。過ぎたら見えなくする
        [SerializeField, Min(0.1f)] float m_fallTime = 1.4f;
        [SerializeField] float m_fallGravity = 18.0f;
        // 崩れ始めてから元に戻るまでの時間(秒)。0 なら戻らない
        [SerializeField, Min(0.0f)] float m_reviveTime = 20.0f;
        // 戻る直前に、タイルが黄色い縁で点滅しながらせり出してくる時間(秒。当たり判定は戻りきってから付ける)
        [SerializeField, Min(0.1f)] float m_reviveWarningTime = 1.5f;
        // 戻ったとき: 氷のかけらが少し舞う
        [SerializeField] GimmickBurstSettings m_reviveEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.9f, 0.96f, 1.0f, 1.0f), ColorB = new Color(0.75f, 0.85f, 0.92f, 1.0f),
            Count = 14, Speed = new Vector2(0.6f, 1.8f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.3f, 0.6f),
            Gravity = -0.2f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.9f, 0, 0),
        };

        [Header("色(耐久が多い順)")]
        [SerializeField] Color[] m_hpColors =
        {
            new Color(0.80f, 0.88f, 0.92f, 1.0f),   // 白に近い薄い水色
            new Color(0.56f, 0.62f, 0.68f, 1.0f),   // くすんだ青灰
            new Color(0.33f, 0.36f, 0.43f, 1.0f),   // 暗い紺灰(真っ黒にはしない)
        };
        [SerializeField] Color m_edgeColor = new Color(0.9f, 0.95f, 0.98f, 1.0f);
        [SerializeField] Color m_warningColor = GimmickPalette.Warning;
        // 予兆の点滅の速さ(1秒あたり。始め → 終わり)
        [SerializeField] Vector2 m_blinkRate = new Vector2(4.0f, 10.0f);
        [SerializeField] float m_shakeAmount = 0.05f;
        [SerializeField] float m_shakeFrequency = 32.0f;

        [Header("エフェクト")]
        // ひびが入ったとき: 氷のかけらが少し跳ねる
        [SerializeField] GimmickBurstSettings m_crackEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.9f, 0.96f, 1.0f, 1.0f), ColorB = new Color(0.7f, 0.8f, 0.88f, 1.0f),
            Count = 10, Speed = new Vector2(1.0f, 2.5f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.25f, 0.5f),
            Gravity = 1.0f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.6f, 0, 0),
        };
        // 落ちるとき: 氷のかけらが飛び散る
        [SerializeField] GimmickBurstSettings m_breakEffect = new GimmickBurstSettings
        {
            ColorA = new Color(0.9f, 0.96f, 1.0f, 1.0f), ColorB = new Color(0.55f, 0.62f, 0.7f, 1.0f),
            Count = 26, Speed = new Vector2(1.5f, 4.0f), Size = new Vector2(0.07f, 0.18f), Lifetime = new Vector2(0.35f, 0.7f),
            Gravity = 1.2f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(1.0f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(0.85f, 0.92f, 1.0f, 0.5f), ShockwaveRadius = 1.6f, ShockwaveTime = 0.25f,
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_crackVolume = 0.35f;
        [SerializeField, Range(0.0f, 1.0f)] float m_breakVolume = 0.5f;

        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        readonly Dictionary<Collider, int> m_byCollider = new Dictionary<Collider, int>();
        readonly Dictionary<DarumaCharacter, float> m_airTime = new Dictionary<DarumaCharacter, float>();
        MaterialPropertyBlock m_mpb;
        DarumaMatch m_match;

        public IReadOnlyList<Tile> Tiles => m_tiles;
        // 残っている(落ちていない)タイルの数
        public int RemainingCount
        {
            get
            {
                int n = 0;
                foreach (var t in m_tiles) if (t.State == TileState.Intact || t.State == TileState.Warning) n++;
                return n;
            }
        }

        private void Awake()
        {
            m_mpb = new MaterialPropertyBlock();
            for (int i = 0; i < m_tiles.Count; i++)
            {
                var t = m_tiles[i];
                if (t.Root != null) t.BasePosition = t.Root.localPosition;
                if (t.Collider != null) m_byCollider[t.Collider] = i;
            }
        }

        private void OnEnable()
        {
            m_match = DarumaMatch.Current;
            if (m_match != null) m_match.Started += ResetTiles;
            RegisterSeams();
            ResetTiles();
        }

        private void OnDisable()
        {
            if (m_match != null) m_match.Started -= ResetTiles;
            m_match = null;
            foreach (var t in m_tiles) FloorSeamFilter.UnregisterPrism(t.Collider);
        }

        // タイルの形を、床の継ぎ目の処理(FloorSeamFilter)に登録する(隣のタイルとの境目で積み木が引っかからないように)
        private void RegisterSeams()
        {
            var polys = new List<Vector2[]>();
            foreach (var t in m_tiles)
            {
                if (t.Collider == null || t.Root == null || t.Points == null || t.PieceSizes == null) continue;
                polys.Clear();
                Vector3 origin = t.Root.parent != null ? t.Root.parent.TransformPoint(t.BasePosition) : t.BasePosition;
                int k = 0;
                foreach (int size in t.PieceSizes)
                {
                    var poly = new Vector2[size];
                    for (int i = 0; i < size && k < t.Points.Length; i++, k++) poly[i] = new Vector2(origin.x + t.Points[k].x, origin.z + t.Points[k].y);
                    polys.Add(poly);
                }
                FloorSeamFilter.RegisterPrism(t.Collider, polys, origin.y, origin.y - t.Depth);
            }
        }

        // すべてのタイルを元に戻す
        public void ResetTiles()
        {
            m_airTime.Clear();
            foreach (var t in m_tiles)
            {
                t.Hp = m_durability;
                t.State = TileState.Intact;
                t.Timer = 0.0f;
                t.Flash = 0.0f;
                if (t.Root != null)
                {
                    t.Root.gameObject.SetActive(true);
                    t.Root.localPosition = t.BasePosition;
                    t.Root.localRotation = Quaternion.identity;
                }
                if (t.Collider != null) t.Collider.enabled = true;
                if (t.Visual != null)
                {
                    t.Visual.localPosition = Vector3.zero;
                    t.Visual.localScale = Vector3.one;
                }
                t.DownTime = 0.0f;
                FloorSeamFilter.SetPrismActive(t.Collider, true);
                ApplyLook(t);
            }
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsNetClient) return;
            if (GimmickClock.IsHeld) { m_airTime.Clear(); return; }
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            DetectLandings(Time.fixedDeltaTime);
        }

        private void Update()
        {
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            bool host = !GimmickClock.IsNetClient;
            using var relay = host ? GimmickNet.Relay() : GimmickNet.Mute();
            foreach (var t in m_tiles)
            {
                t.Flash = Mathf.MoveTowards(t.Flash, 0.0f, Time.deltaTime * 4.0f);
                switch (t.State)
                {
                    case TileState.Warning:
                        t.Timer += dt;
                        if (host && t.Timer >= m_warningTime) BeginFall(t);
                        break;
                    case TileState.Falling:
                        t.DownTime += dt;
                        UpdateFall(t, Time.deltaTime);
                        break;
                    case TileState.Gone:
                        t.DownTime += dt;
                        if (host && m_reviveTime > 0.0f && t.DownTime >= m_reviveTime - m_reviveWarningTime) BeginRevive(t);
                        break;
                    case TileState.Reviving:
                        t.DownTime += dt;
                        t.Timer += dt;
                        UpdateRevive(t);
                        if (host && t.Timer >= m_reviveWarningTime) FinishRevive(t);
                        break;
                }
                UpdateWarningLook(t);
            }
        }

        //========================================
        // 着地の判定
        //========================================

        private void DetectLandings(float dt)
        {
            var match = DarumaMatch.Current;
            if (match == null) return;
            foreach (var p in match.Players)
            {
                if (p == null) continue;
                DarumaCharacter c = p.Character;
                if (c == null || !c.CanBeAffected) continue;
                m_airTime.TryGetValue(c, out float air);
                if (!c.IsGrounded)
                {
                    m_airTime[c] = air + dt;
                    continue;
                }
                m_airTime[c] = 0.0f;
                if (air < m_minAirTime) continue;
                int index = TileUnder(c.transform.position);
                if (index >= 0) OnLanded(index);
            }
        }

        // 足元のタイル(なければ -1)
        private int TileUnder(Vector3 feet)
        {
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.4f, Vector3.down, s_hits, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int found = -1;
            for (int i = 0; i < n; i++)
            {
                if (!m_byCollider.TryGetValue(s_hits[i].collider, out int index)) continue;
                if (s_hits[i].distance < best)
                {
                    best = s_hits[i].distance;
                    found = index;
                }
            }
            return found;
        }

        // ジャンプして index のタイルに着地した
        public void OnLanded(int index)
        {
            if (index < 0 || index >= m_tiles.Count) return;
            var landed = m_tiles[index];
            if (landed.Solid) return;
            if (m_damageLandedTile) Damage(landed);
            if (m_damageNeighbors && landed.Neighbors != null)
            {
                foreach (int n in landed.Neighbors) if (n >= 0 && n < m_tiles.Count) Damage(m_tiles[n]);
            }
        }

        private void Damage(Tile t)
        {
            if (t.Solid || t.State != TileState.Intact || t.Hp <= 0) return;
            t.Hp--;
            t.Flash = 1.0f;
            ApplyLook(t);
            Vector3 top = TopCenter(t);
            GimmickBurst.Play(m_crackEffect, top);
            GimmickSound.Play(GimmickSound.Pop, m_crackVolume, UnityEngine.Random.Range(1.3f, 1.6f));
            if (t.Hp <= 0)
            {
                t.State = TileState.Warning;
                t.Timer = 0.0f;
            }
        }

        //========================================
        // 崩落
        //========================================

        // 元の場所に戻り始める(見た目だけ。下から小さくせり出し、縁が黄色く点滅する)
        private void BeginRevive(Tile t)
        {
            t.State = TileState.Reviving;
            t.Timer = 0.0f;
            if (t.Root != null)
            {
                t.Root.gameObject.SetActive(true);
                t.Root.localPosition = t.BasePosition;
                t.Root.localRotation = Quaternion.identity;
            }
            if (t.Collider != null) t.Collider.enabled = false;
            t.Hp = m_durability;
            ApplyLook(t);
            UpdateRevive(t);
        }

        private void UpdateRevive(Tile t)
        {
            if (t.Visual == null) return;
            float k = Mathf.Clamp01(t.Timer / m_reviveWarningTime);
            float s = Mathf.SmoothStep(0.2f, 1.0f, k);
            t.Visual.localScale = new Vector3(s, 1.0f, s);
            t.Visual.localPosition = Vector3.down * (0.6f * (1.0f - k));
        }

        // 戻りきった: 当たり判定を戻す
        private void FinishRevive(Tile t)
        {
            t.State = TileState.Intact;
            t.Timer = 0.0f;
            t.DownTime = 0.0f;
            t.Hp = m_durability;
            if (t.Visual != null)
            {
                t.Visual.localScale = Vector3.one;
                t.Visual.localPosition = Vector3.zero;
            }
            if (t.Collider != null) t.Collider.enabled = true;
            FloorSeamFilter.SetPrismActive(t.Collider, true);
            t.Flash = 1.0f;
            ApplyLook(t);
            GimmickBurst.Play(m_reviveEffect, TopCenter(t));
            GimmickSound.Play(GimmickSound.Pop, m_crackVolume, 1.1f);
        }

        private void BeginFall(Tile t)
        {
            t.State = TileState.Falling;
            t.Timer = 0.0f;
            t.DownTime = 0.0f;
            t.FallSpeed = 0.0f;
            t.FallSpin = new Vector3(UnityEngine.Random.Range(-40.0f, 40.0f), 0.0f, UnityEngine.Random.Range(-40.0f, 40.0f));
            if (t.Collider != null) t.Collider.enabled = false;
            FloorSeamFilter.SetPrismActive(t.Collider, false);
            if (t.Visual != null) t.Visual.localPosition = Vector3.zero;
            GimmickBurst.Play(m_breakEffect, TopCenter(t));
            AudioClip clip = GimmickSound.Pop;
            GimmickSound.Play(clip, m_breakVolume, 0.55f);
            ApplyLook(t);
        }

        private void UpdateFall(Tile t, float dt)
        {
            if (t.Root == null) return;
            t.Timer += dt;
            t.FallSpeed += m_fallGravity * dt;
            t.Root.localPosition += Vector3.down * (t.FallSpeed * dt);
            t.Root.localRotation = Quaternion.Euler(t.FallSpin * t.Timer);
            if (t.Timer >= m_fallTime)
            {
                t.State = TileState.Gone;
                t.Root.gameObject.SetActive(false);
            }
        }

        //========================================
        // 見た目
        //========================================

        private Vector3 TopCenter(Tile t) => t.Root != null ? t.Root.position + Vector3.up * 0.05f : transform.position;

        private Color HpColor(int hp)
        {
            if (m_hpColors == null || m_hpColors.Length == 0) return Color.white;
            // 耐久が満タンなら0番目、1なら最後
            int i = Mathf.Clamp(m_durability - Mathf.Max(1, hp), 0, m_hpColors.Length - 1);
            return m_hpColors[i];
        }

        private void ApplyLook(Tile t)
        {
            if (m_mpb == null) m_mpb = new MaterialPropertyBlock();
            // 割れない床は床の見た目のまま
            if (t.Solid) return;
            if (t.Body != null)
            {
                Color c = HpColor(t.Hp);
                if (t.Flash > 0.0f) c = Color.Lerp(c, Color.white, t.Flash * 0.5f);
                t.Body.GetPropertyBlock(m_mpb);
                m_mpb.SetColor(s_baseColorId, c);
                t.Body.SetPropertyBlock(m_mpb);
            }
            if (t.Cracks != null)
            {
                int lost = m_durability - t.Hp;
                for (int i = 0; i < t.Cracks.Length; i++)
                {
                    if (t.Cracks[i] != null) t.Cracks[i].enabled = i < lost;
                }
            }
        }

        // 予兆(縁の点滅・揺れ)と、ひびが入った直後の光
        private void UpdateWarningLook(Tile t)
        {
            if (t.Flash > 0.0f) ApplyLook(t);
            if (t.Edge != null)
            {
                Color edge = m_edgeColor;
                if (t.State == TileState.Reviving)
                {
                    edge = Color.Lerp(m_edgeColor, m_warningColor, GimmickPalette.Blink(t.Timer, Mathf.Lerp(m_blinkRate.x, m_blinkRate.y, t.Timer / m_reviveWarningTime)));
                }
                else if (t.State == TileState.Warning)
                {
                    float progress = m_warningTime > 0.0f ? Mathf.Clamp01(t.Timer / m_warningTime) : 1.0f;
                    float rate = Mathf.Lerp(m_blinkRate.x, m_blinkRate.y, progress);
                    edge = Color.Lerp(m_edgeColor, m_warningColor, GimmickPalette.Blink(t.Timer, rate));
                }
                t.Edge.GetPropertyBlock(m_mpb);
                m_mpb.SetColor(s_baseColorId, edge);
                t.Edge.SetPropertyBlock(m_mpb);
            }
            // 予兆の揺れは見た目だけ(当たり判定は動かさない)
            Transform shaken = t.Visual;
            if (shaken == null) return;
            if (t.State == TileState.Warning)
            {
                float time = Time.time * m_shakeFrequency;
                shaken.localPosition = new Vector3(Mathf.PerlinNoise(time, t.Q * 0.37f) - 0.5f, 0.0f, Mathf.PerlinNoise(t.R * 0.53f, time) - 0.5f) * (m_shakeAmount * 2.0f);
            }
            else if (t.State == TileState.Intact) shaken.localPosition = Vector3.zero;
        }

        //========================================
        // ネット対戦用(suehara): タイルの耐久・状態をホストから受け取る
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            s.AddInt(m_tiles.Count);
            foreach (var t in m_tiles)
            {
                s.AddInt(t.Hp);
                s.AddInt((int)t.State);
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            int n = s.Int();
            if (!s.Ok || n != m_tiles.Count) return;
            for (int i = 0; i < n; i++)
            {
                int hp = s.Int();
                var state = (TileState)s.Int();
                if (!s.Ok) return;
                var t = m_tiles[i];
                if (hp != t.Hp)
                {
                    if (hp < t.Hp) t.Flash = 1.0f;
                    t.Hp = hp;
                    ApplyLook(t);
                }
                if (state == t.State) continue;
                // 元に戻った(試合のやり直し)
                if (state == TileState.Intact && t.State != TileState.Intact)
                {
                    t.State = TileState.Intact;
                    if (t.Root != null)
                    {
                        t.Root.gameObject.SetActive(true);
                        t.Root.localPosition = t.BasePosition;
                        t.Root.localRotation = Quaternion.identity;
                    }
                    if (t.Collider != null) t.Collider.enabled = true;
                    if (t.Visual != null)
                    {
                        t.Visual.localPosition = Vector3.zero;
                        t.Visual.localScale = Vector3.one;
                    }
                    FloorSeamFilter.SetPrismActive(t.Collider, true);
                    ApplyLook(t);
                    continue;
                }
                // 戻り始めた
                if (state == TileState.Reviving)
                {
                    BeginRevive(t);
                    continue;
                }
                if (state == TileState.Warning && t.State == TileState.Intact)
                {
                    t.State = TileState.Warning;
                    t.Timer = 0.0f;
                }
                else if ((state == TileState.Falling || state == TileState.Gone) && (t.State == TileState.Intact || t.State == TileState.Warning))
                {
                    using (GimmickNet.Mute()) BeginFall(t);
                }
            }
        }
    }
}
