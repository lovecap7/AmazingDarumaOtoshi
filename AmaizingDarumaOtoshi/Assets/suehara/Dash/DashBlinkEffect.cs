using UnityEngine;

// ブリンクの軌跡(出発点から着いた所まで伸びる光の帯)。すぐに細く薄くなって消える
public class DashBlinkEffect : MonoBehaviour
{
    const float kLifetime = 0.25f;
    const float kStartWidth = 0.9f;
    static readonly Color kColor = new Color(0.75f, 0.95f, 1.0f, 0.85f);
    static Material s_material;

    LineRenderer m_line;
    float m_age;

    // height: キャラクターの体の高さ(帯をその半分の高さに通す)
    public static void Play(Vector3 from, Vector3 to, float height)
    {
        if (s_material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            s_material = new Material(shader);
        }

        var go = new GameObject("DashBlinkEffect");
        var effect = go.AddComponent<DashBlinkEffect>();
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = s_material;
        line.positionCount = 2;
        Vector3 up = Vector3.up * Mathf.Max(0.3f, height * 0.5f);
        line.SetPosition(0, from + up);
        line.SetPosition(1, to + up);
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        effect.m_line = line;
        effect.Apply(0.0f);
    }

    void Update()
    {
        m_age += Time.deltaTime;
        float t = m_age / kLifetime;
        if (t >= 1.0f)
        {
            Destroy(gameObject);
            return;
        }
        Apply(t);
    }

    // t: 0(出た瞬間)〜1(消える)
    void Apply(float t)
    {
        float k = 1.0f - t;
        // 出発点側から先に細くなる
        m_line.startWidth = kStartWidth * k * k;
        m_line.endWidth = kStartWidth * k;
        Color start = kColor;
        start.a *= k * 0.4f;
        Color end = kColor;
        end.a *= k;
        m_line.startColor = start;
        m_line.endColor = end;
    }
}
