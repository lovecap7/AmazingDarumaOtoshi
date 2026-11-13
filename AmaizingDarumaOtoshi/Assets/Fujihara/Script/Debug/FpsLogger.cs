using UnityEngine;

// 重くなっていないか確かめる用。interval 秒ごとに、その間の平均 FPS・一番遅かったフレームの FPS・1フレームの平均時間をコンソールに出す。
// 確かめたいシーンの、どれか1つのオブジェクトに付けるだけでよい
public class FpsLogger : MonoBehaviour
{
    [Tooltip("何秒ごとにコンソールに書くか")]
    [SerializeField, Min(0.1f)] float interval = 1f;
    [Tooltip("ログの先頭に付ける名前（どの場面の計測か分かるように）")]
    [SerializeField] string label = "FPS";

    float elapsed, worst;
    int frames;

    void OnEnable() { elapsed = 0f; frames = 0; worst = 0f; }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;   // スローモーション中でも本当の速さで測る
        elapsed += dt;
        frames++;
        if (dt > worst) worst = dt;
        if (elapsed < interval) return;

        float avg = frames / elapsed;
        Debug.Log($"[{label}] 平均 {avg:F1} fps（{elapsed / frames * 1000f:F2} ms）　一番遅いフレーム {1f / Mathf.Max(worst, 1e-5f):F1} fps（{worst * 1000f:F2} ms）");
        elapsed = 0f; frames = 0; worst = 0f;
    }
}
