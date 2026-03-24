# UGDB 테스트 체크리스트

> TODO.md 기반으로 아직 완료되지 않은 통합 테스트 + 전체 기능 수동 검증 항목 정리
>
> 마지막 업데이트: 2026-03-24

## 상태 범례
- `[ ]` 미검증
- `[x]` 검증 완료
- `[!]` 실패 (사유 기록)

---

## 한 사이클 테스트 가이드 (Quick Cycle)

> 전체 파이프라인을 한 번 돌려보는 최소 경로입니다.
> 아래 순서대로 따라하면서 각 단계의 결과를 확인하세요.

### 사전 준비

- [x] **Unity 열기**: Unity 6000.0.64f1로 프로젝트 오픈 → 컴파일 에러 없음 확인
- [x] **테스트 씬 준비**: Renderer가 포함된 오브젝트 최소 3개 배치 (서로 다른 Material/Shader 사용 권장)
  - Cube (Standard 셰이더, 텍스처 할당, Metallic=0.73 등 스칼라 설정)
  - Sphere (Custom 셰이더 또는 다른 키워드 조합)
  - Plane (MaterialPropertyBlock으로 오버라이드 설정)
- [x] **RenderDoc 확인**: `Edit > Preferences > External Tools`에서 RenderDoc 연동 상태 확인

### Step 1: 컴파일 & 기본 동작

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q1 | `Window > UGDB Lookup` 메뉴 클릭 | UGDB 윈도우가 정상 표시됨 | [x] |
| Q2 | Editor 모드에서 Snap 버튼 클릭 | 경고 메시지 표시 (Play 모드 아님) | [x] |
| Q3 | 탭 전환 (자동검색 / 수동검색 / AutoMatch) | 각 탭이 에러 없이 전환됨 | [x] |

### Step 2: Play 모드 Snap

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q4 | Play 모드 진입 | - | [x] |
| Q5 | UGDB 윈도우에서 **Snap** 클릭 | 콘솔에 `[UGDB]` 로그 출력, 스냅샷 통계 표시 | [x] |
| Q6 | RenderDoc 연동 시: 세션 폴더 확인 | `UGDBCaptures/yyyyMMdd_HHmmss/` 폴더에 snapshot.json + capture.rdc + metadata.json 존재 | [x] |
| Q7 | 세션 드롭다운에 새 세션 표시 | 방금 생성한 세션이 목록에 보임 | [x] |

### Step 3: 클립보드 파싱 → 자동 검색

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q8 | RenderDoc에서 캡처 열기 → 텍스처 정보 복사 (예: `Texture2D 2048x2048 12 mips - BC7_UNORM`) | - |
| Q9 | UGDB 자동검색 탭에 텍스트 붙여넣기 | "감지됨" 라벨에 파싱 결과 표시 |
| Q10 | **검색** 버튼 클릭 | 결과 카드 1개 이상 표시, 신뢰도 점수 확인 |
| Q11 | 결과 카드에서 **Ping Asset** 클릭 | Project 윈도우에서 에셋 하이라이트 |
| Q12 | **Select GO** 클릭 | Hierarchy에서 해당 오브젝트 선택 |
| Q13 | **Copy Path** 클릭 | 클립보드에 경로가 복사됨 |

### Step 4: 수동 검색

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q14 | 수동검색 탭 전환 | 입력 필드 표시 |
| Q15 | Texture 타입 선택 → width=2048, height=2048 입력 → 검색 | 해당 해상도 텍스처를 사용하는 오브젝트 결과 표시 |
| Q16 | Geometry 타입 선택 → 테스트 메시의 indexCount 입력 → 검색 | 해당 메시를 사용하는 Renderer 결과 표시 |

### Step 5: Auto-Match (Phase 4, RenderDoc + Python 필요)

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q17 | AutoMatch 탭 전환 | Auto-Match 버튼 표시 |
| Q18 | **Auto-Match** 버튼 클릭 | Python 실행 → 드로우콜 매칭 결과 목록 표시 |
| Q19 | 필터 토글 (matched only / unmatched only / all) | 필터에 따라 목록 변경 |
| Q20 | 정렬 변경 (confidence 순 / 드로우콜 순서) | 정렬 순서 변경 |
| Q21 | 행 클릭 | 해당 드로우콜의 상세 카드 표시 |
| Q22 | HIGH confidence 항목 1개를 RenderDoc에서 수동 대조 | 실제 바인딩과 UGDB 매칭 결과 일치 |

