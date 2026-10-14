using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // プレイヤーの出現・復活ポイント(DarumaMatch の出現ポイント)のデバッグ表示
    //  ・再生中: 床の上に点線の輪と柱、画面に番号(S1, S2, ...)を出す。キーで表示を切り替える
    //    出現ポイントが動いても(ステージの切り替えなど)ついていく。当たり判定は持たない
    //  ・エディタ: Gizmos で同じ位置に輪と番号を出す(再生していなくても見える)
    // 試合のシーンのデバッグ用の物(DebugTools など)に付ける
    public class SpawnPointDebugView : MonoBehaviour
    {
        [SerializeField] bool m_visible = true;
        [SerializeField] Key m_toggleKey = Key.P;
        [Tooltip("セレクト画面から始めた試合でも表示する(オフなら直接再生のときだけ)")]
        [SerializeField] bool m_showInConfiguredMatch = false;

        [Header("見た目")]
        [SerializeField, Min(0.1f)] float m_radius = 0.9f;
        [SerializeField, Min(0.0f)] float m_pillarHeight = 1.6f;
        [SerializeField] int m_dashCount = 16;
        [SerializeField] Color[] m_colors =
        {
            new Color(0.93f, 0.27f, 0.25f), new Color(0.22f, 0.55f, 0.95f), new Color(0.98f, 0.76f, 0.12f), new Color(0.30f, 0.74f, 0.35f),
            new Color(0.96f, 0.55f, 0.15f), new Color(0.40f, 0.85f, 0.85f), new Color(0.85f, 0.45f, 0.80f), new Color(0.55f, 0.45f, 0.35f),
        };

        [Header("参照(未設定ならシーンから探す)")]
        [SerializeField] DarumaMatch m_match;

        // 床から浮かせる高さ(床の面と重なってちらつかないように)
        const float kFloorOffset = 0.03f;

        readonly List<Transform> m_markers = new List<Transform>();
        readonly List<Material> m_materials = new List<Material>();
        GUIStyle m_labelStyle;

        public bool Visible
        {
            get => m_visible;
            set => m_visible = value;
        }

        bool Enabled => m_visible && Match != null && (m_showInConfiguredMatch || !Match.IsConfigured);

        DarumaMatch Match
        {
            get
            {
                if (m_match == null) m_match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
                return m_match;
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[m_toggleKey].wasPressedThisFrame) m_visible = !m_visible;
        }

        private void LateUpdate()
        {
            bool enabled = Enabled;
            var points = enabled ? Match.SpawnPoints : null;
            int count = points != null ? points.Count : 0;
            while (m_markers.Count < count) m_markers.Add(CreateMarker(m_markers.Count));
            for (int i = 0; i < m_markers.Count; i++)
            {
                bool show = i < count && points[i] != null;
                if (m_markers[i].gameObject.activeSelf != show) m_markers[i].gameObject.SetActive(show);
                if (show) m_markers[i].SetPositionAndRotation(points[i].position + Vector3.up * kFloorOffset, Quaternion.identity);
            }
        }

        private void OnDestroy()
        {
            foreach (var m in m_markers) if (m != null) Destroy(m.gameObject);
            foreach (var mat in m_materials) if (mat != null) Destroy(mat);
        }

        // 点線の輪と、真ん中の柱
        private Transform CreateMarker(int index)
        {
            var root = new GameObject($"SpawnPointMarker{index + 1}").transform;
            root.SetParent(transform, false);
            var mat = CreateMaterial(ColorFor(index));

            int dashes = Mathf.Max(4, m_dashCount);
            float dashLength = 2.0f * Mathf.PI * m_radius / dashes * 0.55f;
            for (int i = 0; i < dashes; i++)
            {
                float a = i * 360.0f / dashes;
                var dash = CreatePrimitive(PrimitiveType.Cube, root, mat);
                dash.localRotation = Quaternion.Euler(0.0f, a, 0.0f);
                dash.localPosition = dash.localRotation * new Vector3(0.0f, 0.0f, m_radius);
                dash.localScale = new Vector3(dashLength, 0.02f, 0.08f);
            }
            if (m_pillarHeight > 0.0f)
            {
                var pillar = CreatePrimitive(PrimitiveType.Cylinder, root, mat);
                pillar.localPosition = new Vector3(0.0f, m_pillarHeight * 0.5f, 0.0f);
                pillar.localScale = new Vector3(0.06f, m_pillarHeight * 0.5f, 0.06f);
            }
            return root;
        }

        private static Transform CreatePrimitive(PrimitiveType type, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        private Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            m_materials.Add(mat);
            return mat;
        }

        private Color ColorFor(int index)
        {
            if (m_colors == null || m_colors.Length == 0) return Color.white;
            return m_colors[index % m_colors.Length];
        }

        // 番号(柱の上)
        private void OnGUI()
        {
            if (!Enabled) return;
            var cam = Camera.main;
            if (cam == null) return;
            m_labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            var points = Match.SpawnPoints;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(points[i].position + Vector3.up * (m_pillarHeight + 0.4f));
                if (sp.z <= 0.0f) continue;
                var rect = new Rect(sp.x - 30, Screen.height - sp.y - 14, 60, 28);
                // 縁取り(黒)をしてから本体の色で描く
                m_labelStyle.normal.textColor = Color.black;
                string text = $"S{i + 1}";
                foreach (var o in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
                {
                    GUI.Label(new Rect(rect.x + o.x * 1.5f, rect.y + o.y * 1.5f, rect.width, rect.height), text, m_labelStyle);
                }
                m_labelStyle.normal.textColor = ColorFor(i);
                GUI.Label(rect, text, m_labelStyle);
            }

            GUI.Label(new Rect(10, Screen.height - 30, 400, 24), $"出現位置の表示 [{m_toggleKey}]");
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            var match = m_match != null ? m_match : FindAnyObjectByType<DarumaMatch>();
            if (match == null) return;
            var points = match.SpawnPoints;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null) continue;
                Vector3 p = points[i].position;
                Gizmos.color = ColorFor(i);
                const int kSegments = 32;
                for (int s = 0; s < kSegments; s++)
                {
                    float a0 = s * Mathf.PI * 2.0f / kSegments, a1 = (s + 1) * Mathf.PI * 2.0f / kSegments;
                    Vector3 up = Vector3.up * kFloorOffset;
                    Gizmos.DrawLine(p + up + new Vector3(Mathf.Cos(a0), 0.0f, Mathf.Sin(a0)) * m_radius, p + up + new Vector3(Mathf.Cos(a1), 0.0f, Mathf.Sin(a1)) * m_radius);
                }
                Gizmos.DrawLine(p, p + Vector3.up * m_pillarHeight);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(p + Vector3.up * (m_pillarHeight + 0.3f), $"S{i + 1}");
#endif
            }
        }
    }
}
