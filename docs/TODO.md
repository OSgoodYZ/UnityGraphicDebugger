# UGDB TODO

> 계획서: `UnityGraphicsDebugBridge Plan v3.md`
> Unity: 6000.0.64f1 (Unity 6), Built-in RP
> 마지막 업데이트: 2026-03-23

## 상태 범례
- `[ ]` 미착수
- `[~]` 진행 중
- `[x]` 완료
- `[!]` 블록됨 (사유 명시)

---

## Phase 1 — Scene Snapshot + Lookup Engine

### P1-01: 프로젝트 스캐폴딩
- **파일**: `Assets/Editor/UGDB/` 폴더 구조 전체 + `UGDB.Editor.asmdef`
- **설명**: Editor 폴더 구조 생성 (Core/, Parser/, RenderDoc/, UI/, Tests/, Python/), Assembly Definition 파일 생성 (`UGDB.Editor.asmdef` — Editor Only 플랫폼), 네임스페이스 `GFTeam.UGDB` 설정. 각 하위 폴더에 빈 `.gitkeep` 또는 placeholder 불필요 — asmdef만 있으면 됨.
- **의존**: 없음
- **검증**: Unity Editor에서 컴파일 에러 없음, asmdef가 Editor Only로 설정됨, 폴더 구조가 Architecture와 일치
- **상태**: [~] (폴더 구조 완료, asmdef 누락)

---

### P1-02: SnapshotData — 데이터 클래스 정의
- **파일**: `Assets/Editor/UGDB/Core/SnapshotData.cs`
- **설명**: 계획서 1-1절의 수집 데이터 구조를 C# 클래스로 정의. `SnapshotResult` (최상위), `RendererEntry`, `MaterialEntry`, `ShaderEntry`, `ShaderPropertyEntry`, `VariantEntry`, `SlotState`, `TextureEntry`, `ScalarEntry`, `LightEntry`, `GlobalStateEntry`. 모든 Entry에 `[Serializable]` 어트리뷰트. `TextureSignature` 구조체 (width, height, format string, mipCount) — `IEquatable<TextureSignature>` + `GetHashCode` 구현.
- **의존**: P1-01
- **검증**: 컴파일 통과, 모든 Entry가 `[Serializable]`, `TextureSignature`가 `IEquatable<T>` 구현, `SnapshotResult`에 renderers/materials/shaders/textures/lights/globalState 리스트 존재
- **상태**: [x]

---

### P1-03: SceneSnapshot — 기본 수집 (Renderer + Material + Shader)
- **파일**: `Assets/Editor/UGDB/Core/SceneSnapshot.cs`
- **설명**: 계획서 1-1절 수집 흐름 구현. `public static SnapshotResult Capture()` 메서드. 1) `SceneManager`로 로드된 씬 목록 2) `Camera.allCameras`로 활성 카메라 목록 3) 각 카메라의 프러스텀 컬링으로 보이는 Renderer 목록 (`GeometryUtility.CalculateFrustumPlanes` + `TestPlanesAABB`) 4) 각 Renderer의 Material → Shader 체인 수집. Renderer별: GameObject 이름, hierarchy 경로 (`GetFullPath` 유틸), 소속 씬, Mesh vtx/idx/subMeshCount. Material별: 이름, instanceId, 인스턴스 여부, renderQueue. Shader별: 이름, 에셋 경로 (`AssetDatabase.GetAssetPath`), passCount, 프로퍼티 목록 (`ShaderUtil.GetPropertyCount/Name/Type`). Play 모드가 아니면 경고 로그 + 빈 결과 반환.
- **의존**: P1-02
- **검증**: Play 모드에서 `Capture()` 호출 시 null 없이 데이터 반환, Renderer가 1개 이상인 씬에서 `renderers.Count > 0`, 비활성 Renderer 제외됨, Editor 모드에서 호출 시 경고 로그 출력
- **상태**: [x]

---