### Step 6: 세션 관리 & 안정성

| # | 할 일 | 확인 포인트 |
|---|------|-----------|
| Q23 | Snap 한 번 더 실행 → 세션 2개 | 드롭다운에 세션 2개 표시 |
| Q24 | 다른 세션 선택 | 해당 세션의 스냅샷으로 검색 가능 |
| Q25 | 세션 삭제 | 목록에서 제거 + 폴더 삭제 확인 |
| Q26 | Play 모드 종료 | 세션/스냅샷 데이터 유지 |
| Q27 | 빈 텍스트 / 인식 불가 텍스트 붙여넣기 | 에러 없이 적절한 안내 메시지 |
| Q28 | 스냅샷 없이 검색 시도 | "먼저 Snap을 실행하세요" 안내 |

### 결과 기록

| 항목 | 값 |
|------|-----|
| 통과 항목 수 | /28 |
| 실패 항목 | |
| 발견된 버그 | |
| 비고 | |

---

## 1. Phase 1 통합 테스트 (TODO P1-14)

> 상태: **미착수** — Unity 에디터에서 직접 실행 필요

### 사전 조건
- [ ] Unity 6000.0.64f1에서 프로젝트 열기
- [ ] 컴파일 에러 없음 확인
- [ ] 테스트용 씬에 Renderer가 포함된 오브젝트 1개 이상 배치

### 1-1. SceneSnapshot 수집

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 1 | Play 모드에서 `SceneSnapshot.Capture()` 호출 | `UGDB > Debug > Test Phase 1` 메뉴 또는 스크립트 직접 호출 | [ ] |
| 2 | `renderers.Count > 0` | 콘솔 로그에서 렌더러 수 확인 | [ ] |
| 3 | 비활성 Renderer 제외 | 비활성 오브젝트가 결과에 포함되지 않음 | [ ] |
| 4 | Editor 모드에서 호출 시 경고 로그 | Play 모드가 아닌 상태에서 호출 → `[UGDB]` 경고 출력 | [ ] |
| 5 | 텍스처 수집 (width/height/format) | 실제 텍스처가 할당된 Material에서 TextureEntry 값이 Inspector와 일치 | [ ] |
| 6 | 스칼라 수집 | `_Metallic`, `_Smoothness` 등 float 값이 Inspector 값과 동일 | [ ] |
| 7 | Light 수집 | 씬에 Directional Light가 있을 때 `lights.Count > 0` | [ ] |
| 8 | MaterialPropertyBlock 오버라이드 | MPB를 사용하는 오브젝트에서 오버라이드 프로퍼티 수집됨 | [ ] |

### 1-2. VariantTracker

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 9 | 다른 키워드 → 다른 variantKey | 같은 셰이더, 다른 키워드의 Material 2개 → variantKey가 서로 다름 | [ ] |
| 10 | IsRealTexture 판별 | 1x1 white 텍스처 → false, 실제 텍스처 → true | [ ] |
| 11 | 슬롯 패턴 빌드 | `_NORMALMAP` ON 시 NormalMap 슬롯이 `isRealTexture=true` | [ ] |

### 1-3. LookupEngine 검색

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 12 | 텍스처 해상도 검색 | 2048x2048 텍스처로 검색 시 해당 해상도만 반환 | [ ] |
| 13 | 지오메트리 검색 | indexCount로 검색 시 해당 메시의 Renderer만 반환 | [ ] |
| 14 | 셰이더 이름 검색 | `"Standard"` 검색 시 Standard 셰이더 사용 Material 반환 | [ ] |
| 15 | 스칼라 값 검색 | float 0.73 검색 시 `_Metallic=0.73` Material 반환 | [ ] |
| 16 | variant 패턴 검색 | `[real, real, dummy]` 패턴으로 검색 시 올바른 variant 매칭 | [ ] |

