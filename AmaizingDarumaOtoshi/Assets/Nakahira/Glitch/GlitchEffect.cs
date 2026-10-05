using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 画面全体にグリッチをかける
// カメラに付ければ使えるようにする(Rendererアセットの設定は要らない)
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class GlitchEffect : MonoBehaviour
{
    [Header("グリッチ用シェーダー(Nakahira/Glitch)")]
    [SerializeField]
    private Shader m_shader;

    [Header("常にかけておく強さ")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float m_intensity = 0.0f;

    [Header("ノイズが切り替わる速さ(回/秒)")]
    [SerializeField]
    private float m_speed = 12.0f;

    [Header("ブロックの分割数(横, 縦)")]
    [SerializeField]
    private Vector2 m_blockCount = new(6.0f, 14.0f);

    [Header("ずれるブロックの割合")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float m_blockAmount = 0.3f;

    [Header("ブロックがずれる幅(画面幅を1とする)")]
    [SerializeField, Range(0.0f, 0.5f)]
    private float m_blockShift = 0.1f;

    [Header("横線ごとの揺れ幅(画面幅を1とする)")]
    [SerializeField, Range(0.0f, 0.1f)]
    private float m_jitter = 0.01f;

    [Header("色ずれの幅(画面幅を1とする)")]
    [SerializeField, Range(0.0f, 0.1f)]
    private float m_colorShift = 0.01f;

    [Header("走査線の濃さ")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float m_scanline = 0.2f;

    private static readonly int s_intensityID = Shader.PropertyToID("_GlitchIntensity");
    private static readonly int s_timeID = Shader.PropertyToID("_GlitchTime");
    private static readonly int s_speedID = Shader.PropertyToID("_GlitchSpeed");
    private static readonly int s_blockCountID = Shader.PropertyToID("_GlitchBlockCount");
    private static readonly int s_blockAmountID = Shader.PropertyToID("_GlitchBlockAmount");
    private static readonly int s_blockShiftID = Shader.PropertyToID("_GlitchBlockShift");
    private static readonly int s_jitterID = Shader.PropertyToID("_GlitchJitter");
    private static readonly int s_colorShiftID = Shader.PropertyToID("_GlitchColorShift");
    private static readonly int s_scanlineID = Shader.PropertyToID("_GlitchScanline");

    private Camera m_camera;
    private Material m_material;
    private GlitchPass m_pass;

    // Play()で一時的にかけるグリッチ
    private float m_burstIntensity = 0.0f;
    private float m_burstDuration = 0.0f;
    private float m_burstSecondLeft = 0.0f;

    // ポストプロセスの後に、画面全体へシェーダーをかけるパス
    private class GlitchPass : ScriptableRenderPass
    {
        public Material m_material;

        public GlitchPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            // バックバッファに直接描かれると読み込めないので中間テクスチャを使わせる
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            // 今の画面と同じ設定のテクスチャを作ってそこに描く
            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(source);
            desc.name = "_GlitchTexture";
            desc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            RenderGraphUtils.BlitMaterialParameters param = new(source, destination, m_material, 0);
            renderGraph.AddBlitPass(param, "Glitch");

            // 以降は描いた先を画面として使ってもらう
            resourceData.cameraColor = destination;
        }
    }

    // コンポーネントを付けた時にシェーダーを入れておく(エディタでしか呼ばれない)
    void Reset()
    {
        m_shader = Shader.Find("Nakahira/Glitch");
    }

    void OnEnable()
    {
        m_camera = GetComponent<Camera>();
        m_pass = new();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        CoreUtils.Destroy(m_material);
        m_material = null;
    }

    void Update()
    {
        // ヒットストップ中も動くようにtimeScaleの影響を受けない時間を使う
        if (m_burstSecondLeft > 0.0f)
        {
            m_burstSecondLeft -= Time.unscaledDeltaTime;
        }
    }

    /// <summary>
    /// 一時的にグリッチをかける。時間経過で弱くなって消える
    /// </summary>
    /// <param name="duration">かける時間(秒)</param>
    /// <param name="intensity">かけ始めの強さ 0~1</param>
    public void Play(float duration, float intensity = 1.0f)
    {
        m_burstDuration = duration;
        m_burstSecondLeft = duration;
        m_burstIntensity = intensity;
    }

    /// <summary>
    /// 常にかけておく強さを変える
    /// </summary>
    public void SetIntensity(float intensity)
    {
        m_intensity = Mathf.Clamp01(intensity);
    }

    // 今の強さ。常時の分と一時的な分の強い方
    private float GetCurrentIntensity()
    {
        float burst = 0.0f;
        if (m_burstSecondLeft > 0.0f && m_burstDuration > 0.0f)
        {
            burst = m_burstIntensity * (m_burstSecondLeft / m_burstDuration);
        }
        return Mathf.Clamp01(Mathf.Max(m_intensity, burst));
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // 自分のカメラだけにかける
        if (camera != m_camera) return;

        // かかっていない時はパスごと積まない
        float intensity = GetCurrentIntensity();
        if (intensity <= 0.0f) return;

        if (m_shader == null) return;
        if (m_material == null || m_material.shader != m_shader)
        {
            CoreUtils.Destroy(m_material);
            m_material = CoreUtils.CreateEngineMaterial(m_shader);
        }

        m_material.SetFloat(s_intensityID, intensity);
        m_material.SetFloat(s_timeID, Time.unscaledTime);
        m_material.SetFloat(s_speedID, m_speed);
        m_material.SetVector(s_blockCountID, m_blockCount);
        m_material.SetFloat(s_blockAmountID, m_blockAmount);
        m_material.SetFloat(s_blockShiftID, m_blockShift);
        m_material.SetFloat(s_jitterID, m_jitter);
        m_material.SetFloat(s_colorShiftID, m_colorShift);
        m_material.SetFloat(s_scanlineID, m_scanline);

        m_pass.m_material = m_material;
        camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(m_pass);
    }
}
