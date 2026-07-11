// 레퍼런스(REFERENCE 노드) 서버 REST API DTO. (API 명세 2026-07-10)
//   키워드 추천 POST /api/references/keyword  { room_id, node_id } → { room_id, node_id, reference_keyword }
//   연결(업로드) POST /api/references/generate (multipart) { room_id, file(BIN), node_id, metadata:{mime_type,width,height} }
//                → { room_id, node_id, reference_url }. 서버가 이미지 저장 + REFERENCE 노드화(그래프 반영은 GRAPH_UPDATED/조회로).

[System.Serializable]
public class ReferenceKeywordRequest
{
    public string room_id;
    public string node_id;
}

[System.Serializable]
public class ReferenceKeywordResult
{
    public string room_id;
    public string node_id;
    public string reference_keyword;
}

[System.Serializable]
public class ReferenceKeywordResponse
{
    public bool isSuccess;
    public string code;      // 예: REFERENCE200
    public string message;
    public ReferenceKeywordResult result;
}

// multipart 의 metadata 필드(JSON 문자열로 실어 보낸다).
[System.Serializable]
public class ReferenceMetadata
{
    public string mime_type;
    public int width;
    public int height;
}

[System.Serializable]
public class ReferenceGenerateResult
{
    public string room_id;
    public string node_id;
    public string reference_url;
}

[System.Serializable]
public class ReferenceGenerateResponse
{
    public bool isSuccess;
    public string code;      // 예: REFERENCE201
    public string message;
    public ReferenceGenerateResult result;
}