### P1-04: SceneSnapshot — 텍스처 + 스칼라 수집
- **파일**: `Assets/Editor/UGDB/Core/SceneSnapshot.cs` (확장)
- **설명**: P1-03의 Capture 메서드를 확장하여 각 Material의 텍스처/스칼라 프로퍼티를 수집. Shader 프로퍼티 목록을 순회하며: Texture 타입 → `mat.GetTexture()` 로 에셋 경로, 해상도, 포맷, 밉맵 수, filterMode, wrapMode, 메모리 크기, 타입(Texture2D/RT/Cubemap) 수집. Float/Range → `mat.GetFloat()`. Color → `mat.GetColor()`. Vector → `mat.GetVector()`. Int → `mat.GetInt()`. `_ST` 접미사 프로퍼티는 TextureOffset/Scale로 분류. `TextureSignature` 생성하여 `TextureEntry`에 포함.
- **의존**: P1-03
- **검증**: 텍스처가 할당된 Material에서 `TextureEntry`의 width/height/format이 실제 값과 일치, 스칼라 값이 Inspector에 표시되는 값과 동일
- **상태**: [x]

---

### P1-05: SceneSnapshot — Light + 글로벌 상태 수집
- **파일**: `Assets/Editor/UGDB/Core/SceneSnapshot.cs` (확장)
- **설명**: Light 수집: `FindObjectsByType<Light>()` 로 씬 내 활성 라이트 목록. 각 라이트의 타입, color, intensity, range, shadow 설정, shadowmap 해상도, cookie 텍스처. 글로벌 상태: `Shader.GetGlobalTexture()`로 `_CameraDepthTexture` 등 글로벌 텍스처, `RenderTexture.active` + 활성 RT 목록, `LightmapSettings.lightmaps`로 라이트맵 텍스처, `RenderSettings` (ambientMode, fog 등), `QualitySettings` (shadowResolution 등).
- **의존**: P1-03
- **검증**: 씬에 Directional Light가 있을 때 `lights.Count > 0`, 글로벌 텍스처 중 `_CameraDepthTexture`가 수집됨 (Play 모드, 카메라에 DepthTextureMode 설정 시)
- **상태**: [x]

---

### P1-06: SceneSnapshot — MaterialPropertyBlock 오버라이드 수집
- **파일**: `Assets/Editor/UGDB/Core/SceneSnapshot.cs` (확장)
- **설명**: 각 Renderer에서 `MaterialPropertyBlock`을 통해 오버라이드된 프로퍼티 수집. `renderer.GetPropertyBlock(mpb)` 후 `mpb.isEmpty`가 false이면 오버라이드 존재. Float/Color/Vector/Texture 프로퍼티를 순회하여 오버라이드 값을 `RendererEntry`에 저장. MPB 오버라이드는 Material 값보다 우선하므로 검색 시 MPB 값을 먼저 매칭.
- **의존**: P1-03
- **검증**: MPB를 사용하는 오브젝트가 있는 씬에서 오버라이드 프로퍼티가 수집됨, MPB가 없는 Renderer에서는 빈 리스트
- **상태**: [x]

---

### P1-07: VariantTracker — 키워드 조합 + real/dummy 판별
- **파일**: `Assets/Editor/UGDB/Core/VariantTracker.cs`
- **설명**: 계획서 1-3절 구현. 각 Material의 활성 키워드 조합으로 variant 식별 키 생성 (예: `"Custom/CharacterPBR|_EMISSION|_NORMALMAP"` — 키워드 알파벳 정렬). `IsRealTexture(Material, int propertyNameId)` 정적 메서드: tex == null → false, width/height <= 4 → false (Unity 기본 텍스처), 이름이 `"unity_default"`, `""`, `"UnityWhite"`, `"UnityBlack"`, `"UnityNormalMap"` → false, 그 외 → true.
- **의존**: P1-02
- **검증**: 동일 셰이더를 사용하되 키워드가 다른 두 Material이 서로 다른 variantKey를 가짐, `IsRealTexture`가 1x1 white 텍스처에 대해 false 반환
- **상태**: [x]

---

### P1-08: VariantTracker — 슬롯 패턴 빌드
- **파일**: `Assets/Editor/UGDB/Core/VariantTracker.cs` (확장)
- **설명**: 계획서 1-3절 `SlotState` + variant별 텍스처 슬롯 패턴 빌드. `BuildVariants(SnapshotResult)` 메서드: 모든 Material을 순회하며 variant 키 생성 → 같은 variant끼리 그룹핑 → 각 variant의 `textureSlotPattern` (슬롯별 real/dummy + TextureSignature) 빌드 → `scalarPattern` 빌드. 씬 전체에서 같은 셰이더의 variant 조합 목록을 수집하여 `VariantEntry.materials` 리스트에 해당 variant를 사용하는 Material들을 연결.
- **의존**: P1-07
- **검증**: `_NORMALMAP` 키워드가 켜진 Material의 variant에서 NormalMap 슬롯이 `isRealTexture=true`, 키워드가 꺼진 Material에서는 `isRealTexture=false`
- **상태**: [x]

