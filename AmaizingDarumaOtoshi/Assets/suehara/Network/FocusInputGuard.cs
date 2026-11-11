using UnityEngine;
using UnityEngine.EventSystems;

// このウィンドウが選ばれていない間は、UI(EventSystem)のカーソル移動・決定・取り消しを止める。
// 同じ PC で複数の画面を動かす(Multiplayer Play Mode など)と、キーボード・コントローラーの入力が裏の画面にも届き、
// メインメニューなど EventSystem で操作する画面が、別のウィンドウの操作で動いてしまうため。
// (MenuInput・ネット対戦のメニュー・キャラの操作は、それぞれ Application.isFocused で止めている)
// 他の人の画面は変えず、起動時に自動で1つ作られる(シーンをまたいで残る)。
//  ・止めるのは EventSystem.sendNavigationEvents だけ(マウスのクリックは、選んでいるウィンドウにしか届かないのでそのまま)
//  ・場面を移る間は藤原さんの Fader が同じ設定で止めているので、ここで止めたものだけを戻す
public class FocusInputGuard : MonoBehaviour
{
    EventSystem m_stopped;   // ここで止めた EventSystem(選ばれたら戻す)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("FocusInputGuard");
        DontDestroyOnLoad(go);
        go.AddComponent<FocusInputGuard>();
    }

    void Update()
    {
        var es = EventSystem.current;
        if (!Application.isFocused)
        {
            if (es != null && es.sendNavigationEvents)
            {
                es.sendNavigationEvents = false;
                m_stopped = es;
            }
            return;
        }
        // 選ばれたら、ここで止めたものだけを戻す(シーンが変わって別の EventSystem になっていたら何もしない)
        if (m_stopped != null && m_stopped == es && !Fader.IsBusy) es.sendNavigationEvents = true;
        m_stopped = null;
    }
}
