using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ステージをまとめたシーンでの、ステージ1つ分のルート
    // 床・ギミック・生成エリア・TumikiSpawner などステージ固有の物を子に持ち、StageSwitcher がこのGameObjectごと有効・無効を切り替える
    // 試合(DarumaMatch)・カメラは全ステージで共有するので、このステージでの設定値だけを持つ
    public class StageEntry : MonoBehaviour
    {
        [SerializeField] StageInfo m_info;
        // 出現・復活ポイント(共有の出現ポイントをこの位置へ動かす)
        [SerializeField] Transform[] m_spawnPoints;
        // このステージの範囲(無効なGameObjectに置いた見本。共有のStageAreaへ設定を写す)
        [SerializeField] StageArea m_stageArea;
        // このステージを映すカメラの位置・向き
        [SerializeField] Transform m_cameraPoint;

        public StageInfo Info => m_info;
        public Transform[] SpawnPoints => m_spawnPoints;
        public StageArea StageArea => m_stageArea;
        public Transform CameraPoint => m_cameraPoint;
        public string DisplayName => m_info != null ? $"{m_info.Id} {m_info.Title}" : name;

        public void Set(StageInfo info, Transform[] spawnPoints, StageArea stageArea, Transform cameraPoint)
        {
            m_info = info;
            m_spawnPoints = spawnPoints;
            m_stageArea = stageArea;
            m_cameraPoint = cameraPoint;
        }
    }
}