---

### P1-09: LookupEngine — 텍스처 인덱스 + 지오메트리 인덱스
- **파일**: `Assets/Editor/UGDB/Core/LookupEngine.cs`
- **설명**: 계획서 1-2절의 인덱스 1, 4번 구현. `LookupEngine(SnapshotResult snapshot)` 생성자에서 인덱스 빌드. 텍스처 인덱스: `Dictionary<TextureSignature, List<TextureEntry>> textureIndex` — 해상도+포맷으로 검색. `SearchByTexture(int width, int height, string format = null)` 메서드. 지오메트리 인덱스: `Dictionary<(int vtx, int idx), List<RendererEntry>> geometryIndex` — vtx+idx로 검색. `SearchByGeometry(int? vertexCount, int? indexCount)` 메서드 (둘 중 하나만으로도 검색 가능).
- **의존**: P1-02, P1-04
- **검증**: 2048x2048 텍스처로 검색 시 해당 해상도의 텍스처만 반환, indexCount로 검색 시 해당 메시를 가진 Renderer만 반환
- **상태**: [x]

---

### P1-10: LookupEngine — 셰이더 인덱스 + 스칼라 인덱스
- **파일**: `Assets/Editor/UGDB/Core/LookupEngine.cs` (확장)
- **설명**: 계획서 1-2절의 인덱스 2, 3번 구현. 셰이더 인덱스: `Dictionary<string, ShaderEntry> shaderByName`, `Dictionary<string, List<ShaderEntry>> shaderByKeyword`. `SearchByShaderName(string name)`, `SearchByKeyword(string keyword)`. 스칼라 인덱스: `Dictionary<float, List<ScalarEntry>> scalarByValue` (epsilon 0.001 범위 검색), `Dictionary<Vector4, List<ScalarEntry>> colorByValue`. `SearchByFloat(float value, float epsilon = 0.001f)`, `SearchByColor(Vector4 color, float epsilon = 0.01f)`.
- **의존**: P1-02, P1-04
- **검증**: 셰이더 이름 `"Standard"`로 검색 시 Standard 셰이더 사용 Material 반환, float 값 0.73으로 검색 시 `_Metallic=0.73`인 Material 반환
- **상태**: [x]

---

### P1-11: LookupEngine — Variant 인덱스 + 슬롯패턴 검색
- **파일**: `Assets/Editor/UGDB/Core/LookupEngine.cs` (확장)
- **설명**: 계획서 1-2절의 인덱스 5번 구현. `Dictionary<string, VariantEntry> variantIndex` (variantKey → entry), `Dictionary<string, List<VariantEntry>> variantBySlotPattern` (슬롯 패턴 문자열 → variant 목록). 슬롯 패턴 키: real/dummy 시퀀스 + 해상도 (예: `"real(2048,ASTC)|real(2048,ASTC)|dummy"`). `SearchBySlotPattern(List<SlotState> pattern)` 메서드. `SearchByVariantKey(string variantKey)` 메서드.
- **의존**: P1-08
- **검증**: 텍스처 슬롯 패턴 `[real, real, dummy]`으로 검색 시 `_NORMALMAP` ON / `_EMISSION` OFF variant 매칭
- **상태**: [x]

---