### 1-4. DrawCallMatcher 복합 매칭

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 17 | 전체 일치 → HIGH | vtx+idx+텍스처+CB 모두 일치하는 입력 → score >= 90 | [ ] |
| 18 | 부분 일치 → LOW | 텍스처만 일치하는 입력 → score < 70 | [ ] |

### 1-5. SnapshotStore 직렬화

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 19 | Save → Load 왕복 | 저장 후 로드한 데이터와 원본 비교 (renderers.Count, gameObjectName 등) | [ ] |
| 20 | 파일 생성 확인 | `Application.persistentDataPath/UGDB/` 에 JSON 파일 존재 | [ ] |

---

## 2. Phase 2 UI 검증

### 2-1. 클립보드 파서

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 21 | Unity Test Runner 실행 | `ClipboardParserTests` 전체 통과 | [ ] |
| 22 | 텍스처 패턴 파싱 | `"Texture2D 2048x2048 12 mips - BC7_UNORM"` → width=2048, format=BC7_UNORM | [ ] |
| 23 | DrawIndexed 파싱 | `"DrawIndexed(34560, 1, 0, 0, 0)"` → indexCount=34560 | [ ] |
| 24 | CB float 파싱 | `"float 0.730"` → cbFloats에 0.73 | [ ] |
| 25 | CB hex 파싱 | `"3F800000"` → 1.0f | [ ] |
| 26 | 복합 QueryType | 텍스처+드로우콜 동시 포함 → QueryType=Composite | [ ] |

### 2-2. 에디터 윈도우

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 27 | 윈도우 열기 | `Window > UGDB Lookup` 메뉴 → 윈도우 정상 표시 | [ ] |
| 28 | Snap 버튼 (Play 모드) | Play 모드에서 Snap 클릭 → 스냅샷 수집 + 인덱스 빌드 | [ ] |
| 29 | Snap 버튼 (Editor 모드) | Editor 모드에서 Snap 클릭 → 경고 표시 | [ ] |
| 30 | 텍스트 붙여넣기 → 파싱 | 텍스트 입력 후 "감지됨" 라벨 갱신 | [ ] |
| 31 | 검색 → 결과 표시 | 검색 버튼 클릭 → ResultsPanel에 카드 표시 | [ ] |
| 32 | Ping Asset 버튼 | 결과 카드에서 Ping 클릭 → Project 윈도우에서 에셋 하이라이트 | [ ] |
| 33 | Select GO 버튼 | 결과 카드에서 Select 클릭 → Hierarchy에서 오브젝트 선택 | [ ] |
| 34 | Copy Path 버튼 | 클릭 → 클립보드에 경로 복사 | [ ] |
| 35 | 수동 검색 탭 | ManualSearch 탭에서 width/height 입력 → 검색 → 결과 표시 | [ ] |
| 36 | 히스토리 기능 | 여러 번 검색 후 뒤로가기 → 이전 결과 표시 | [ ] |
| 37 | 스냅샷 없이 검색 | 스냅샷 미수집 상태에서 검색 → "먼저 Snap을 실행하세요" 안내 | [ ] |
| 38 | 탭 전환 | 자동검색 / 수동검색 / AutoMatch 탭 전환 정상 동작 | [ ] |

---

## 3. Phase 3 통합 테스트 (TODO P3-05)

> 상태: **미착수** — RenderDoc 연동 필수

### 사전 조건
- [ ] RenderDoc가 설치되어 있고 Unity에 연동됨 (`Edit > Preferences > External Tools` 확인)
- [ ] Play 모드 상태

### 3-1. RenderDoc 연동

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 39 | RenderDoc 감지 | `RenderDocBridge.IsAvailable()` → true | [ ] |
| 40 | RenderDoc 미연동 시 | RenderDoc 없이 실행 → IsAvailable() false + 경고 로그 | [ ] |

### 3-2. 동시 캡처 E2E

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 41 | Snap → 세션 폴더 생성 | `UGDBCaptures/yyyyMMdd_HHmmss/` 폴더 생성 확인 | [ ] |
| 42 | snapshot.json 생성 | 세션 폴더에 snapshot.json 파일 존재 | [ ] |
| 43 | capture.rdc 생성 | 세션 폴더에 .rdc 파일 존재 (RenderDoc 연동 시) | [ ] |
| 44 | metadata.json 생성 | 세션 폴더에 metadata.json 존재 (Unity 버전, 해상도, API 등) | [ ] |

