using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // CPU(コンピュータープレイヤー)。人の入力と同じ操作命令(DarumaCommand)を出してキャラクターを動かす
    // 試合では MatchPlayer と同じGameObjectに付ける(操作する体は MatchPlayer から取る)。
    // キャラクターに直接付けた場合はそのキャラクターを動かす
    // 行動は部品に分けてある。今は「積む」(CpuCollect)だけ
    public class DarumaCpu : MonoBehaviour, IDarumaCommandSource
    {
        [SerializeField] CpuParams m_params;
        // 狙っている積み木をシーンビューに表示する
        [SerializeField] bool m_drawGizmos = true;

        MatchPlayer m_player;
        DarumaCharacter m_character;
        readonly CpuCollect m_collect = new CpuCollect();
        readonly List<Vector3> m_opponents = new List<Vector3>();

        public CpuParams Params
        {
            get => m_params;
            set => m_params = value;
        }

        public DarumaCommand ReadCommand()
        {
            var cmd = new DarumaCommand();
            DarumaCharacter self = FindCharacter();
            // 幽霊のときは今は何もしない
            if (self == null || m_params == null) return cmd;

            GatherOpponents(self);
            m_collect.Tick(self, m_params, m_opponents, Time.deltaTime, ref cmd);
            return cmd;
        }

        private DarumaCharacter FindCharacter()
        {
            // MatchPlayer はこのコンポーネントより後に付くので、使うときに取る
            if (m_player == null) m_player = GetComponent<MatchPlayer>();
            if (m_player != null) return m_player.Character;
            if (m_character == null) m_character = GetComponent<DarumaCharacter>();
            return m_character;
        }

        // 自分以外の体(生存キャラクター・幽霊)の位置
        private void GatherOpponents(DarumaCharacter self)
        {
            m_opponents.Clear();
            var match = DarumaMatch.Current;
            if (match == null) return;
            foreach (var p in match.Players)
            {
                if (p == null || p.Body == null || p.Body == self.transform) continue;
                m_opponents.Add(p.Body.position);
            }
        }

        private void OnDrawGizmos()
        {
            if (!m_drawGizmos || !Application.isPlaying) return;
            DarumaCharacter self = FindCharacter();
            TumikiBlock target = m_collect.Target;
            if (self == null || target == null) return;

            // たどっている道(通り過ぎた曲がり角は除く)
            Gizmos.color = Color.cyan;
            Vector3 from = self.transform.position;
            for (int i = m_collect.RouteIndex; i < m_collect.RouteCount; i++)
            {
                Vector3 corner = m_collect.RouteCorner(i);
                Gizmos.DrawLine(from + Vector3.up * 0.1f, corner + Vector3.up * 0.1f);
                from = corner;
            }
            Gizmos.DrawWireSphere(target.transform.position, TumikiBlock.kRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(m_collect.AimPoint, 0.15f);
        }
    }
}