### P1-12: DrawCallMatcher — 복합 매칭 스코어링
- **파일**: `Assets/Editor/UGDB/Core/DrawCallMatcher.cs`
- **설명**: 계획서 1-4절 구현. `DrawCallMatcher(LookupEngine engine)`. `Match(ParseResult query)` → `List<MatchResult>` (score 내림차순). 가중치: `WEIGHT_GEOMETRY=0.30`, `WEIGHT_TEXTURE=0.25`, `WEIGHT_SCALAR=0.20`, `WEIGHT_VARIANT=0.15`, `WEIGHT_SHADER=0.10`. `MatchResult`에 score(0~100), confidence 등급(HIGH/MEDIUM/LOW), 매칭된 RendererEntry + MaterialEntry + 각 카테고리별 부분 점수 포함. 각 카테고리의 부분 매칭: geometry → vtx+idx 완전 일치 시 1.0, texture → 일치 텍스처 수 / 전체 텍스처 수, scalar → epsilon 내 일치 값 수 / 전체 값 수, variant → 슬롯 패턴 완전 일치 시 1.0, shader → 키워드 일치 비율.
- **의존**: P1-09, P1-10, P1-11
- **검증**: vtx+idx+텍스처+CB값 모두 일치하는 입력에 대해 score >= 90 (HIGH), 텍스처만 일치하는 입력에 대해 score < 70
- **상태**: [x]

---

### P1-13: SnapshotStore — JSON 직렬화/역직렬화
- **파일**: `Assets/Editor/UGDB/Core/SnapshotStore.cs`
- **설명**: `SnapshotResult`를 JSON으로 저장/로드. `public static void Save(SnapshotResult data, string filePath)` — `JsonUtility.ToJson(data, prettyPrint: true)` + `File.WriteAllText`. `public static SnapshotResult Load(string filePath)` — `File.ReadAllText` + `JsonUtility.FromJson<SnapshotResult>`. 기본 저장 경로: `Application.persistentDataPath + "/UGDB/"`. 파일명 패턴: `snapshot_yyyyMMdd_HHmmss.json`.
- **의존**: P1-02
- **검증**: Save → Load 왕복 후 데이터 일치 (renderers.Count, 첫 번째 renderer의 gameObjectName 등), 파일이 실제 디스크에 생성됨
- **상태**: [x]

---

### P1-14: Phase 1 통합 테스트
- **파일**: `Assets/Editor/UGDB/Tests/Phase1Tests.cs`
- **설명**: Phase 1 전체 파이프라인 E2E 확인용 에디터 스크립트. `[MenuItem("UGDB/Debug/Test Phase 1")]` 메뉴 아이템. 1) `SceneSnapshot.Capture()` → 결과 로그 (renderer/material/texture/light 수), 2) `LookupEngine` 생성 → 텍스처/지오메트리/셰이더/스칼라 검색 테스트, 3) `DrawCallMatcher` → 샘플 입력으로 복합 매칭 테스트, 4) `SnapshotStore.Save/Load` 왕복 테스트. 모든 결과를 `Debug.Log("[UGDB Test] ...")` 로 콘솔에 출력.
- **의존**: P1-03 ~ P1-13
- **검증**: Unity 메뉴 `UGDB > Debug > Test Phase 1` 실행 시 콘솔에 에러 없이 테스트 결과 출력
- **상태**: [ ]

---

## Phase 2 — Clipboard Parser + UI

### P2-01: ClipboardPatterns — 정규식 정의
- **파일**: `Assets/Editor/UGDB/Parser/ClipboardPatterns.cs`
- **설명**: 계획서 2-1절의 패턴 A~L을 `static readonly Regex` 필드로 정의. 패턴 A: Resource 패널 (`Texture2D WxH N mips - FORMAT`), B: Pipeline State 탭 구분, C: 해상도만, D: DrawIndexed, E: API Inspector IndexCount, F: Draw(), G: CB 구조체 뷰, H: CB 오프셋 뷰, I: CB raw hex, J: 셰이더 키워드, K: 셰이더 이름. 각 Regex에 `RegexOptions.Compiled` 적용. 포맷 이름 매핑 테이블: `Dictionary<string, string> FormatAliases` (BC7_UNORM ↔ ASTC_6x6 등 DX11/Vulkan 간 대응).
- **의존**: P1-14 (Phase 1 완료 후)
- **검증**: 각 패턴에 대해 계획서의 예시 텍스트가 매칭됨, `FormatAliases`에 주요 포맷 쌍이 존재
- **상태**: [x]

---

