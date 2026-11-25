using UnityEngine;

namespace Nakahira.DebugTools
{
    // テスト用の操作(Unity MCP から動かす)。ScriptedCommandSource.Attach(キャラ) で付けて差し替え、
    // Jump() で次のフレームに 1 回ジャンプを、Sweep() で横振りを押す。Move で移動の向き(ワールドの XZ)を入れ続ける
    public class ScriptedCommandSource : MonoBehaviour, IDarumaCommandSource
    {
        public Vector2 Move;
        bool m_jump;
        bool m_sweep;

        public static ScriptedCommandSource Attach(DarumaCharacter character)
        {
            var source = character.GetComponent<ScriptedCommandSource>();
            if (source == null) source = character.gameObject.AddComponent<ScriptedCommandSource>();
            character.SetCommandSource(source);
            return source;
        }

        public void Jump() => m_jump = true;
        // 次のフレームに 1 回、横振り(薙ぎ払い)を押す
        public void Sweep() => m_sweep = true;

        public DarumaCommand ReadCommand()
        {
            var cmd = new DarumaCommand { Move = Move, Jump = m_jump, Sweep = m_sweep };
            m_jump = false;
            m_sweep = false;
            return cmd;
        }
    }
}
