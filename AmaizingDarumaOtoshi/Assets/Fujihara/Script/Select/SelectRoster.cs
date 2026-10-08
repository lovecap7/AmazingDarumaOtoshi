using System.Collections.Generic;
using UnityEngine;

// セレクト画面に並べるキャラクターとステージの一覧。ここに足せば画面に並ぶ（並びは自動で調整される）。
[CreateAssetMenu(menuName = "Daruma/Select Roster", fileName = "SelectRoster")]
public class SelectRoster : ScriptableObject
{
    public List<CharacterData> characters = new List<CharacterData>();
    public List<StageData> stages = new List<StageData>();
}