### P2-02: ClipboardParser — 텍스처 패턴 파싱 (A, B, C)
- **파일**: `Assets/Editor/UGDB/Parser/ClipboardParser.cs`
- **설명**: 계획서 2-2절 기반. `ClipboardParser` 클래스 + `ParseResult` 클래스 + `QueryType` enum 정의. `public ParseResult Parse(string clipboardText)` 메인 메서드. 줄 단위 분리 후 `TryParseTexture(line, result)` 구현: 패턴 A → width, height, mipCount, format 추출하여 `TextureSignature` 생성 → `result.textures`에 추가. 패턴 B → slot index + 텍스처 정보 추출. 패턴 C → 해상도만 추출. 여러 줄이면 각 줄 파싱하여 복수 텍스처 수집.
- **의존**: P2-01
- **검증**: `"Texture2D 2048x2048 12 mips - BC7_UNORM"` 파싱 시 width=2048, height=2048, format="BC7_UNORM" 추출, QueryType이 Texture
- **상태**: [x]

---

### P2-03: ClipboardParser — 드로우콜 패턴 파싱 (D, E, F)
- **파일**: `Assets/Editor/UGDB/Parser/ClipboardParser.cs` (확장)
- **설명**: `TryParseDrawCall(line, result)` 구현. 패턴 D: `DrawIndexed(34560, 1, 0, 0, 0)` → indexCount=34560. 패턴 E: `IndexCount: 34560` → indexCount=34560. 패턴 F: `Draw(1200)` → vertexCount=1200. 추출 값을 `result.indexCount` 또는 `result.vertexCount`에 저장.
- **의존**: P2-02
- **검증**: `"DrawIndexed(34560, 1, 0, 0, 0)"` 파싱 시 indexCount=34560, QueryType이 Geometry
- **상태**: [x]

---

### P2-04: ClipboardParser — CB 패턴 파싱 (G, H, I)
- **파일**: `Assets/Editor/UGDB/Parser/ClipboardParser.cs` (확장)
- **설명**: `TryParseCBStruct(line, result)`: 패턴 G — `float4 1.000 0.950 0.900 1.000` → Vector4, `float 0.730` → float. `TryParseCBOffset(line, result)`: 패턴 H — `[0]: 1, 0.95, 0.9, 1` → offset=0, values. `TryParseCBHex(line, result)`: 패턴 I — hex → float 변환 (`BitConverter.Int32BitsToSingle`). 추출 값을 `result.cbFloats`, `result.cbVectors`, `result.cbOffsetValues`에 저장.
- **의존**: P2-03
- **검증**: `"float 0.730"` 파싱 시 cbFloats에 0.73 추가, `"[16]: 0.73"` 파싱 시 cbOffsetValues[16]=0.73, hex `"3F800000"` → 1.0f
- **상태**: [x]

---

### P2-05: ClipboardParser — 셰이더/복합 패턴 파싱 (J, K, L) + QueryType 결정
- **파일**: `Assets/Editor/UGDB/Parser/ClipboardParser.cs` (확장)
- **설명**: `TryParseKeywords(line, result)`: 패턴 J — `_NORMALMAP`, `_EMISSION` 등 대문자 키워드 추출 → `result.shaderKeywords`. `TryParseShaderName(line, result)`: 패턴 K — `Custom/CharacterPBR` 같은 슬래시 포함 경로 → `result.shaderName`. `DetermineQueryType(ParseResult)` 구현: 추출된 키 조합에 따라 Texture/Geometry/ConstantBuffer/Shader/Composite 자동 결정 (2개 이상 카테고리 → Composite).
- **의존**: P2-04
- **검증**: 텍스처+드로우콜 정보가 동시에 포함된 텍스트에서 QueryType이 Composite, 키워드 `_NORMALMAP`이 정상 추출
- **상태**: [x]

---

### P2-06: ClipboardParser 유닛 테스트
- **파일**: `Assets/Editor/UGDB/Tests/ClipboardParserTests.cs`
- **설명**: 계획서 2-3절의 모든 패턴(A~L)에 대한 유닛 테스트. 각 패턴별 최소 2개 입력 (정상 + 변형). 복합 입력 (여러 패턴 혼합) 테스트. QueryType 자동 결정 테스트. `Tests/RenderDocSamples/` 폴더에 실제 복사 텍스트 샘플 파일 포함 (계획서 예시 기반). NUnit `[Test]` 어트리뷰트 사용.
- **의존**: P2-05
- **검증**: Unity Test Runner에서 모든 테스트 통과
- **상태**: [x]

---