### 3-3. 세션 관리

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 45 | 세션 목록 표시 | Snap 후 드롭다운에 새 세션 표시 | [ ] |
| 46 | 세션 전환 | 드롭다운에서 다른 세션 선택 → 해당 스냅샷으로 검색 가능 | [ ] |
| 47 | 세션 삭제 | 세션 삭제 → 목록에서 제거 + 폴더 삭제 | [ ] |

### 3-4. RenderDoc 복사 → UGDB 검색 E2E

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 48 | 텍스처 정보 복사 → 검색 | RenderDoc에서 텍스처 정보 복사 → UGDB 붙여넣기 → 올바른 오브젝트 매칭 | [ ] |
| 49 | 드로우콜 정보 복사 → 검색 | DrawIndexed 정보 복사 → 매칭 결과 확인 | [ ] |
| 50 | CB 값 복사 → 검색 | Constant Buffer 값 복사 → 매칭 결과 확인 | [ ] |

---

## 4. Phase 4 통합 테스트 (TODO P4-05)

> 상태: **미착수** — Python + pyrenderdoc 환경 필수

### 사전 조건
- [ ] Python 3.x 설치 확인
- [ ] renderdoccmd가 PATH에 있거나 EditorPrefs에 경로 설정
- [ ] Phase 3 Snap으로 .rdc 파일이 존재

### 4-1. Python 스크립트

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 51 | 스크립트 실행 | `renderdoccmd python extract_rdc_bindings.py <input.rdc> <output.json>` | [ ] |
| 52 | JSON 출력 유효성 | 출력 JSON에 drawCalls 배열 존재, 각 항목에 eventId/textures/cbFloats 포함 | [ ] |

### 4-2. Auto-Match E2E

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 53 | Auto-Match 버튼 실행 | UGDB 윈도우 Auto-Match 탭 → 버튼 클릭 → Python 실행 → 결과 표시 | [ ] |
| 54 | 매칭률 확인 | 전체 드로우콜 중 매칭 성공률 50% 이상 | [ ] |
| 55 | HIGH confidence 정확도 | HIGH 등급 항목이 RenderDoc에서 수동 대조 시 실제와 일치 | [ ] |
| 56 | 필터 기능 | matched only / unmatched only / all 필터 동작 | [ ] |
| 57 | 정렬 기능 | confidence 순 / 드로우콜 순서 정렬 동작 | [ ] |
| 58 | 행 클릭 → 상세 보기 | 드로우콜 행 클릭 → ResultsPanel에서 상세 카드 표시 | [ ] |

---

## 5. 엣지 케이스 / 안정성

| # | 테스트 항목 | 검증 방법 | 결과 |
|---|-----------|----------|------|
| 59 | 빈 씬에서 Snap | Renderer가 없는 빈 씬에서 Snap → 에러 없이 빈 결과 | [ ] |
| 60 | 빈 텍스트 붙여넣기 | 빈 문자열 입력 → 에러 없이 무시 | [ ] |
| 61 | 인식 불가 텍스트 | 임의 문자열 입력 → "인식된 패턴 없음" 안내 | [ ] |
| 62 | 대량 렌더러 씬 | Renderer 100개 이상 씬에서 Snap 속도 + 검색 정상 동작 | [ ] |
| 63 | 다중 카메라 | 카메라 2개 이상 → 모든 카메라 프러스텀의 Renderer 수집 | [ ] |
| 64 | 스냅샷 JSON 대용량 | 큰 스냅샷의 Save/Load 정상 동작 | [ ] |
| 65 | Play ↔ Edit 전환 | Play 모드 종료 후에도 세션/스냅샷 데이터 유지 | [ ] |

---

## 테스트 환경 메모

| 항목 | 값 |
|------|-----|
| Unity 버전 | |
| RenderDoc 버전 | |
| OS | |
| Graphics API | |
| GPU | |
| 테스트 날짜 | |
| 테스터 | |
