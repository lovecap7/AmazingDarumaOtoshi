using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 「＜ フルスクリーン ＞」のように左右で選択肢を切り替えるUI。
// コントローラーの左右・矢印キー・両端の矢印ボタンのクリックで切り替わる。
public class OptionSelector : Selectable
{
    [SerializeField] TMP_Text valueLabel;
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
    [SerializeField] bool loop = true;

    [Serializable] public class IntEvent : UnityEvent<int> { }
    public IntEvent onValueChanged = new IntEvent();

    string[] options = Array.Empty<string>();
    int index;

    public int Index => index;

    protected override void Awake()
    {
        base.Awake();
        if (!Application.isPlaying) return;
        if (prevButton != null) prevButton.onClick.AddListener(() => { Select(); Step(-1); });
        if (nextButton != null) nextButton.onClick.AddListener(() => { Select(); Step(+1); });
    }

    // 選択肢を差し替える（通知はしない）
    public void SetOptions(string[] newOptions, int selected)
    {
        options = newOptions ?? Array.Empty<string>();
        index = Mathf.Clamp(selected, 0, Mathf.Max(0, options.Length - 1));
        Refresh();
    }

    public void Step(int dir)
    {
        if (!IsInteractable() || options.Length == 0) return;

        int next = index + dir;
        if (loop) next = (next % options.Length + options.Length) % options.Length;
        else next = Mathf.Clamp(next, 0, options.Length - 1);
        if (next == index) return;

        index = next;
        Refresh();
        onValueChanged.Invoke(index);
    }

    void Refresh()
    {
        if (valueLabel != null) valueLabel.text = options.Length > 0 ? options[index] : "-";
    }

    public override void OnMove(AxisEventData eventData)
    {
        switch (eventData.moveDir)
        {
            case MoveDirection.Left: Step(-1); eventData.Use(); break;
            case MoveDirection.Right: Step(+1); eventData.Use(); break;
            default: base.OnMove(eventData); break;
        }
    }
}
