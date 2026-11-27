using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // みだるの必殺技「作戦ボード」(suehara)
    // ・発動から DrawTime 秒のあいだ、みだるが歩いた跡に、ホワイトボードのマーカーのような線を引く
    //   (この間はダッシュでスタミナを使わない)
    // ・線の端から始点へ「閉じたときの線」をうすく出しておく(どこが囲まれるか、敵にも分かるように)
    // ・時間になると始点と終点がつながって囲いが閉じ、中にいた敵は即死(積み木の数に関係なし)。
    //   みだる本人・味方・無敵中の敵は除く。同じ向きに何周もした所も「中」に数える
    // ・みだるがやられたら、その場で消える(囲いは閉じない)
    // 即死はホストだけ(ネット対戦のクライアントは線の見た目だけ)
    public class MidaruTacticsBoard : MonoBehaviour
    {
        // 線の点を足す間隔(m)
        const float kPointSpacing = 0.25f;
        // 線を地面から浮かせる高さ(m)
        const float kLineHeight = 0.06f;
        const float kLineWidth = 0.18f;
        // 閉じたあと、赤く光ってから消えるまでの時間(秒)
        const float kFadeTime = 0.8f;
        static readonly Color kLineColor = new Color(0.2f, 0.95f, 0.45f, 1.0f);
        static readonly Color kPreviewColor = new Color(0.2f, 0.95f, 0.45f, 0.35f);
        static readonly Color kClosedColor = new Color(1.0f, 0.25f, 0.2f, 1.0f);
        static Material s_material;

        DarumaCharacter m_owner;
        float m_drawTime;
        float m_start;
        readonly List<Vector3> m_points = new List<Vector3>();
        LineRenderer m_line;
        LineRenderer m_preview;
        bool m_closed;
        bool m_cancelled;
        float m_endedAt;
        // みだるのダッシュをスタミナなしにしているか
        bool m_freeDash;

        // 線を引いている最中か
        public bool IsDrawing => !m_closed && !m_cancelled;
        public float RemainingSeconds => IsDrawing ? Mathf.Max(0.0f, m_drawTime - (Time.time - m_start)) : 0.0f;
        public DarumaCharacter Owner => m_owner;
        // 囲まれた敵を即死させた数(テスト用)
        public int KilledCount { get; private set; }

        public static MidaruTacticsBoard Begin(DarumaCharacter owner, float drawTime)
        {
            var go = new GameObject("MidaruTacticsBoard");
            var board = go.AddComponent<MidaruTacticsBoard>();
            board.m_owner = owner;
            board.m_drawTime = Mathf.Max(0.5f, drawTime);
            board.m_start = Time.time;
            board.BuildLines();
            board.AddPoint(true);
            // 発動した瞬間から、ダッシュでスタミナを使わない
            if (owner != null)
            {
                owner.FreeDash = true;
                board.m_freeDash = true;
            }
            return board;
        }

        void BuildLines()
        {
            if (s_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) s_material = new Material(shader);
            }
            m_line = MakeLine("Line", kLineColor);
            m_preview = MakeLine("ClosePreview", kPreviewColor);
        }

        LineRenderer MakeLine(string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            // 線の幅を地面(XZ)に寝かせる(TransformZ = この向きに面を向ける)
            go.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = s_material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = kLineWidth;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void FixedUpdate()
        {
            if (!IsDrawing) return;
            if (m_owner == null || !m_owner.CanBeAffected)
            {
                Cancel();
                return;
            }
            if (!m_freeDash)
            {
                m_owner.FreeDash = true;
                m_freeDash = true;
            }
            AddPoint(false);
            if (Time.time - m_start >= m_drawTime) Close();
        }

        // みだるの足元に点を足す(前の点から kPointSpacing 以上離れたら)
        void AddPoint(bool force)
        {
            if (m_owner == null) return;
            Vector3 p = m_owner.transform.position;
            if (m_points.Count > 0)
            {
                Vector3 last = m_points[m_points.Count - 1];
                Vector3 d = p - last;
                d.y = 0.0f;
                if (!force && d.sqrMagnitude < kPointSpacing * kPointSpacing) return;
                // 跳んでいる間は、地面の高さのまま線を引く
                if (!m_owner.IsGrounded) p.y = last.y - kLineHeight;
            }
            p.y += kLineHeight;
            m_points.Add(p);
        }

        void Close()
        {
            AddPoint(true);
            m_closed = true;
            m_endedAt = Time.time;
            ReleaseOwner();
            if (!SpecialNet.IsClient) KillInside();   // ネット対戦用(suehara): 即死はホストだけ
            Debug.Log($"[みだる] 作戦ボード：囲いを閉じた（即死 {KilledCount} 人）");
        }

        void Cancel()
        {
            m_cancelled = true;
            m_endedAt = Time.time;
            ReleaseOwner();
        }

        void ReleaseOwner()
        {
            if (!m_freeDash) return;
            m_freeDash = false;
            if (m_owner != null) m_owner.FreeDash = false;
        }

        void OnDestroy()
        {
            ReleaseOwner();
        }

        // 囲いの中にいる敵を即死させる
        void KillInside()
        {
            if (m_points.Count < 3) return;
            foreach (var c in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (c == null || c == m_owner || !c.CanBeAffected || c.IsInvincible) continue;
                if (m_owner != null && DarumaMatch.AreFriends(m_owner, c)) continue;   // チーム戦: 味方は除く
                if (!IsInside(c.transform.position)) continue;
                c.Eliminate(EliminationCause.Other, Vector3.up, c.transform.position);
                KilledCount++;
            }
        }

        // 線で囲まれた中か(上から見て。何周もした所も中に数える = 回転数が 0 でない)
        public bool IsInside(Vector3 p)
        {
            int count = m_points.Count;
            if (count < 3) return false;
            int winding = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = m_points[i];
                Vector3 b = m_points[(i + 1) % count];
                if (a.z <= p.z)
                {
                    if (b.z > p.z && Side(a, b, p) > 0.0f) winding++;
                }
                else if (b.z <= p.z && Side(a, b, p) < 0.0f)
                {
                    winding--;
                }
            }
            return winding != 0;
        }

        // a→b の線から見て、p が左(正)か右(負)か
        static float Side(Vector3 a, Vector3 b, Vector3 p) => (b.x - a.x) * (p.z - a.z) - (p.x - a.x) * (b.z - a.z);

        void Update()
        {
            if (m_line == null) return;
            m_line.positionCount = m_points.Count;
            for (int i = 0; i < m_points.Count; i++) m_line.SetPosition(i, m_points[i]);

            if (IsDrawing)
            {
                // 今いる所から始点へ、閉じたときの線をうすく出す
                if (m_points.Count >= 2 && m_owner != null)
                {
                    Vector3 now = m_owner.transform.position;
                    now.y = m_points[m_points.Count - 1].y;
                    m_preview.positionCount = 2;
                    m_preview.SetPosition(0, now);
                    m_preview.SetPosition(1, m_points[0]);
                }
                return;
            }

            m_preview.positionCount = 0;
            // 閉じたら赤く光り、だんだん消える。やられて途中で終わったら、そのまま消える
            float t = Mathf.Clamp01((Time.time - m_endedAt) / kFadeTime);
            Color c = m_closed ? kClosedColor : kLineColor;
            c.a = 1.0f - t;
            m_line.startColor = c;
            m_line.endColor = c;
            m_line.widthMultiplier = kLineWidth * (m_closed ? 1.0f + 0.8f * (1.0f - t) : 1.0f);
            if (t >= 1.0f) Destroy(gameObject);
        }
    }
}
