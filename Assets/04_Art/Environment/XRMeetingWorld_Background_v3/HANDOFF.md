# 배경 에셋 전달 — XR 시작 화면 세팅

**XR Meeting World v3 / 서서 플레이(standing) 기준**

디자이너 → 개발자 전달 문서. 좌표를 직접 잴 필요 없게 **FBX 안에 로케이터를 심어놨습니다.**
아래 순서대로 하면 됩니다.

---

## 1. 전달 파일

| 파일 | 용도 |
|---|---|
| `XRMeetingWorld.fbx` | 월드 전체 (건물 + 지형 + 나무 + 호수) |
| `skybox/Sunset_Sky.exr` | **노을 하늘.** FBX엔 하늘이 안 담기므로 이걸 씁니다 |
| `skybox/Sunset_Sky.png` | 위와 같은 그림, 룩 확인용 |
| `preview/*.png` | 의도한 최종 룩 — 결과가 이것과 다르면 세팅이 빠진 것 |
| `optimization/` | 성능 최적화용 (6절). 1차 통합엔 필요 없습니다 |

---

## 2. FBX 임포트

Model 탭
- **Scale Factor 1**, **Convert Units 체크 해제** → 1 unit = 1 m로 그대로 맞습니다
- Lightmap UV 베이크할 거면 *Generate Lightmap UVs* 켜기

Materials 탭에서 *Extract Materials*. **투명 머티리얼 3개만 손보면 됩니다** (FBX가 알파를 못 옮김):

| 머티리얼 | Surface Type | Alpha | Smoothness |
|---|---|---|---|
| `M_Glass_Curtainwall` | Transparent | 0.10 | 0.95 |
| `M_Glass_Skylight` | Transparent | 0.20 | 0.96 |
| `M_Water` | Transparent | 0.85 | 0.97 |

나머지는 Opaque 그대로 두시면 됩니다.

> 지붕 유리(`M_Glass_Skylight`)는 Blender에서 프레넬로 알파를 조절했는데 URP/Lit엔 같은 기능이
> 없습니다. 밖에서 봤을 때 지붕이 여러 장의 판으로 분해돼 보이면 알파를 0.35까지 올리거나,
> Shader Graph에서 Fresnel Effect 노드를 Alpha에 연결해 주세요.

---

## 3. FBX 안의 로케이터 3개

임포트하면 아래 3개의 빈 GameObject가 같이 들어옵니다. **위치는 이미 맞춰져 있으니 회전값만
아래 표대로 넣어주시면 됩니다** (FBX는 빈 오브젝트의 회전을 안정적으로 못 옮깁니다).

| 이름 | Unity Position | Unity Rotation | 용도 |
|---|---|---|---|
| `SPAWN_User` | (9.00, 0.22, 6.00) | (0, **198**, 0) | XR Origin을 여기 놓으면 노을을 정면으로 봅니다 |
| `ANCHOR_StartUI` | (8.44, 1.45, 4.29) | (0, **18**, 0) | 시작 UI 캔버스를 여기 붙입니다 |
| `DIM_SphereCenter` | (9.00, 1.82, 6.00) | — | 어둡게 덮는 스피어 중심 (= 사용자 눈높이) |

- 사용자 정면 방향과 UI 패널 방향이 정확히 180° 차이입니다 (198° ↔ 18°). 패널이 사용자를
  마주보게 하려면 그렇게 돼야 합니다.
- UI 패널은 사용자 앞 **1.8 m**, 높이 **1.45 m** (눈높이보다 살짝 아래 — VR UI 표준 배치).

---

## 4. 하늘 + 조명

**스카이박스**
1. `Sunset_Sky.exr` 임포트 → Texture Shape를 **Cube**, Mapping을 **Latitude-Longitude (Cylindrical)**
2. Material 새로 만들고 Shader = **Skybox/Panoramic**, 위 텍스처 연결
3. Lighting 창 → Environment → Skybox Material에 지정
4. 머티리얼의 **Rotation 슬라이더**로 노을 방향을 디렉셔널 라이트와 맞춰주세요
   (라이트가 향하는 쪽 = Unity yaw 18° 방향에 노을이 오게)

**디렉셔널 라이트**

| 항목 | 값 |
|---|---|
| Rotation | **(25, 18, 0)** |
| Color | **#FFC996** |
| Intensity | 1.0 ~ 1.4 사이에서 조정 |
| Shadows | Soft |

Lighting → Environment Lighting Source = **Skybox**, Intensity Multiplier 1.0.

---

## 5. 시작 화면 만들기

### 5-1. 어둡게 덮는 스피어

