# tools

개발자3 로컬 검증용 보조 스크립트. 빌드에 포함되지 않는다(`Assets/` 밖).

## minio_forward.py — 2D 이미지가 헤드셋에서 안 받아질 때

**증상**

Quest 에서 2D 생성은 되는데(WS `2D_GENERATED` 수신됨) 이미지 다운로드만 즉시 실패한다.

```
[Generate2DController] 2D 이미지 다운로드 → http://<맥IP>:9000/...
[Generate2DController] 2D 이미지 다운로드 실패: Cannot connect to destination host (code=0)
```

**원인**

MinIO(9000)는 Docker Desktop 이 퍼블리시한 포트라 **LAN 의 다른 기기에서 닿지 않는다.**
uvicorn(8000)은 네이티브 프로세스라 정상이므로, REST/WS 는 되는데 이미지만 실패하는 형태로 나타난다.

맥에서 자기 LAN IP 로 `curl` 하면 루프백으로 돌아 성공하므로 **오진하기 쉽다.**
"서버는 되는데 이미지만 안 된다"면 이 문제를 먼저 의심할 것.

**사용**

```bash
python3 tools/minio_forward.py      # 0.0.0.0:9100 → 127.0.0.1:9000
```

띄운 뒤 서버 `.env` 를 바꾸고 uvicorn 을 재시작한다(`.env` 는 프로세스 시작 시에만 읽힌다).

```
MINIO_PUBLIC_BASE_URL=http://<맥 LAN IP>:9100
```

**임시 조치다.** 근본 해결은 서버팀이 MinIO 를 네이티브로 띄우거나 Docker 네트워크 설정을
바꾸는 것이다. 참고: `docs/server-api-alignment.md` 의 2026-08-02 절.
