using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 左に並ぶメニューの積み木1個。マウス操作を MenuController に伝え、暗さを受け持つ。
[RequireComponent(typeof(RectTransform))]
public class MenuBlock : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Tooltip("暗くするための黒い Image（ブロックの子で最前面、Raycast Target OFF）")]
    [SerializeField] Image dimOverlay;

    public MenuController Owner { get; set; }
    public int Index { get; set; }
    public RectTransform Rect => (RectTransform)transform;

    public void SetDim(float amount)
    {
        if (dimOverlay == null) return;
        var c = dimOverlay.color;
        c.a = amount;
        dimOverlay.color = c;
    }

    public void OnPointerEnter(PointerEventData eventData) => Owner?.OnBlockHover(Index);
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Owner?.OnBlockClick(Index);
    }
}
