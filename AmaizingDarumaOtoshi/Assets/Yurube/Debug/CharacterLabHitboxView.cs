using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yurube.Lab
{
    // キャラクターラボ用の F3 当たり判定可視化。
    // suehara 側の新必殺技（紅蓮乱舞・疾風連撃・マシンガン・震脚）とだるきんぐ（エクスカリバー）の
    // 判定（カプセル／ボックス／球／衝撃波半径）を SpecialHitboxDebug.Visible に連動して描画する。
    // いわだる・へびだるは既存の CircleHitboxView が自動描画する。
    public class CharacterLabHitboxView : MonoBehaviour
    {
        const float kQuakeEchoDuration = 0.65f;
        const int kRingSegments = 64;

        class CapsuleVisual
        {
            public GameObject Root;
            public Transform Shaft;
            public Transform CapA;
            public Transform CapB;
        }

        class QuakeEcho
        {
            public Vector3 Position;
            public float Radius;
            public float ExpireAt;
        }

        CharacterLab m_lab;
        Material m_fillMaterial;
        Material m_lineMaterial;

        readonly List<CapsuleVisual> m_capsules = new List<CapsuleVisual>();
        readonly List<GameObject> m_boxes = new List<GameObject>();
        readonly List<GameObject> m_spheres = new List<GameObject>();
        readonly List<GameObject> m_discs = new List<GameObject>();
        readonly List<LineRenderer> m_lines = new List<LineRenderer>();

        readonly HashSet<KidaruQuake> m_seenQuakes = new HashSet<KidaruQuake>();
        readonly List<QuakeEcho> m_quakeEchoes = new List<QuakeEcho>();

        int m_capsuleUsed;
        int m_boxUsed;
        int m_sphereUsed;
        int m_discUsed;
        int m_lineUsed;

        public void Initialize(CharacterLab lab)
        {
            m_lab = lab;
            m_fillMaterial = CreateMaterial(new Color(1.0f, 0.12f, 0.10f, 0.28f));
            m_lineMaterial = CreateMaterial(new Color(1.0f, 0.28f, 0.15f, 0.92f));
        }

        public void ClearEchoes()
        {
            m_seenQuakes.Clear();
            m_quakeEchoes.Clear();
        }

        void LateUpdate()
        {
            m_capsuleUsed = 0;
            m_boxUsed = 0;
            m_sphereUsed = 0;
            m_discUsed = 0;
            m_lineUsed = 0;

            TrackNewQuakes();

            if (!SpecialHitboxDebug.Visible || m_lab == null)
            {
                HideUnused();
                return;
            }

            DrawAkadaruStorm();
            DrawAodaruGale();
            DrawMidaruMachineGun();
            DrawKidaruQuake();
            DrawDaruKingExcalibur();
            HideUnused();
        }

        void TrackNewQuakes()
        {
            float now = Time.time;
            for (int i = m_quakeEchoes.Count - 1; i >= 0; i--)
            {
                if (now >= m_quakeEchoes[i].ExpireAt) m_quakeEchoes.RemoveAt(i);
            }

            var quakes = FindObjectsByType<KidaruQuake>(FindObjectsInactive.Exclude);
            float radius = 5.0f;
            var current = m_lab != null ? m_lab.Character : null;
            var kidaru = current != null ? current.GetComponent<KidaruSpecial>() : null;
            if (kidaru != null) radius = kidaru.QuakeRadius;

            foreach (var q in quakes)
            {
                if (q == null) continue;
                if (!m_seenQuakes.Add(q)) continue;
                m_quakeEchoes.Add(new QuakeEcho
                {
                    Position = q.transform.position,
                    Radius = radius + DarumaCharacter.kBodyRadius,
                    ExpireAt = now + kQuakeEchoDuration,
                });
            }
            m_seenQuakes.RemoveWhere(q => q == null);
        }

        // あかだる「紅蓮乱舞」: 各ハンマーの柄・頭のカプセル判定を AkadaruHammerStorm と同じ式で描く
        void DrawAkadaruStorm()
        {
            var storms = FindObjectsByType<AkadaruHammerStorm>(FindObjectsInactive.Exclude);
            if (storms.Length == 0) return;

            var character = m_lab.Character;
            var special = character != null ? character.GetComponent<AkadaruSpecial>() : null;
            if (special != null && special.RemainingSeconds <= 0.0f) return;

            float scale = special != null ? special.StormHammerScale : 2.0f;
            float baseArm = character != null && character.Hammer != null ? character.Hammer.ArmLength : 1.2f;
            float armLength = baseArm * scale;

            foreach (var storm in storms)
            {
                if (storm == null) continue;
                for (int i = 0; i < storm.transform.childCount; i++)
                {
                    Transform pivot = storm.transform.GetChild(i);
                    if (pivot == null || !pivot.name.StartsWith("StormHammer")) continue;

                    float appear = Mathf.Max(0.01f, pivot.localScale.x);
                    float size = scale * appear;
                    float arm = armLength * appear;
                    Vector3 pivotPos = pivot.position;
                    Vector3 dir = pivot.rotation * Vector3.forward;
                    Vector3 headAxis = pivot.rotation * Vector3.up;
                    Vector3 head = pivotPos + dir * arm;

                    // 柄カプセル (kHitStartRatio = 0.15, kHandleRadius = 0.1)
                    PlaceCapsule(pivotPos + dir * (arm * 0.15f), head, 0.1f * size);
                    // 頭カプセル (kHeadHalfLength = 0.3, kHeadRadius = 0.22)
                    Vector3 half = headAxis * (0.3f * size);
                    PlaceCapsule(head - half, head + half, 0.22f * size);
                }
            }
        }

        // あおだる「疾風連撃」: 飛んでいる斬撃のボックス判定
        void DrawAodaruGale()
        {
            var slashes = FindObjectsByType<AodaruGaleSlash>(FindObjectsInactive.Exclude);
            if (slashes.Length == 0) return;

            var character = m_lab.Character;
            var special = character != null ? character.GetComponent<AodaruSpecial>() : null;
            float width = special != null ? special.SlashWidth : 2.4f;
            float height = special != null ? special.SlashHeight : 0.6f;
            float depth = special != null ? special.SlashDepth : 0.4f;

            foreach (var slash in slashes)
            {
                if (slash == null) continue;
                Vector3 center = slash.transform.position + Vector3.up * (height * 0.5f);
                PlaceBox(center, slash.transform.rotation, new Vector3(width, height, depth));
            }
        }

        // みだる「マシンガン」: 砲台の照準線と各弾の球判定 (kRadius = 0.28)
        void DrawMidaruMachineGun()
        {
            var guns = FindObjectsByType<MidaruMachineGun>(FindObjectsInactive.Exclude);
            foreach (var gun in guns)
            {
                if (gun == null || !gun.IsFiring) continue;
                Transform turret = gun.transform.Find("Turret");
                if (turret == null) continue;
                Vector3 start = turret.position;
                Vector3 end = start + turret.forward * 18.0f;
                PlaceLine(start, end);
            }

            var bullets = FindObjectsByType<MidaruBullet>(FindObjectsInactive.Exclude);
            foreach (var bullet in bullets)
            {
                if (bullet == null) continue;
                PlaceSphere(bullet.transform.position, 0.28f * 1.05f);
            }
        }

        // きだる「震脚」: 効果中の着地衝撃波半径リング + 着地時の衝撃波エコー円盤
        void DrawKidaruQuake()
        {
            var character = m_lab.Character;
            var kidaru = character != null ? character.GetComponent<KidaruSpecial>() : null;
            if (kidaru != null && kidaru.UsesQuake && kidaru.IsActive)
            {
                Vector3 center = character.transform.position + Vector3.up * 0.06f;
                PlaceCircleLine(center, kidaru.QuakeRadius);
                PlaceCircleLine(center, kidaru.QuakeRadius + DarumaCharacter.kBodyRadius);
            }

            for (int i = 0; i < m_quakeEchoes.Count; i++)
            {
                var echo = m_quakeEchoes[i];
                PlaceDisc(echo.Position + Vector3.up * 0.04f, echo.Radius);
                PlaceCircleLine(echo.Position + Vector3.up * 0.07f, echo.Radius);
            }
        }

        // だるきんぐ「エクスカリバー」: 脅威中の直線即死ボックス
        void DrawDaruKingExcalibur()
        {
            var character = m_lab.Character;
            var king = character != null ? character.GetComponent<DaruKingSpecial>() : null;
            if (king == null || !king.IsThreatening) return;

            king.GetAttackLine(out Vector3 start, out Vector3 end);
            Vector3 delta = end - start;
            delta.y = 0.0f;
            float length = delta.magnitude;
            if (length < 0.05f) return;

            Vector3 center = (start + end) * 0.5f + Vector3.up * 0.8f;
            Quaternion rot = Quaternion.LookRotation(delta.normalized, Vector3.up);
            PlaceBox(center, rot, new Vector3(king.AttackWidth, 1.6f, length));
        }

        void PlaceCapsule(Vector3 from, Vector3 to, float radius)
        {
            if (radius <= 0.001f) return;
            var vis = GetCapsule(m_capsuleUsed++);
            vis.Root.SetActive(true);

            Vector3 delta = to - from;
            float len = delta.magnitude;
            float d = radius * 2.0f;

            vis.CapA.position = from;
            vis.CapA.localScale = Vector3.one * d;
            vis.CapB.position = to;
            vis.CapB.localScale = Vector3.one * d;

            if (len > 0.0001f)
            {
                vis.Shaft.gameObject.SetActive(true);
                vis.Shaft.position = (from + to) * 0.5f;
                vis.Shaft.rotation = Quaternion.FromToRotation(Vector3.up, delta / len);
                vis.Shaft.localScale = new Vector3(d, len * 0.5f, d);
            }
            else
            {
                vis.Shaft.gameObject.SetActive(false);
            }
        }

        void PlaceBox(Vector3 center, Quaternion rotation, Vector3 scale)
        {
            var go = GetPrimitive(m_boxes, ref m_boxUsed, PrimitiveType.Cube, "HitboxBox");
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = scale;
        }

        void PlaceSphere(Vector3 center, float radius)
        {
            var go = GetPrimitive(m_spheres, ref m_sphereUsed, PrimitiveType.Sphere, "HitboxSphere");
            go.transform.position = center;
            go.transform.localScale = Vector3.one * (radius * 2.0f);
        }

        void PlaceDisc(Vector3 center, float radius)
        {
            var go = GetPrimitive(m_discs, ref m_discUsed, PrimitiveType.Cylinder, "HitboxDisc");
            go.transform.SetPositionAndRotation(center, Quaternion.identity);
            go.transform.localScale = new Vector3(radius * 2.0f, 0.02f, radius * 2.0f);
        }

        void PlaceLine(Vector3 start, Vector3 end)
        {
            var lr = GetLine(m_lineUsed++);
            lr.loop = false;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
        }

        void PlaceCircleLine(Vector3 center, float radius)
        {
            var lr = GetLine(m_lineUsed++);
            lr.loop = true;
            lr.positionCount = kRingSegments;
            for (int i = 0; i < kRingSegments; i++)
            {
                float a = i * Mathf.PI * 2.0f / kRingSegments;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.0f, Mathf.Sin(a) * radius));
            }
        }

        CapsuleVisual GetCapsule(int index)
        {
            while (m_capsules.Count <= index)
            {
                var root = new GameObject($"HitboxCapsule{m_capsules.Count}");
                root.transform.SetParent(transform, false);
                var shaft = MakeChildPrimitive(root.transform, PrimitiveType.Cylinder, "Shaft");
                var capA = MakeChildPrimitive(root.transform, PrimitiveType.Sphere, "CapA");
                var capB = MakeChildPrimitive(root.transform, PrimitiveType.Sphere, "CapB");
                m_capsules.Add(new CapsuleVisual { Root = root, Shaft = shaft.transform, CapA = capA.transform, CapB = capB.transform });
            }
            return m_capsules[index];
        }

        GameObject GetPrimitive(List<GameObject> pool, ref int used, PrimitiveType type, string baseName)
        {
            int index = used++;
            while (pool.Count <= index)
            {
                pool.Add(MakeChildPrimitive(transform, type, $"{baseName}{pool.Count}"));
            }
            var go = pool[index];
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        LineRenderer GetLine(int index)
        {
            while (m_lines.Count <= index)
            {
                var go = new GameObject($"HitboxLine{m_lines.Count}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.09f;
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (m_lineMaterial != null) lr.sharedMaterial = m_lineMaterial;
                Color c = new Color(1.0f, 0.28f, 0.15f, 0.92f);
                lr.startColor = c;
                lr.endColor = c;
                m_lines.Add(lr);
            }
            var line = m_lines[index];
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
            return line;
        }

        GameObject MakeChildPrimitive(Transform parent, PrimitiveType type, string objectName)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                Destroy(col);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (m_fillMaterial != null) r.sharedMaterial = m_fillMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return go;
        }

        void HideUnused()
        {
            for (int i = m_capsuleUsed; i < m_capsules.Count; i++)
                if (m_capsules[i].Root.activeSelf) m_capsules[i].Root.SetActive(false);
            for (int i = m_boxUsed; i < m_boxes.Count; i++)
                if (m_boxes[i].activeSelf) m_boxes[i].SetActive(false);
            for (int i = m_sphereUsed; i < m_spheres.Count; i++)
                if (m_spheres[i].activeSelf) m_spheres[i].SetActive(false);
            for (int i = m_discUsed; i < m_discs.Count; i++)
                if (m_discs[i].activeSelf) m_discs[i].SetActive(false);
            for (int i = m_lineUsed; i < m_lines.Count; i++)
                if (m_lines[i].gameObject.activeSelf) m_lines[i].gameObject.SetActive(false);
        }

        static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        void OnDestroy()
        {
            if (m_fillMaterial != null) Destroy(m_fillMaterial);
            if (m_lineMaterial != null) Destroy(m_lineMaterial);
        }
    }
}
