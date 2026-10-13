using System;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // 積み木同士がぶつかったときのモードをキー入力で切り替える(デバッグ用)
    // 押すたびに登録したモードを順に切り替え(最後の次は最初に戻る)、盤面のすべての積み木とこれから出る積み木に適用する
    // 既定の並び: 1回ヒットで破壊 → 2回ヒットで破壊 → 3回ヒットで破壊 → 非破壊
    public class TumikiImpactModeToggle : MonoBehaviour
    {
        [Serializable]
        public class Mode
        {
            public string Name = "破壊";
            [Tooltip("当てたときの振る舞い(未設定なら既定の連鎖。耐久度を使うなら TumikiDurabilityImpact)")]
            public TumikiImpactAsset Impact;
            [Tooltip("積み木の耐久度の最大値(何回当てると壊れるか)。0なら壊れない")]
            [Min(0)] public int Durability = 1;
        }

        [SerializeField] Key m_key = Key.M;
        [SerializeField] Mode[] m_modeList;
        // 再生開始時のモード
        [SerializeField, Min(0)] int m_startIndex = 0;
        [SerializeField] bool m_showLabel = true;
        [SerializeField] bool m_log = true;

        static readonly Mode kFallback = new Mode();

        int m_index;

        public Mode Current => m_modeList != null && m_modeList.Length > 0 ? m_modeList[m_index] : kFallback;
        public ITumikiImpactBehavior CurrentBehavior => Current.Impact != null ? Current.Impact : TumikiBreakImpact.Instance;
        int ModeCount => m_modeList != null && m_modeList.Length > 0 ? m_modeList.Length : 1;

        private void Start()
        {
            Apply(m_startIndex);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[m_key].wasPressedThisFrame) Apply((m_index + 1) % ModeCount);
        }

        public void Apply(int index)
        {
            m_index = Mathf.Clamp(index, 0, ModeCount - 1);
            Mode mode = Current;
            ITumikiImpactBehavior behavior = CurrentBehavior;
            TumikiBlock.DefaultImpactBehavior = behavior;
            TumikiDurability.DefaultMax = mode.Durability;

            // 積み木ごとに個別の設定があっても、切り替えたときは盤面のすべてに揃える(耐久度は満タンから)
            int count = 0;
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                block.ImpactBehavior = behavior;
                var durability = block.GetComponent<TumikiDurability>();
                if (durability != null) durability.SetMax(mode.Durability, true);
                count++;
            }
            if (m_log) Debug.Log($"[TumikiImpactModeToggle] 積み木の衝突: {mode.Name} (盤面の{count}個に適用)");
        }

        private void OnGUI()
        {
            if (!m_showLabel) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            GUI.Box(new Rect(10, 50, 420, 34), $" 積み木の衝突: {Current.Name}  [{m_key}]", style);
        }
    }
}
