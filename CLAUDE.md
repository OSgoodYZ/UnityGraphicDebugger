# UGDB — Unity Graphics Debug Bridge

## Project Overview
- Unity 6 (6000.0.64f1), Built-in RP, Editor-only 에디터 툴
- RenderDoc에서 복사한 텍스트를 자동 파싱하여 Unity 에셋을 역추적하는 룩업 툴
- 계획서: `UnityGraphicsDebugBridge Plan v3.md` (타겟 환경은 2021.3+로 기술되어 있으나 실제 Unity 6000.0.64f1 기준으로 개발)
- TODO: `docs/TODO.md`

## Architecture
```
Assets/Editor/UGDB/
├── Core/      (SceneSnapshot, VariantTracker, LookupEngine, DrawCallMatcher, SnapshotData, SnapshotStore)
├── Parser/    (ClipboardParser, ClipboardPatterns)
├── RenderDoc/ (RenderDocBridge, CaptureCoordinator, SessionManager)
├── UI/        (UGDBWindow, PasteArea, ResultsPanel, ManualSearchPanel, AutoMatchPanel)
├── Tests/     (ClipboardParserTests, RenderDocSamples/)
└── Python/    (extract_rdc_bindings.py)
```

## Coding Conventions
- 네임스페이스: `GFTeam.UGDB` (하위: `.Core`, `.Parser`, `.RenderDoc`, `.UI`)
- Assembly Definition: `UGDB.Editor.asmdef` (Editor Only)
- `[Serializable]` 필수, `JsonUtility` 호환
- Unity 오브젝트 null 체크: `obj != null` 사용 (`?.` 금지 — Unity fake null 이슈)
- 로깅: `Debug.Log("[UGDB] ...")` 프리픽스 통일
- UI: `EditorGUILayout` 기반 (UI Toolkit 아님)
- 테스트: `Assets/Editor/UGDB/Tests/` 에 EditMode 테스트

## TODO Workflow
"TODO P1-03 해줘" 형태로 지시 시:
1. `docs/TODO.md`에서 해당 항목을 읽는다
2. **의존** 필드의 선행 파일이 존재하는지 확인한다
3. **설명** 필드와 필요 시 계획서 해당 절을 참조하여 구현한다
4. **검증** 필드의 기준을 만족하는지 확인한다
5. 완료 후 **상태**를 `[x]`로 업데이트한다

## Key Design Decisions
- Play 모드에서만 Snapshot 수집 (런타임 데이터 기반)
- 텍스처 real/dummy 판별: width/height <= 4 이면 dummy
- float 비교: epsilon = 0.001f
- DrawCallMatcher 가중치: Geometry 0.30, Texture 0.25, Scalar 0.20, Variant 0.15, Shader 0.10
- 신뢰도 등급: 90~100 HIGH, 70~89 MEDIUM, 50~69 LOW

## Skills

커스텀 검증 및 유지보수 스킬은 `.claude/skills/`에 정의되어 있습니다.

| Skill | Purpose |
|-------|---------|
| `verify-implementation` | 프로젝트의 모든 verify 스킬을 순차 실행하여 통합 검증 보고서를 생성합니다 |
| `manage-skills` | 세션 변경사항을 분석하고, 검증 스킬을 생성/업데이트하며, CLAUDE.md를 관리합니다 |
