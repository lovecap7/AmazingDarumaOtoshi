// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
// BlockInfoの色に合わせて、積み木の見た目の色を更新する
[RequireComponent(typeof(BlockInfo), typeof(Renderer))]
public class BlockColorVisual : MonoBehaviour
{
    BlockInfo info;
    Renderer rend;

    void Awake()
    {
        info = GetComponent<BlockInfo>();
        rend = GetComponent<Renderer>();
        Apply(info.Color);                  // Prefabの初期色を反映
        info.OnColorChanged += Apply;       // 以降の変更に追従
    }

    void OnDestroy()
    {
        info.OnColorChanged -= Apply;
    }

    void Apply(BlockColor c)
    {
        // 実行中は、この積み木だけの色を変えるためrend.material(複製)を使う
        rend.material.color = ToDisplayColor(c);
    }

    static Color ToDisplayColor(BlockColor c)
    {
        switch (c)
        {
            case BlockColor.Red: return Color.red;
            case BlockColor.Blue: return Color.blue;
            case BlockColor.Green: return Color.green;
            case BlockColor.Yellow: return Color.yellow;
            default: return Color.white; // All
        }
    }
}
}
