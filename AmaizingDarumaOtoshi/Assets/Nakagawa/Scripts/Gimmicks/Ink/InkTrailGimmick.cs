using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 墨の筆跡(Stage25「書き初めの半紙」)。床(半紙)の上に、プレイヤーの色の墨の線を引く
    //  ・歩いて引く: 生存者が歩くと、足元から線が引かれる。太さは積み木の数で変わる(身軽なほど太い)。ゆっくり動くと細くなる(筆の入り・止め)
    //  ・打った積み木も引く: 持ち主のいる飛んでいる積み木が、通り道に持ち主の色の線を引く(幽霊が打ち返した積み木は幽霊の色)
    //  ・復活したら、出た場所のまわりに自分の色の墨だまりができる
    //  ・墨は乾く: 引いてから m_holdTime 秒は濃いまま、そのあと m_fadeTime 秒で消える
    //  ・効く線: 「引いた本人の足元から一度離れた線」だけが効く。自分の効く線の上にいる間、移動が m_speedScale 倍になる
    //    (立ち止まっても、まっすぐ歩いても、足元の線はまだ離れていないので効かない。前に通った道をもう一度通ると速い)
    //    自分の効く線の上に自分の新しい線を重ねても、効く線のまま(重ね書きで効果が切れない)
    //  ・墨が付かない床(下敷き)は m_noPaintRects / m_noPaintCircles
    // 線は画素(1m を m_pixelsPerMeter 分割)に「引いた人・時刻・離れたか」を書き込み、見た目はシェーダー(Nakagawa/InkTrail)が描く
    // ネット対戦: クライアントも、届いた体と積み木の位置から自分で線を引く(見た目だけ)。速くするのはホストだけ
    [DefaultExecutionOrder(-10)]
    public class InkTrailGimmick : MonoBehaviour
    {
        [Header("範囲")]
        // 墨が付く範囲(この GameObject の位置が中心。X・Z)
        [Tooltip("墨が付く範囲の大きさ(m)。X が横幅、Y が奥行き(Z)。この GameObject の位置が中心\n※再生中に変えても効きません(始まったときに決まります)")]
        [SerializeField] Vector2 m_size = new Vector2(28.0f, 20.0f);
        [Tooltip("1m を何画素に分けて墨を記録するか。大きいほど線がなめらかになるが重くなる\n※再生中に変えても効きません")]
        [SerializeField, Range(4, 32)] int m_pixelsPerMeter = 16;
        // 墨が付かない床(ワールドの X・Z の矩形 / 円 x, z, 半径)
        [Tooltip("墨が付かない床(下敷き)の四角。ワールド座標の X・Z で、x・y が左下の角、width・height が大きさ(m)\n補給所と同じ形にしてあります")]
        [SerializeField] Rect[] m_noPaintRects = new Rect[0];
        [Tooltip("墨が付かない床(下敷き)の円。x = 中心の X、y = 中心の Z、z = 半径(m)\n補給所と同じ形にしてあります")]
        [SerializeField] Vector3[] m_noPaintCircles = new Vector3[0];

        [Header("線の太さ")]
        // 積み木の数が m_widthSteps.x 個以下 / m_widthSteps.y 個以下 / それより多いときの太さ(m)
        [Tooltip("歩いて引く線の太さ(m)。積み木が少ないほど太い(身軽なほど広く塗れる)\nx = Width Steps の x 個以下 / y = Width Steps の y 個以下 / z = それより多いとき")]
        [SerializeField] Vector3 m_walkWidths = new Vector3(2.4f, 1.6f, 0.8f);
        [Tooltip("線の太さが変わる積み木の数の区切り。x 個以下なら一番太い、y 個以下なら中くらい、それより多いと細い")]
        [SerializeField] Vector2Int m_widthSteps = new Vector2Int(2, 6);
        // この速さ(m/秒)より遅いと細くなる(筆の入り・止め)。最も細いときの割合
        [Tooltip("この速さ(m/秒)以上で歩くと、線が本来の太さになる。これより遅いと細くなる(筆の入り・止め)")]
        [SerializeField] float m_fullWidthSpeed = 3.0f;
        [Tooltip("一番ゆっくり歩いたときの線の太さの割合(本来の太さに掛ける)")]
        [SerializeField, Range(0.1f, 1.0f)] float m_slowWidthRate = 0.55f;
        [Tooltip("打った積み木が通り道に引く線の太さ(m)")]
        [SerializeField] float m_shotWidth = 0.8f;
        // この高さより上を飛んでいる積み木・跳んでいる人は線を引かない
        [Tooltip("床からこの高さ(m)より上にいる人・積み木は線を引かない(跳んでいる間・高く飛んでいる積み木)")]
        [SerializeField] float m_paintHeight = 1.0f;
        [Tooltip("復活したとき、出た場所のまわりにできる自分の色の墨だまりの半径(m)。0 で出ない")]
        [SerializeField] float m_respawnSplashRadius = 2.0f;

        [Header("乾く・効く")]
        [Tooltip("線を引いてから濃いままでいる時間(秒)。このあと Fade Time 秒で消える\n※Hold Time + Fade Time は 30 秒未満にしてください(時刻を 32 秒で折り返して記録しているため)")]
        [SerializeField] float m_holdTime = 7.0f;
        [Tooltip("濃いままの時間が終わってから、線が薄くなって消えるまでの時間(秒)")]
        [SerializeField] float m_fadeTime = 3.0f;
        // 線の太さの半分にこれを足した距離より足元が離れたら、その線は「離れた」(効く線)になる
        [Tooltip("足元がこの距離(m)+線の太さの半分より離れたら、その線は「効く線」になる\n大きくすると、引いた線が効くようになるまで遠くへ離れる必要がある")]
        [SerializeField] float m_leaveMargin = 0.2f;
        [Tooltip("自分の効く線の上にいる間の移動の速さの倍率(1.15 = 15% 速い)")]
        [SerializeField] float m_speedScale = 1.15f;

        [Header("見た目")]
        [Tooltip("墨を描く板(床の上に重ねた Quad)。ステージを作り直すと自動で入ります")]
        [SerializeField] Renderer m_overlay;
        [Tooltip("プレイヤーごとの墨の色(上から 1P・2P・3P・4P)")]
        [SerializeField] Color[] m_inkColors =
        {
            new Color(0.74f, 0.30f, 0.28f),   // 1P
            new Color(0.28f, 0.44f, 0.78f),   // 2P
            new Color(0.76f, 0.60f, 0.22f),   // 3P
            new Color(0.30f, 0.58f, 0.34f),   // 4P
        };
        [Tooltip("墨の濃さ(0 = 見えない、1 = 不透明)")]
        [SerializeField, Range(0.0f, 1.0f)] float m_opacity = 0.62f;
        // 速くなっている間、その人のまわりの自分の墨を光らせる半径(m)と、光り始め・消えるまでの時間(秒)
        [Tooltip("速くなっている間、その人のまわりの自分の墨が光る範囲の半径(m)")]
        [SerializeField] float m_glowRadius = 3.5f;
        [Tooltip("光り始めてから明るくなりきるまで・消えるまでの時間(秒)")]
        [SerializeField, Min(0.01f)] float m_glowFadeTime = 0.2f;

        // 時刻の刻み(1秒を何刻みにするか)。8 なら 256 刻みで 32 秒で折り返す(乾くまでの 10 秒より十分長い)
        const float kTicksPerSecond = 8.0f;
        const byte kLeft = 255;

        static readonly int s_inkTexId = Shader.PropertyToID("_InkTex");
        static readonly int s_nowTickId = Shader.PropertyToID("_NowTick");
        static readonly int s_holdId = Shader.PropertyToID("_HoldTicks");
        static readonly int s_fadeId = Shader.PropertyToID("_FadeTicks");
        static readonly int s_opacityId = Shader.PropertyToID("_Opacity");
        static readonly int[] s_colorIds =
        {
            Shader.PropertyToID("_Color1"), Shader.PropertyToID("_Color2"), Shader.PropertyToID("_Color3"), Shader.PropertyToID("_Color4"),
        };
        static readonly int[] s_glowIds =
        {
            Shader.PropertyToID("_Glow0"), Shader.PropertyToID("_Glow1"), Shader.PropertyToID("_Glow2"), Shader.PropertyToID("_Glow3"),
        };
        static readonly int s_glowRadiusId = Shader.PropertyToID("_GlowRadius");

        // プレイヤーごとの、前のステップの位置と、まだ離れていない画素
        class Painter
        {
            public bool HasLast;
            public Vector3 Last;
            public bool WasAlive;
            public readonly List<int> Pending = new List<int>();
            // 速くなっているか(このステップ)と、光の強さ(0〜1。なめらかに変える)
            public bool Boosted;
            public float Glow;
            public Vector3 Feet;
        }

        readonly Dictionary<int, Painter> m_painters = new Dictionary<int, Painter>();
        readonly Dictionary<TumikiBlock, Vector3> m_blockLast = new Dictionary<TumikiBlock, Vector3>();
        readonly List<TumikiBlock> m_blockRemove = new List<TumikiBlock>();
        Texture2D m_tex;
        Color32[] m_pixels;
        bool[] m_noPaint;
        int m_w, m_h;
        float m_time;
        float m_expireTimer;
        bool m_dirty;
        MaterialPropertyBlock m_mpb;
        DarumaMatch m_match;

        // 墨が付く範囲の左下(ワールドの X・Z)
        Vector2 Origin => new Vector2(transform.position.x - m_size.x * 0.5f, transform.position.z - m_size.y * 0.5f);

        private void Awake()
        {
            m_w = Mathf.Max(1, Mathf.RoundToInt(m_size.x * m_pixelsPerMeter));
            m_h = Mathf.Max(1, Mathf.RoundToInt(m_size.y * m_pixelsPerMeter));
            m_pixels = new Color32[m_w * m_h];
            m_tex = new Texture2D(m_w, m_h, TextureFormat.RGBA32, false, true)
            {
                name = "InkTrail",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            BuildNoPaintMask();
            m_mpb = new MaterialPropertyBlock();
            ClearAll();
        }

        private void OnDestroy()
        {
            if (m_tex != null) Destroy(m_tex);
        }

        private void OnEnable()
        {
            m_match = DarumaMatch.Current;
            if (m_match != null) m_match.Started += ClearAll;
            ClearAll();
        }

        private void OnDisable()
        {
            if (m_match != null) m_match.Started -= ClearAll;
            m_match = null;
        }

        public void ClearAll()
        {
            if (m_pixels == null) return;
            for (int i = 0; i < m_pixels.Length; i++) m_pixels[i] = new Color32(0, 0, 0, 0);
            m_painters.Clear();
            m_blockLast.Clear();
            m_time = 0.0f;
            m_dirty = true;
        }

        void BuildNoPaintMask()
        {
            m_noPaint = new bool[m_w * m_h];
            Vector2 origin = Origin;
            float step = 1.0f / m_pixelsPerMeter;
            for (int y = 0; y < m_h; y++)
            {
                for (int x = 0; x < m_w; x++)
                {
                    Vector2 p = origin + new Vector2((x + 0.5f) * step, (y + 0.5f) * step);
                    bool blocked = false;
                    foreach (var r in m_noPaintRects) if (r.Contains(p)) { blocked = true; break; }
                    if (!blocked)
                    {
                        foreach (var c in m_noPaintCircles)
                        {
                            if ((p - new Vector2(c.x, c.y)).sqrMagnitude <= c.z * c.z) { blocked = true; break; }
                        }
                    }
                    m_noPaint[y * m_w + x] = blocked;
                }
            }
        }

        byte NowTick => (byte)((int)(m_time * kTicksPerSecond) & 255);

        float AgeSeconds(byte tick)
        {
            int age = (NowTick - tick) & 255;
            return age / kTicksPerSecond;
        }

        bool Alive(Color32 c) => c.r != 0 && AgeSeconds(c.g) < m_holdTime + m_fadeTime;

        //========================================
        // 毎ステップ
        //========================================

        private void FixedUpdate()
        {
            if (GimmickClock.IsHeld) return;
            var match = DarumaMatch.Current;
            if (match == null || match.IsFinished) return;
            float dt = Time.fixedDeltaTime;
            m_time += dt;
            bool host = !GimmickClock.IsNetClient;

            foreach (var p in match.Players)
            {
                if (p == null || p.Entry == null) continue;
                if (!m_painters.TryGetValue(p.Index, out var painter))
                {
                    painter = new Painter();
                    m_painters[p.Index] = painter;
                }
                UpdatePainter(p, painter, dt, host);
            }
            UpdateBlocks(match);

            m_expireTimer -= dt;
            if (m_expireTimer <= 0.0f)
            {
                m_expireTimer = 0.5f;
                Expire();
            }
        }

        private void LateUpdate()
        {
            if (m_overlay == null || m_tex == null) return;
            if (m_dirty)
            {
                m_tex.SetPixels32(m_pixels);
                m_tex.Apply(false);
                m_dirty = false;
            }
            m_overlay.GetPropertyBlock(m_mpb);
            m_mpb.SetTexture(s_inkTexId, m_tex);
            m_mpb.SetFloat(s_nowTickId, (m_time * kTicksPerSecond) % 256.0f);
            m_mpb.SetFloat(s_holdId, m_holdTime * kTicksPerSecond);
            m_mpb.SetFloat(s_fadeId, m_fadeTime * kTicksPerSecond);
            m_mpb.SetFloat(s_opacityId, m_opacity);
            for (int i = 0; i < s_colorIds.Length && i < m_inkColors.Length; i++) m_mpb.SetColor(s_colorIds[i], m_inkColors[i]);

            // 速くなっている人のまわりの墨を光らせる(強さはなめらかに上げ下げする)
            int slot = 0;
            float step = Time.deltaTime / m_glowFadeTime;
            foreach (var kv in m_painters)
            {
                var p = kv.Value;
                p.Glow = Mathf.MoveTowards(p.Glow, p.Boosted ? 1.0f : 0.0f, step);
                if (slot >= s_glowIds.Length || p.Glow <= 0.0f) continue;
                m_mpb.SetVector(s_glowIds[slot++], new Vector4(p.Feet.x, p.Feet.z, kv.Key + 1, p.Glow));
            }
            for (; slot < s_glowIds.Length; slot++) m_mpb.SetVector(s_glowIds[slot], Vector4.zero);
            m_mpb.SetFloat(s_glowRadiusId, m_glowRadius);
            m_overlay.SetPropertyBlock(m_mpb);
        }

        void UpdatePainter(MatchPlayer p, Painter painter, float dt, bool host)
        {
            DarumaCharacter c = p.State == MatchPlayer.PlayerState.Alive ? p.Character : null;
            painter.Boosted = false;
            if (c == null || !c.CanBeAffected)
            {
                // 体がなくなった(やられた・ワープの移動中など): 残った線は、足元から離れたものとして効く線にする
                if (painter.Pending.Count > 0) UpdateLeaving(new Vector2(float.MaxValue * 0.5f, 0.0f), 0.0f, (byte)(p.Index + 1), painter.Pending);
                painter.HasLast = false;
                painter.WasAlive = c != null;
                return;
            }

            Vector3 pos = c.transform.position;
            byte owner = (byte)(p.Index + 1);
            float fullWidth = WalkWidth(p.StackCount);

            // 復活した(前のステップでは体がなかった): 出た場所に墨だまり
            if (!painter.WasAlive)
            {
                painter.WasAlive = true;
                PaintDisc(new Vector2(pos.x, pos.z), m_respawnSplashRadius, owner, false, painter.Pending);
                painter.HasLast = false;
            }

            bool low = pos.y - transform.position.y < m_paintHeight;
            if (low)
            {
                if (painter.HasLast)
                {
                    float moved = new Vector2(pos.x - painter.Last.x, pos.z - painter.Last.z).magnitude;
                    if (moved > 0.01f)
                    {
                        float speed = moved / Mathf.Max(0.0001f, dt);
                        float rate = Mathf.Lerp(m_slowWidthRate, 1.0f, Mathf.Clamp01(speed / Mathf.Max(0.01f, m_fullWidthSpeed)));
                        PaintCapsule(new Vector2(painter.Last.x, painter.Last.z), new Vector2(pos.x, pos.z), fullWidth * 0.5f * rate, owner, false, painter.Pending);
                    }
                }
                painter.Last = pos;
                painter.HasLast = true;
            }
            else
            {
                painter.HasLast = false;
            }

            // 足元から離れた画素を「効く線」にする
            UpdateLeaving(new Vector2(pos.x, pos.z), fullWidth * 0.5f + m_leaveMargin, owner, painter.Pending);

            // 足元が自分の効く線なら速くする(速くするのはホストだけ。光らせるのは各端末)
            painter.Feet = pos;
            if (low && IsOwnLeftAt(new Vector2(pos.x, pos.z), owner))
            {
                painter.Boosted = true;
                if (host) c.RequestSpeedScale(m_speedScale);
            }
        }

        float WalkWidth(int stack)
        {
            if (stack <= m_widthSteps.x) return m_walkWidths.x;
            if (stack <= m_widthSteps.y) return m_walkWidths.y;
            return m_walkWidths.z;
        }

        void UpdateBlocks(DarumaMatch match)
        {
            m_blockRemove.Clear();
            foreach (var kv in m_blockLast)
            {
                if (kv.Key == null || kv.Key.CurrentState != TumikiBlock.State.Flying) m_blockRemove.Add(kv.Key);
            }
            foreach (var b in m_blockRemove) m_blockLast.Remove(b);

            foreach (var b in TumikiBlock.All)
            {
                if (b == null || b.CurrentState != TumikiBlock.State.Flying) continue;
                var owner = b.Owner;
                if (!DarumaAttackerUtil.IsAlive(owner)) continue;
                MatchPlayer p = FindPlayer(match, owner, out bool ghost);
                if (p == null) continue;

                Vector3 pos = b.transform.position;
                if (pos.y - transform.position.y > m_paintHeight)
                {
                    m_blockLast.Remove(b);
                    continue;
                }
                if (m_blockLast.TryGetValue(b, out Vector3 last))
                {
                    byte id = (byte)(p.Index + 1);
                    if (ghost || p.Character == null)
                    {
                        // 幽霊の線は、引いた時点で効く線
                        PaintCapsule(new Vector2(last.x, last.z), new Vector2(pos.x, pos.z), m_shotWidth * 0.5f, id, true, null);
                    }
                    else
                    {
                        // 打った人の足元の近くだけ、まだ離れていない線にする
                        m_painters.TryGetValue(p.Index, out var painter);
                        Vector3 feet = p.Character.transform.position;
                        float keep = WalkWidth(p.StackCount) * 0.5f + m_leaveMargin;
                        PaintCapsule(new Vector2(last.x, last.z), new Vector2(pos.x, pos.z), m_shotWidth * 0.5f, id, true,
                            painter != null ? painter.Pending : null, new Vector2(feet.x, feet.z), keep);
                    }
                }
                m_blockLast[b] = pos;
            }
        }

        static MatchPlayer FindPlayer(DarumaMatch match, IDarumaAttacker attacker, out bool ghost)
        {
            ghost = false;
            foreach (var p in match.Players)
            {
                if (p == null) continue;
                if (p.Character != null && ReferenceEquals(p.Character, attacker)) return p;
                if (p.Ghost != null && ReferenceEquals(p.Ghost, attacker))
                {
                    ghost = true;
                    return p;
                }
            }
            return null;
        }

        //========================================
        // 画素への書き込み
        //========================================

        bool ToPixel(Vector2 world, out int x, out int y)
        {
            Vector2 local = (world - Origin) * m_pixelsPerMeter;
            x = Mathf.FloorToInt(local.x);
            y = Mathf.FloorToInt(local.y);
            return x >= 0 && y >= 0 && x < m_w && y < m_h;
        }

        Vector2 PixelCenter(int index)
        {
            int x = index % m_w, y = index / m_w;
            return Origin + new Vector2((x + 0.5f) / m_pixelsPerMeter, (y + 0.5f) / m_pixelsPerMeter);
        }

        // a〜b を半径 radius でなぞる。left: 引いた時点で効く線にするか
        // keepNear: left のとき、この点から keepRadius 以内の画素はまだ離れていない線にする(打った人の足元)
        void PaintCapsule(Vector2 a, Vector2 b, float radius, byte owner, bool left, List<int> pending, Vector2? keepNear = null, float keepRadius = 0.0f)
        {
            if (radius <= 0.0f) return;
            Vector2 min = Vector2.Min(a, b) - Vector2.one * radius;
            Vector2 max = Vector2.Max(a, b) + Vector2.one * radius;
            Vector2 origin = Origin;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x - origin.x) * m_pixelsPerMeter));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((min.y - origin.y) * m_pixelsPerMeter));
            int x1 = Mathf.Min(m_w - 1, Mathf.CeilToInt((max.x - origin.x) * m_pixelsPerMeter));
            int y1 = Mathf.Min(m_h - 1, Mathf.CeilToInt((max.y - origin.y) * m_pixelsPerMeter));
            if (x0 > x1 || y0 > y1) return;

            Vector2 ab = b - a;
            float len2 = Mathf.Max(0.000001f, ab.sqrMagnitude);
            float r2 = radius * radius;
            float k2 = keepRadius * keepRadius;
            float step = 1.0f / m_pixelsPerMeter;
            byte tick = NowTick;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * m_w + x;
                    if (m_noPaint[i]) continue;
                    Vector2 p = origin + new Vector2((x + 0.5f) * step, (y + 0.5f) * step);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    if ((a + ab * t - p).sqrMagnitude > r2) continue;
                    bool l = left && !(keepNear.HasValue && (p - keepNear.Value).sqrMagnitude <= k2);
                    WritePixel(i, owner, l, tick, pending);
                }
            }
        }

        void PaintDisc(Vector2 center, float radius, byte owner, bool left, List<int> pending)
        {
            PaintCapsule(center, center, radius, owner, left, pending);
        }

        void WritePixel(int i, byte owner, bool left, byte tick, List<int> pending)
        {
            Color32 old = m_pixels[i];
            // 自分の効く線の上に自分の線を重ねても、効く線のまま(時刻だけ新しくする)
            bool alive = Alive(old);
            bool keepLeft = old.r == owner && old.b == kLeft && alive;
            // 自分のまだ離れていない画素は、もう待ちの一覧に入っている(同じ画素を何度も足さない)
            bool alreadyPending = old.r == owner && old.b != kLeft && alive;
            bool l = left || keepLeft;
            m_pixels[i] = new Color32(owner, tick, l ? kLeft : (byte)0, 255);
            if (!l && pending != null && !alreadyPending) pending.Add(i);
            m_dirty = true;
        }

        // 引いた本人の足元(feet)から distance より離れた画素を、効く線にする
        void UpdateLeaving(Vector2 feet, float distance, byte owner, List<int> pending)
        {
            if (pending.Count == 0) return;
            float d2 = distance * distance;
            int write = 0;
            for (int k = 0; k < pending.Count; k++)
            {
                int i = pending[k];
                Color32 c = m_pixels[i];
                // ほかの人に上書きされた・もう効く線になった・消えた画素は外す
                if (c.r != owner || c.b == kLeft || !Alive(c)) continue;
                if ((PixelCenter(i) - feet).sqrMagnitude > d2)
                {
                    c.b = kLeft;
                    m_pixels[i] = c;
                    m_dirty = true;
                    continue;
                }
                pending[write++] = i;
            }
            pending.RemoveRange(write, pending.Count - write);
        }

        bool IsOwnLeftAt(Vector2 world, byte owner)
        {
            if (!ToPixel(world, out int x, out int y)) return false;
            Color32 c = m_pixels[y * m_w + x];
            return c.r == owner && c.b == kLeft && Alive(c);
        }

        // 乾ききった画素を消す(時刻の折り返しで、また濃く見えないように)
        void Expire()
        {
            for (int i = 0; i < m_pixels.Length; i++)
            {
                Color32 c = m_pixels[i];
                if (c.r == 0 || Alive(c)) continue;
                m_pixels[i] = new Color32(0, 0, 0, 0);
                m_dirty = true;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(m_size.x, 0.05f, m_size.y));
            Gizmos.color = new Color(0.3f, 0.35f, 0.6f, 0.8f);
            foreach (var r in m_noPaintRects) Gizmos.DrawWireCube(new Vector3(r.center.x, transform.position.y, r.center.y), new Vector3(r.width, 0.05f, r.height));
            foreach (var c in m_noPaintCircles) Gizmos.DrawWireSphere(new Vector3(c.x, transform.position.y, c.y), c.z);
        }
    }
}