### P2-07: UGDBWindow + PasteArea
- **파일**: `Assets/Editor/UGDB/UI/UGDBWindow.cs`, `Assets/Editor/UGDB/UI/PasteArea.cs`
- **설명**: 계획서 2-4절 레이아웃 구현. `UGDBWindow : EditorWindow` — `[MenuItem("Window/UGDB Lookup")]`으로 메뉴 등록. 상단: 타이틀 + Snap 버튼 (Phase 3에서 연결, 지금은 placeholder) + 스냅샷 정보 라벨. 중앙: `PasteArea` — `EditorGUILayout.TextArea`로 붙여넣기 영역, Ctrl+V 감지 시 자동 파싱 트리거, "감지됨: ..." 라벨 표시, [검색] 버튼. 하단: 결과 영역 (P2-08에서 구현), 상태바 (스냅샷 통계). 히스토리 기능: 이전 검색 결과 목록 (List로 관리, 뒤로가기 버튼).
- **의존**: P1-14 (LookupEngine 참조)
- **검증**: `Window > UGDB Lookup` 메뉴로 윈도우 열림, 텍스트 붙여넣기 시 "감지됨" 라벨 갱신, 검색 버튼 클릭 가능
- **상태**: [x]

---

### P2-08: ResultsPanel — 결과 카드 렌더링
- **파일**: `Assets/Editor/UGDB/UI/ResultsPanel.cs`
- **설명**: 계획서 2-4절 결과 카드 UI. `ResultsPanel` 클래스 — `void Draw(List<MatchResult> results)`. 각 결과 카드: GameObject 이름 + hierarchy 경로, Mesh 정보 (vtx/idx), Material + Shader + Keywords, 텍스처 슬롯 목록 ([0] _MainTex → T_Hero_Body_D.png), CB Layout (추정 오프셋 + 프로퍼티 이름 + 값), 신뢰도 표시 (score + HIGH/MEDIUM/LOW). 액션 버튼: [Ping Asset] — `EditorGUIUtility.PingObject`, [Select GO] — `Selection.activeGameObject`, [Copy Path] — `EditorGUIUtility.systemCopyBuffer`.
- **의존**: P2-07
- **검증**: MatchResult 데이터가 있을 때 카드가 렌더링됨, Ping/Select/Copy 버튼 동작
- **상태**: [x]

---

### P2-09: ManualSearchPanel — 수동 검색 UI
- **파일**: `Assets/Editor/UGDB/UI/ManualSearchPanel.cs`
- **설명**: 파서가 인식 못한 경우를 위한 수동 입력 탭. 검색 타입 드롭다운 (Texture/Geometry/Shader/CB). 타입별 입력 필드: Texture → width, height, format 입력. Geometry → vtx, idx 입력. Shader → 이름 또는 키워드 입력. CB → float 값 입력. [검색] 버튼 → `LookupEngine`의 해당 메서드 직접 호출 → 결과를 `ResultsPanel`에 표시.
- **의존**: P2-08
- **검증**: 수동으로 width=2048, height=2048 입력 후 검색 시 결과 표시
- **상태**: [x]

---

### P2-10: UI 통합 연결 (붙여넣기 → 파싱 → 검색 → 결과)
- **파일**: `Assets/Editor/UGDB/UI/UGDBWindow.cs` (확장)
- **설명**: 전체 파이프라인 연결. PasteArea에서 텍스트 입력 → `ClipboardParser.Parse()` → `ParseResult` 기반으로 `LookupEngine` 또는 `DrawCallMatcher` 호출 → `MatchResult` 리스트를 `ResultsPanel`에 전달. 탭 전환: 자동 검색 / 수동 검색. 스냅샷 로드: `SnapshotStore.Load()` → `LookupEngine` 초기화. Snap 버튼: Play 모드에서 `SceneSnapshot.Capture()` → `SnapshotStore.Save()` → 인덱스 리빌드.
- **의존**: P2-06, P2-09
- **검증**: 텍스트 붙여넣기 → 자동 파싱 → 검색 → 결과 카드 표시까지 E2E 동작, 스냅샷 없는 상태에서 검색 시 "먼저 Snap을 실행하세요" 안내
- **상태**: [x]

---

## Phase 3 — RenderDoc 동시 캡처