VR에선 전체 화면 블러를 쓰지 않습니다(스테레오라 비용 2배 + 멀미 요인). 대신 사용자를 감싸는
반투명 검은 구로 월드를 눌러줍니다.

1. Sphere 생성 → 이름 `MenuDimSphere`
2. **XR Origin의 자식으로** 넣기 (사용자를 항상 따라다녀야 함)
3. Transform: Position (0, 1.6, 0), **Scale (24, 24, 24)** ← 반지름 12 m
4. Material: Shader **URP/Unlit**
   - Surface Type: **Transparent**
   - **Render Face: Back** ← 이게 핵심. 구 안쪽에서 보이게 해줍니다 (메시 뒤집기 불필요)
   - Base Color: 검정, **Alpha 0.55** (0.45~0.65 사이에서 취향껏)
5. Mesh Renderer → **Cast Shadows: Off**, Receive Shadows 해제
6. 메뉴 열릴 때 `SetActive(true)`, 시작 누르면 `false`

> 알파를 0.7 이상 올리면 배경이 거의 안 보여서 "월드 안에 있다"는 느낌이 사라집니다.
> 0.55 정도가 UI 가독성과 배경 존재감의 균형점입니다.

### 5-2. 기존 UI를 World Space로

**중요: VR에선 Screen Space - Overlay 캔버스가 헤드셋에 아예 렌더되지 않습니다.**
지금 만들어두신 UI가 Overlay면 아래처럼 바꿔야 합니다.

1. Canvas → Render Mode를 **World Space**로 변경
2. Rect Transform:
   - Width **1200**, Height **800**
   - **Scale (0.001, 0.001, 0.001)** → 실제 1.2 m × 0.8 m 패널이 됩니다
   - Position / Rotation은 `ANCHOR_StartUI` 값 그대로 (또는 그 오브젝트의 자식으로 넣기)
3. Canvas의 `Graphic Raycaster`를 제거하고 **`Tracked Device Graphic Raycaster`** 추가
4. EventSystem의 Input Module을 **`XR UI Input Module`** 로 교체
5. 컨트롤러의 `XR Ray Interactor`에서 UI 상호작용 켜기

**폰트 크기**: 스케일 0.001에서는 폰트 28~36 px가 1.8 m 거리 기준 읽기 편한 최소선입니다.
20 px 이하는 헤드셋에서 뭉개집니다.

### 5-3. 카메라

VR에선 **카메라를 임의로 움직이지 마세요.** 데스크톱 시작 화면처럼 천천히 공전시키면
멀미납니다. 사용자는 `SPAWN_User` 위치에 가만히 서 있고, 고개만 자유롭게 돌리는 게 맞습니다.

---

## 6. 성능 (헤드셋 타겟이면 권장)

씬 FBX는 나무·바위·풀 1,670개를 개별 오브젝트로 담고 있어 드로우콜이 많습니다.

1. `optimization/props/*.fbx` 를 각각 프리팹으로 임포트
2. 씬에서 `__Nature_Trees`, `__Nature_Rocks`, `__Nature_Undergrowth` 삭제
3. `optimization/scatter.json` 을 읽어 프리팹 인스턴스화
   - 좌표 변환: JSON의 `p: [x, y, z]` → Unity `(x, z, y)`
   - 회전: Y축에 `-rz` (라디안 → 도)
4. 각 프리팹 머티리얼에 **Enable GPU Instancing** 체크

→ 자연 요소 전체가 **12 드로우콜**로 떨어집니다.

---

## 7. 체크리스트

- [ ] FBX Scale Factor 1 / Convert Units 해제
- [ ] 투명 머티리얼 3개 Transparent 전환
- [ ] 스카이박스 EXR → Cube / Lat-Long, Rotation으로 노을 방향 정렬
- [ ] 디렉셔널 라이트 (25, 18, 0), #FFC996
- [ ] XR Origin을 `SPAWN_User` 위치·회전에 배치
- [ ] `MenuDimSphere` — Render Face **Back**, Alpha 0.55, XR Origin 자식
- [ ] Canvas를 World Space로, Scale 0.001, `ANCHOR_StartUI`에 배치
- [ ] Tracked Device Graphic Raycaster + XR UI Input Module 교체
- [ ] 헤드셋에서 실제로 착용하고 패널 거리·높이 확인 (앉은 자세면 높이 조정 필요)

마지막 항목이 실제로 제일 중요합니다. 1.45 m는 서 있는 성인 기준이라, 앉아서 쓰는 시나리오면
패널을 0.2~0.3 m 낮춰야 합니다.
