using UnityEngine;

namespace Nakahira.DebugTools
{
    // テスト用の操作(Unity MCP から動かす)。ScriptedCommandSource.Attach(キャラ) で付けて差し替え、
    // Jump() で次のフレームに 1 回ジャンプを押す。Move で移動の向き(ワールドの XZ)を入れ続ける
    public class ScriptedCommandSource : MonoBehaviour, IDarumaCommandSource
    {
        public Vector2 Move;
        bool m_jump;

        public static ScriptedCommandSource Attach(DarumaCharacter character)
        {
            var source = character.GetComponent<ScriptedCommandSource>();
            if (source == null) source = character.gameObject.AddComponent<ScriptedCommandSource>();
            character.SetCommandSource(source);
            return source;
        }

        public void Jump() => m_jump = true;

        public DarumaCommand ReadCommand()
        {
            var cmd = new DarumaCommand { Move = Move, Jump = m_jump };
            m_jump = false;
            return cmd;
        }
    }
}
