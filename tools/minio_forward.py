#!/usr/bin/env python3
"""MinIO 외부 접근용 네이티브 TCP 포워더.

퀘스트에서 uvicorn(8000, 네이티브 프로세스)은 잘 닿는데 MinIO(9000, Docker Desktop
퍼블리시)는 즉시 연결 거부된다. Docker Desktop for Mac 이 퍼블리시한 포트가 LAN 의
다른 기기에서 안 잡히는 경우가 있어, 네이티브 파이썬 프로세스로 중계한다.

    퀘스트 → 0.0.0.0:9100 (이 스크립트) → 127.0.0.1:9000 (MinIO)

서버 코드/리포는 건드리지 않는다. .env 의 MINIO_PUBLIC_BASE_URL 만 이 포트로 바꾸면
서버가 내려주는 img_url 이 이 경로를 가리키게 된다.
"""
import asyncio

LISTEN_HOST = "0.0.0.0"
LISTEN_PORT = 9100
TARGET_HOST = "127.0.0.1"
TARGET_PORT = 9000


async def pipe(reader, writer):
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                break
            writer.write(data)
            await writer.drain()
    except Exception:
        pass
    finally:
        try:
            writer.close()
        except Exception:
            pass


async def handle(client_reader, client_writer):
    peer = client_writer.get_extra_info("peername")
    try:
        target_reader, target_writer = await asyncio.open_connection(
            TARGET_HOST, TARGET_PORT
        )
    except Exception as exc:
        print(f"[forward] {peer} → MinIO 연결 실패: {exc}", flush=True)
        client_writer.close()
        return

    print(f"[forward] {peer} 연결", flush=True)
    await asyncio.gather(
        pipe(client_reader, target_writer),
        pipe(target_reader, client_writer),
    )


async def main():
    server = await asyncio.start_server(handle, LISTEN_HOST, LISTEN_PORT)
    print(
        f"[forward] {LISTEN_HOST}:{LISTEN_PORT} → {TARGET_HOST}:{TARGET_PORT} 시작",
        flush=True,
    )
    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    asyncio.run(main())
