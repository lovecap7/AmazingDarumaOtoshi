using System.Collections.Generic;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // 積み木同士がぶつかったときの振る舞いをキー入力で切り替える(デバッグ用)
    // 押すたびに「破壊(既定の連鎖)」→ 登録した振る舞い →… の順に切り替え、盤面のすべての積み木とこれから出る積み木に適用する
    public class TumikiImpactModeToggle : MonoBehaviour
    {
        [SerializeField] Key m_key = Key.M;
        // 破壊(既定)の次に順番に切り替える振る舞い
        [SerializeField] TumikiImpactAsset[] m_modes;
        [SerializeField] bool m_showLabel = true;
        [SerializeField] bool m_log = true;

        // 0: 破壊(既定)、1以降: m_modes[i - 1]
        int m_index;

        public ITumikiImpactBehavior Current => m_index == 0 ? TumikiBreakImpact.Instance : m_modes[m_index - 1];
        public string CurrentName => m_index == 0 ? "破壊" : m_modes[m_index - 1].DisplayName;
        int ModeCount => 1 + (m_modes != null ? m_modes.Length : 0);

        private void Start()
        {
            // 再生のたびに既定(破壊)から始める
            Apply(0);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[m_key].wasPressedThisFrame) Apply((m_index + 1) % ModeCount);
        }

        public void Apply(int index)
        {
            m_index = Mathf.Clamp(index, 0, ModeCount - 1);
            ITumikiImpactBehavior behavior = Current;
            TumikiBlock.DefaultImpactBehavior = behavior;
            // 積み木ごとに個別の振る舞いが設定されていても、切り替えたときは盤面のすべてに揃える
            int count = 0;
            foreach (var block in FindObjectsByType<TumikiBlock>())
            {
                block.ImpactBehavior = behavior;
                count++;
            }
            if (m_log) Debug.Log($"[TumikiImpactModeToggle] 積み木の衝突: {CurrentName} (盤面の{count}個に適用)");
        }

        private void OnGUI()
        {
            if (!m_showLabel) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            GUI.Box(new Rect(10, 50, 420, 34), $" 積み木の衝突: {CurrentName}  [{m_key}]", style);
        }
    }
}
