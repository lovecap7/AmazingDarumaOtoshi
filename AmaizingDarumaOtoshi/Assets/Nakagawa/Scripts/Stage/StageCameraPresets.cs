using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ステージごとのカメラの位置・角度・視野角(ステージの id ごと)。ステージの CameraPoint の代わりに使う
    //  ・再生中に StageCameraTuner で調整して保存する(エディタで再生しているときだけ、このアセットに書き込める)
    //  ・StageSwitcher はステージを表示するとき、ここに設定があればステージの CameraPoint の代わりに使う
    //  ・StageBuilder / StageCombiner でステージを作り直しても消えない(作り直したステージの CameraPoint にも写される)
    [CreateAssetMenu(menuName = "Nakagawa/Stage Camera Presets", fileName = "StageCameraPresets")]
    public class StageCameraPresets : ScriptableObject
    {
        [System.Serializable]
        public class Preset
        {
            public string StageId;
            public Vector3 Position;
            public Vector3 Euler;
            [Tooltip("視野角(0以下ならカメラの既定のまま)")]
            public float Fov;

            public Quaternion Rotation => Quaternion.Euler(Euler);
        }

        [SerializeField] List<Preset> m_presets = new List<Preset>();

        public IReadOnlyList<Preset> Presets => m_presets;

        // id のステージの設定(なければ null)
        public Preset Find(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) return null;
            foreach (var p in m_presets)
            {
                if (p != null && p.StageId == stageId) return p;
            }
            return null;
        }

        public void Set(string stageId, Vector3 position, Quaternion rotation, float fov)
        {
            var p = Find(stageId);
            if (p == null)
            {
                p = new Preset { StageId = stageId };
                m_presets.Add(p);
                m_presets.Sort((a, b) => string.CompareOrdinal(a.StageId, b.StageId));
            }
            p.Position = position;
            p.Euler = rotation.eulerAngles;
            p.Fov = fov;
        }

        // 消したら true
        public bool Remove(string stageId)
        {
            return m_presets.RemoveAll(p => p != null && p.StageId == stageId) > 0;
        }
    }
}