### P3-01: RenderDocBridge — 캡처 트리거
- **파일**: `Assets/Editor/UGDB/RenderDoc/RenderDocBridge.cs`
- **설명**: 계획서 3-2절 구현. `public static bool IsAvailable()` — RenderDoc이 로드되었는지 확인 (리플렉션으로 `UnityEditorInternal.RenderDoc` 클래스 접근). `public static void TriggerCapture()` — Game View 윈도우에 `RenderDocCapture` 커맨드 이벤트 전송. `public static string GetLatestCapturePath()` — 가장 최근 .rdc 파일 경로 반환. RenderDoc 미설치 시 graceful 경고.
- **의존**: P2-10
- **검증**: RenderDoc 연동 상태에서 `TriggerCapture()` 호출 시 .rdc 파일 생성, 미연동 시 `IsAvailable()` false + 경고 로그
- **상태**: [x]

---

### P3-02: CaptureCoordinator — Snap + RenderDoc 동기화
- **파일**: `Assets/Editor/UGDB/RenderDoc/CaptureCoordinator.cs`
- **설명**: 계획서 3-1절 동시 캡처 플로우 구현. `public static IEnumerator CaptureFrame()` 코루틴: 1) `RenderDocBridge.TriggerCapture()`, 2) `WaitForEndOfFrame`, 3) `SceneSnapshot.Capture()`, 4) 세션 폴더 생성 (SessionManager에 위임), 5) snapshot.json + .rdc + metadata.json 저장. `metadata.json`: Unity 버전, 해상도, 그래픽스 API, 캡처 시각. Play 모드가 아니면 RenderDoc 캡처 스킵 (스냅샷만 수행).
- **의존**: P3-01
- **검증**: Snap 실행 시 세션 폴더에 snapshot.json + capture.rdc + metadata.json 3개 파일 생성
- **상태**: [x]

---

### P3-03: SessionManager — 세션 폴더 관리
- **파일**: `Assets/Editor/UGDB/RenderDoc/SessionManager.cs`
- **설명**: 세션 폴더 생성/목록/삭제 관리. 기본 경로: 프로젝트 루트 `UGDBCaptures/`. 폴더 명명: `yyyyMMdd_HHmmss/`. `public static string CreateSession()` → 새 세션 폴더 경로. `public static List<SessionInfo> GetSessions()` → 기존 세션 목록 (날짜, renderer 수, 텍스처 수 등 metadata 파싱). `public static void DeleteSession(string path)`. `SessionInfo` 데이터 클래스.
- **의존**: P3-02
- **검증**: 세션 생성 → 목록에 표시 → 삭제 → 목록에서 제거
- **상태**: [x]

---

### P3-04: UGDBWindow — Snap 버튼 + 세션 드롭다운 연동
- **파일**: `Assets/Editor/UGDB/UI/UGDBWindow.cs` (확장)
- **설명**: Snap 버튼을 실제 `CaptureCoordinator`에 연결. 상단에 세션 드롭다운: `SessionManager.GetSessions()` 목록 표시, 선택 시 해당 세션의 snapshot.json 로드 → LookupEngine 리빌드. RenderDoc 연동 상태 표시: 아이콘/라벨로 `RenderDocBridge.IsAvailable()` 결과 표시. 비연동 시 "RenderDoc 없이 Snap (스냅샷만)" 모드 지원.
- **의존**: P3-03
- **검증**: Snap 버튼 클릭 → 세션 생성 → 드롭다운에 새 세션 표시 → 선택 시 검색 가능
- **상태**: [x]

---

### P3-05: Phase 3 통합 테스트
- **파일**: 수동 테스트
- **설명**: RenderDoc 연동 + Snap + 검색 E2E 수동 테스트. 1) RenderDoc 연동 상태에서 Play 모드 진입, 2) UGDB 윈도우에서 Snap 클릭, 3) RenderDoc에서 캡처 열기 → 텍스처 정보 복사, 4) UGDB에 붙여넣기 → 결과 확인, 5) 세션 전환 테스트.
- **의존**: P3-04
- **검증**: 위 5단계가 에러 없이 수행됨
- **상태**: [ ]

---

## Phase 4 — pyrenderdoc 자동 매칭 (선택)

