# UGDB — Unity Graphics Debug Bridge

RenderDoc에서 복사한 GPU 바인딩 정보를 Unity 에디터에 붙여넣으면, 해당 드로우콜이 어떤 GameObject / Material / Shader에서 나온 것인지 자동으로 역추적해주는 에디터 툴입니다.

## 요구 사항

| 항목 | 버전 |
|------|------|
| Unity | 6000.0.64f1 (Unity 6) |
| Render Pipeline | Built-in RP |
| RenderDoc | 1.x (선택 — 없어도 스냅샷 기능 사용 가능) |
| Python | 3.x (Phase 4 Auto-Match 사용 시) |

## 설치

1. 이 저장소를 Unity 프로젝트의 `Assets/Editor/UGDB/` 경로에 배치합니다.
2. Unity Editor를 열면 자동으로 컴파일됩니다.
3. 메뉴 `Window > UGDB Lookup` 으로 툴 윈도우를 엽니다.

## 기능 개요

### Phase 1 — Scene Snapshot + Lookup Engine

Play 모드에서 씬의 렌더링 상태를 스냅샷으로 수집합니다.

- **SceneSnapshot**: 카메라 프러스텀 기반 Renderer/Material/Shader/Texture/Light/GlobalState 수집
- **VariantTracker**: 셰이더 키워드 조합으로 variant 식별, 텍스처 슬롯 real/dummy 판별
- **LookupEngine**: 텍스처 해상도, 지오메트리(vtx/idx), 셰이더, 스칼라, variant 패턴 기반 다중 인덱스 검색
- **DrawCallMatcher**: 가중치 스코어링으로 복합 매칭 (Geometry 30%, Texture 25%, Scalar 20%, Variant 15%, Shader 10%)
- **SnapshotStore**: JSON 직렬화/역직렬화

### Phase 2 — Clipboard Parser + Editor UI

RenderDoc에서 복사한 텍스트를 자동 파싱합니다.

- **ClipboardParser**: RenderDoc 패턴 A~K 자동 인식 (텍스처 정보, DrawIndexed, CB 값, 셰이더 키워드 등)
- **UGDBWindow**: 에디터 윈도우 (자동 검색 / 수동 검색 / Auto-Match 탭)
- **PasteArea**: 텍스트 붙여넣기 → 자동 파싱 → 검색 트리거
- **ResultsPanel**: 매칭 결과 카드 (Ping Asset / Select GO / Copy Path 버튼)
- **ManualSearchPanel**: 텍스처/지오메트리/셰이더/CB 수동 검색

### Phase 3 — RenderDoc 동시 캡처

Unity Snapshot과 RenderDoc 캡처를 동시에 수행합니다.

- **RenderDocBridge**: 리플렉션 기반 RenderDoc 캡처 트리거
- **CaptureCoordinator**: Snap 버튼 → snapshot.json + .rdc + metadata.json 동시 저장
- **SessionManager**: 세션 폴더 관리 (생성/목록/삭제)

### Phase 4 — pyrenderdoc 자동 매칭

.rdc 캡처 파일의 전체 드로우콜을 자동으로 Unity 오브젝트에 매칭합니다.

- **extract_rdc_bindings.py**: pyrenderdoc API로 .rdc에서 전체 드로우콜 바인딩 추출 → JSON 출력
- **AutoMatcher**: JSON 파싱 → DrawCallMatcher로 전체 드로우콜 자동 매칭
- **AutoMatchPanel**: 드로우콜 목록 UI (필터/정렬/상세 보기)

## 사용법

### 기본 워크플로우 (수동 검색)

1. Unity에서 Play 모드 진입
2. `Window > UGDB Lookup` 윈도우 열기
3. **Snap** 버튼 클릭 → 씬 스냅샷 수집
4. RenderDoc에서 관심 있는 드로우콜의 텍스처/CB/지오메트리 정보 복사
5. UGDB 윈도우의 **붙여넣기 영역**에 Ctrl+V
6. 자동 파싱 → 매칭 결과 카드에서 해당 GameObject 확인

### Auto-Match 워크플로우

1. RenderDoc 연동 상태에서 Play 모드 진입
2. **Snap** 버튼 → snapshot.json + .rdc 동시 캡처
3. **Auto-Match** 탭에서 실행 버튼 클릭
4. Python 스크립트가 .rdc를 파싱하고 전체 드로우콜을 자동 매칭
5. 결과 목록에서 confidence 기준으로 필터/정렬하여 확인

## 프로젝트 구조

```
Assets/Editor/UGDB/
├── Core/
│   ├── AutoMatcher.cs         # .rdc JSON 자동 매칭
│   ├── DrawCallMatcher.cs     # 가중치 복합 매칭 스코어링
│   ├── LookupEngine.cs        # 다중 인덱스 검색 엔진
│   ├── SceneSnapshot.cs       # 씬 렌더링 상태 수집
│   ├── SnapshotData.cs        # 데이터 클래스 정의
│   ├── SnapshotStore.cs       # JSON 직렬화/역직렬화
│   └── VariantTracker.cs      # 셰이더 variant 추적
├── Parser/
│   ├── ClipboardParser.cs     # RenderDoc 텍스트 파싱
│   └── ClipboardPatterns.cs   # 정규식 패턴 정의
├── RenderDoc/
│   ├── CaptureCoordinator.cs  # 동시 캡처 코디네이터
│   ├── RenderDocBridge.cs     # RenderDoc 연동 브릿지
│   └── SessionManager.cs     # 세션 폴더 관리
├── UI/
│   ├── AutoMatchPanel.cs      # Auto-Match 결과 UI
│   ├── ManualSearchPanel.cs   # 수동 검색 UI
│   ├── PasteArea.cs           # 붙여넣기 영역
│   ├── ResultsPanel.cs        # 결과 카드 렌더링
│   └── UGDBWindow.cs          # 메인 에디터 윈도우
├── Tests/
│   ├── ClipboardParserTests.cs
│   └── RenderDocSamples/
└── Python/
    └── extract_rdc_bindings.py
```

## 신뢰도 등급

| 등급 | 점수 범위 | 의미 |
|------|-----------|------|
| HIGH | 90~100 | 높은 확률로 정확한 매칭 |
| MEDIUM | 70~89 | 후보군 — 추가 확인 권장 |
| LOW | 50~69 | 참고용 — 수동 검증 필요 |

## 라이선스

내부 프로젝트 전용
