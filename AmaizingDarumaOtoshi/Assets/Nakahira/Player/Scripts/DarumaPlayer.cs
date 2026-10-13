using UnityEngine;

namespace Nakahira
{
    // ボタンを押して試合に参加する人(ステージのシーンを直接再生したとき。PlayerInputManager が出す)
    // 同じGameObjectの IDarumaCommandSource(DarumaInputHandler等)が操作の供給元になる
    // 参加すると DarumaMatch が同じGameObjectに MatchPlayer を付け、キャラクター・幽霊・復活などはそちらで扱う
    // (セレクト画面から始めた試合の人と同じ扱いになる)
    public class DarumaPlayer : MonoBehaviour
    {
        DarumaMatch m_match;

        // 試合での1人分(参加できたら設定される)
        public MatchPlayer MatchPlayer { get; private set; }
        public int Index => MatchPlayer != null ? MatchPlayer.Index : -1;
        public DarumaCharacter Character => MatchPlayer != null ? MatchPlayer.Character : null;
        public DarumaGhost Ghost => MatchPlayer != null ? MatchPlayer.Ghost : null;
        public bool IsSurvivor => Character != null;
        // 現在操作しているキャラクターのTransform(いなければnull)
        public Transform CurrentBody => MatchPlayer != null ? MatchPlayer.Body : null;

        private void Start()
        {
            // PlayerInputManagerから参加した場合はここで試合に登録する(参加できなければ消える)
            if (m_match != null) return;
            var match = FindAnyObjectByType<DarumaMatch>();
            if (match == null || !match.Register(this))
            {
                Destroy(gameObject);
            }
        }

        // DarumaMatchから呼ばれる
        public void OnRegistered(DarumaMatch match, MatchPlayer player)
        {
            m_match = match;
            MatchPlayer = player;
        }
    }
}
