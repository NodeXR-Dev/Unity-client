/*
 * File: NodeData.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Defines graph node data for NodeXR.
 * Notes:
 * - type is stored as string for server JSON compatibility (JsonUtility serializes enum as int).
 * - property_category is AI-generated free text, not a fixed enum.
 * - label and node_text both exist because server responses may use either field.
 */
using UnityEngine;

[System.Serializable]
public class NodeData
{
    public string node_id;
    public string type;       // "PART" | "PROPERTY" | "REFERENCE"
    public string label;      // 주 표시명
    public string node_text;  // 서버에서 label 대신 node_text로 올 수 있음

    public float[] position;  // [x, y, z] — 서버 JSON 배열 형식과 일치

    // PROPERTY 전용 — AI가 자유롭게 생성하는 문자열, enum으로 제한하지 않음
    public string property_category;

    // PART 전용 — ALL 파트 판별용
    // TODO: 추후 서버 스펙에 따라 data: { is_global: true } 형태로 옮길 수 있음
    public bool is_global;

    // --- 편의 프로퍼티 (직렬화 대상 아님) ---

    // label 우선, 없으면 node_text 반환
    public string DisplayText =>
        !string.IsNullOrEmpty(label) ? label : node_text;

    // type 문자열 → NodeType enum. 파싱 실패 시 UNKNOWN 반환
    public NodeType NodeType
    {
        get
        {
            if (System.Enum.TryParse(type, ignoreCase: true, out NodeType result))
                return result;
            return NodeType.UNKNOWN;
        }
    }

    // position 배열 → Vector3
    public Vector3 Position =>
        position != null && position.Length == 3
            ? new Vector3(position[0], position[1], position[2])
            : Vector3.zero;

    // 노드 이동 시 position 배열 갱신 (GraphManager.RequestMoveNode에서 사용 예정)
    public void SetPosition(Vector3 newPosition)
    {
        if (position == null || position.Length != 3)
            position = new float[3];
        position[0] = newPosition.x;
        position[1] = newPosition.y;
        position[2] = newPosition.z;
    }
}
