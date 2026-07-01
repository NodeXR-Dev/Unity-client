/*
 * File: GraphData.cs
 * Author: Developer 3
 * Created: 2026-05-20
 * Purpose: Top-level graph container for NodeXR.
 * Notes:
 * - graph_version: 서버가 그래프 변경 후 최신 상태 판단에 사용할 수 있음.
 * - 서버 응답이 { result: { graph: {...} } } 형태로 감싸질 경우
 *   별도 GraphResponse wrapper 클래스가 필요하며, 이후 서버 API 연결 시 추가한다.
 */
using System.Collections.Generic;

[System.Serializable]
public class GraphData
{
    public string room_id;
    public int graph_version;
    public List<NodeData> nodes = new List<NodeData>();
    public List<EdgeData> edges = new List<EdgeData>();
}
