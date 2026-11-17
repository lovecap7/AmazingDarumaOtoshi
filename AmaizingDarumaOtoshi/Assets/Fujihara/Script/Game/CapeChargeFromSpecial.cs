using UnityEngine;

// 試合のだるきんぐのマント（CapeSway）を、必殺技「エクスカリバー」の溜めに合わせて大きくなびかせる。
// 詠唱（溜め）〜カットイン〜振りかぶりの間は Charge を上げ、終わったら 0 に戻す（CapeSway がなめらかに追いかける）。
// GameModelSwapper がマントを付けるときに一緒に付ける（必殺技のスクリプトは読むだけで、変えない）
public class CapeChargeFromSpecial : MonoBehaviour
{
    [Tooltip("溜め始めてから最大になるまでの秒数")]
    [SerializeField] float rampTime = 0.8f;

    CapeSway sway;
    Yurube.DaruKingSpecial special;
    float charge;

    public void Init(CapeSway target, Transform body)
    {
        sway = target;
        special = body != null ? body.GetComponentInParent<Yurube.DaruKingSpecial>() : null;
        if (special == null && body != null) special = body.GetComponentInChildren<Yurube.DaruKingSpecial>(true);
    }

    void Update()
    {
        if (sway == null) return;
        bool active = special != null && special.isActiveAndEnabled && special.IsThreatening;
        charge = active ? Mathf.MoveTowards(charge, 1f, Time.deltaTime / Mathf.Max(rampTime, 0.01f)) : 0f;
        sway.Charge = charge;
    }
}
