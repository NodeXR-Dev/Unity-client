/*
 * 파일명: MeetingRoomHistoryNodeBox.cs
 * 목적: 디자이너의 NodeBox 프리팹(05_Design/JW/UI/Prefabs/NodeBox)을 그래프 데이터와 연결하는 어댑터.
 *
 * NodeBox 구조:
 *   Root(= NodeBox2.fbx 박스 몸체)
 *   ├── Canvas > NodeName (TextMeshProUGUI 라벨)
 *   ├── Port_In  (왼쪽 엣지 연결점)
 *   └── Port_Out (오른쪽 엣지 연결점)
 *
 * NodeBox 에는 바인딩 스크립트가 없으므로, 렌더러가 인스턴스에 이 컴포넌트를 AddComponent 한 뒤
 * Init() → SetText()/SetColor() 로 데이터를 채운다. 엣지는 PortIn/PortOut 위치를 쓴다.
 */
using TMPro;
using UnityEngine;

public class MeetingRoomHistoryNodeBox : MonoBehaviour
{
    private TMP_Text _label;
    private Transform _portIn;
    private Transform _portOut;
    private Renderer _body;

    // 엣지 연결점 (없으면 노드 중심으로 폴백)
    public Transform PortIn => _portIn != null ? _portIn : transform;
    public Transform PortOut => _portOut != null ? _portOut : transform;

    public void Init()
    {
        _label = GetComponentInChildren<TMP_Text>(true);
        _portIn = FindDeep("Port_In");
        _portOut = FindDeep("Port_Out");
        _body = GetComponentInChildren<MeshRenderer>(true); // FBX 박스 몸체
    }

    public void SetText(string text)
    {
        if (_label != null) _label.text = text;
    }

    public void SetColor(Color color)
    {
        if (_body == null) return;
        var mpb = new MaterialPropertyBlock();
        _body.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", color); // URP Lit
        mpb.SetColor("_Color", color);     // Standard 폴백
        _body.SetPropertyBlock(mpb);
    }

    // 자식 전체에서 이름으로 Transform 탐색 (Port_In / Port_Out)
    private Transform FindDeep(string targetName)
    {
        var all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
            if (t.name == targetName) return t;
        return null;
    }
}