### P4-01: extract_rdc_bindings.py — pyrenderdoc 파싱 스크립트
- **파일**: `Assets/Editor/UGDB/Python/extract_rdc_bindings.py`
- **설명**: 계획서 4-1절. pyrenderdoc API로 .rdc 파일을 열어 전체 드로우콜 목록 추출. 각 드로우콜의 GPU 바인딩 정보: 텍스처 슬롯 (해상도, 포맷), CB 값, 셰이더 키워드, vtx/idx count. 결과를 JSON으로 출력 (stdout 또는 파일). CLI 인터페이스: `python extract_rdc_bindings.py <input.rdc> <output.json>`.
- **의존**: P3-05
- **검증**: 실제 .rdc 파일에 대해 실행 시 유효한 JSON 출력
- **상태**: [x]

---

### P4-02: AutoMatcher — .rdc JSON + 스냅샷 자동 매칭
- **파일**: `Assets/Editor/UGDB/Core/AutoMatcher.cs`
- **설명**: Python 스크립트의 JSON 출력을 파싱하여 각 드로우콜에 대해 `DrawCallMatcher`로 자동 매칭. `public static List<AutoMatchResult> Match(string rdcJsonPath, SnapshotResult snapshot)`. `AutoMatchResult`: drawCallIndex, drawCallName, MatchResult(from DrawCallMatcher), 매칭 여부.
- **의존**: P4-01
- **검증**: JSON 입력에 대해 매칭 결과 리스트 반환, 매칭 실패 항목은 unmatched 표시
- **상태**: [x]

---

### P4-03: AutoMatchPanel — 전체 드로우콜 매칭 결과 UI
- **파일**: `Assets/Editor/UGDB/UI/AutoMatchPanel.cs`
- **설명**: 전체 드로우콜 목록을 스크롤 뷰로 표시. 각 행: Draw #N → Unity 오브젝트/머티리얼 (confidence %). 필터: matched only / unmatched only / all. 정렬: confidence 순 / 드로우콜 순서. 행 클릭 시 해당 결과의 상세 카드 (ResultsPanel 재사용).
- **의존**: P4-02
- **검증**: AutoMatch 실행 후 드로우콜 목록이 표시됨, 필터/정렬 동작
- **상태**: [x]

---

### P4-04: UGDBWindow — Auto-Match 탭 연동
- **파일**: `Assets/Editor/UGDB/UI/UGDBWindow.cs` (확장)
- **설명**: UGDBWindow에 탭 추가: Lookup / Auto-Match. Auto-Match 탭: [Auto-Match] 버튼 → Python 스크립트 실행 → 결과를 AutoMatchPanel에 표시. Python 경로 설정 (EditorPrefs). 진행 상태 표시 (EditorUtility.DisplayProgressBar).
- **의존**: P4-03
- **검증**: Auto-Match 버튼 클릭 → Python 실행 → 결과 UI 표시
- **상태**: [x]

---

### P4-05: Phase 4 통합 테스트
- **파일**: 수동 테스트
- **설명**: 전체 자동 매칭 E2E 테스트. 1) Snap으로 snapshot + .rdc 생성, 2) Auto-Match 실행, 3) 결과에서 올바른 오브젝트 매칭 확인, 4) RenderDoc에서 같은 드로우콜 선택하여 대조.
- **의존**: P4-04
- **검증**: 매칭률 50% 이상, HIGH confidence 항목이 실제와 일치
- **상태**: [ ]

---

## 의존성 그래프

```
Phase 1:
P1-01 → P1-02 → P1-03 → P1-04
                       → P1-05
                       → P1-06
         P1-02 → P1-07 → P1-08
         P1-02 + P1-04 → P1-09
         P1-02 + P1-04 → P1-10
         P1-08 → P1-11
         P1-09 + P1-10 + P1-11 → P1-12
         P1-02 → P1-13
         P1-03~P1-13 → P1-14

Phase 2 (P1-14 완료 후):
P2-01 → P2-02 → P2-03 → P2-04 → P2-05 → P2-06
P2-07 → P2-08 → P2-09  (P2-07은 P1-14만 의존, 파서와 병렬 가능)
P2-06 + P2-09 → P2-10

Phase 3 (P2-10 완료 후):
P3-01 → P3-02 → P3-03 → P3-04 → P3-05

Phase 4 (P3-05 완료 후, 선택):
P4-01 → P4-02 → P4-03 → P4-04 → P4-05
```
