using UnityEngine;

namespace Nakagawa
{
    // ステージの説明(コンセプト・予想される試合展開など)。ゲームの動作には影響しない
    // ステージ選択画面やデバッグ表示から読む想定
    public class StageInfo : MonoBehaviour
    {
        [SerializeField] string m_id;
        [SerializeField] string m_title;
        [SerializeField, TextArea(2, 6)] string m_concept;
        [SerializeField, TextArea(3, 10)] string m_expectedFlow;
        // 使っているギミック・生成エリアなどの構成の要約
        [SerializeField, TextArea(2, 6)] string m_layout;

        public string Id => m_id;
        public string Title => m_title;
        public string Concept => m_concept;
        public string ExpectedFlow => m_expectedFlow;
        public string Layout => m_layout;

        public void Set(string id, string title, string concept, string expectedFlow, string layout)
        {
            m_id = id;
            m_title = title;
            m_concept = concept;
            m_expectedFlow = expectedFlow;
            m_layout = layout;
        }
    }
}
