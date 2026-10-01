using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

// 残像をつかさどる
// これを付ければ誰でも残像を出せるようにする
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class AfterImage : MonoBehaviour
{
    [Header("残像の数")]
    [SerializeField]
    private int m_imageNum;

    [Header("残像が生成される間隔(秒)")]
    [SerializeField]
    private float m_imageInterval;

    [Header("残像が残る時間(秒)")]
    [SerializeField]
    private float m_imageLifespan;

    [Header("残像用マテリアル\n無いときはモデルのマテリアルを使う")]
    [SerializeField]
    private Material m_material;

    /// <summary>
    /// 残像働き中
    /// </summary>
    private bool m_isPlaying;

    // 残像を出すためのタイマー
    private float m_addTimer = 0.0f;

    private MeshRenderer m_meshRenderer;
    private MeshFilter m_meshFilter;

    // 次に使うインデックス
    private int m_addIndex = 0;

    // 残像一つを表すデータ
    private class AnImageData
    {
        // 初期化
        public AnImageData(float lifespan)
        {
            m_matrix = Matrix4x4.identity;
            m_secondLeft = lifespan;
            m_lifespan = lifespan;
        }

        public Matrix4x4 m_matrix;

        // 寿命が何秒残っているか
        public float m_secondLeft;
        // 元
        public float m_lifespan;

        // クラス内で使いまわす
        private static MaterialPropertyBlock s_property = new();
        private static int s_colorPropertyID = Shader.PropertyToID("_BaseColor");

        // 新しい命を与えよう
        public void Activation(Matrix4x4 mat)
        {
            m_matrix = mat;
            // 重ねて描画するとZファイティングが起こるのでちょっとだけ小さくする
            m_matrix = m_matrix * Matrix4x4.Scale(new(0.9f, 0.9f, 0.9f));
            m_secondLeft = m_lifespan;
        }

        public void Update()
        {
            // 寿命を減らす
            m_secondLeft -= Time.deltaTime;
        }

        public void Draw(ref MeshRenderer meshRenderer, ref MeshFilter mesh)
        {
            // 自分の存在時間に応じて透明度を上げていく
            float lifeTime = m_secondLeft / m_lifespan; // 0~1
            float alpha = Mathf.Clamp((4.0f * lifeTime * (1.0f - lifeTime)) * 0.3f, 0.0f, 0.5f);
            Color color = new(1.0f, 1.0f, 1.0f, alpha);
            s_property.SetColor(s_colorPropertyID, color);
            s_property.SetFloat("_Metallic", 0.0f);
            s_property.SetFloat("_Glossiness", 0.0f);
            s_property.SetColor("_EmissionColor", Color.black);

            // 最後にParamにして描画
            RenderParams renderParams = new(meshRenderer.material)
            {
                matProps = s_property
            };
            Graphics.RenderMesh(renderParams, mesh.sharedMesh, 0, m_matrix);

            // 描画したらプロパティを解除
            s_property.Clear();
            meshRenderer.SetPropertyBlock(null);
        }

        // 有効かどうか
        public bool IsValid()
        {
            return m_secondLeft > 0.0f;
        }
    }

    private List<AnImageData> m_datas;

    // 残像の初期化処理
    public void Init()
    {
        // インスペクタから指示された数の残像を作る
        m_datas = new();

        // マテリアル無いときはモデルのマテリアル
        if (m_material == null)
        {
            m_material = GetComponent<MeshRenderer>().material;
        }

        for (int i = 0; i < m_imageNum; ++i)
        {
            // インスタンスを最初から規定量作っておく
            m_datas.Add(new(m_imageLifespan));
        }

        m_addIndex = 0;

        m_addTimer = 0.0f;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_meshFilter = GetComponent<MeshFilter>();
        m_meshRenderer = GetComponent<MeshRenderer>();

        Init();

        SetPlay(true);
    }

    // Update is called once per frame
    void Update()
    {
        if (!m_isPlaying) return;

        // 現在の位置を残像に登録
        m_addTimer += Time.deltaTime;

        // 新しい残像を作成するタイミングになったら
        if (m_addTimer > m_imageInterval)
        {
            m_addTimer = 0.0f;

            // 残像を今の位置に生成
            // 残像が上限を超えたら
            // 最も古いものを削除して使いまわす
            m_datas[m_addIndex].Activation(transform.localToWorldMatrix);

            // Indexをループさせる
            ++m_addIndex;
            m_addIndex = m_addIndex % m_imageNum;
        }

        foreach (var data in m_datas)
        {
            // 有効でない物は描画しない
            if (!data.IsValid()) continue;

            // 寿命を減らす
            data.Update();

            data.Draw(ref m_meshRenderer, ref m_meshFilter);
        }
    }

    /// <summary>
    /// 残像を付けるかどうか
    /// </summary>
    public void SetPlay(bool playOrNot)
    {
        m_isPlaying = playOrNot;
    }
}
