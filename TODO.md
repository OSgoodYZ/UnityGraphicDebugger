# Phase 1 — Scene Snapshot + Lookup Engine

> Play 모드에서 Snap -> 인덱싱 -> 검색으로 Unity 에셋 찾기

## Tasks

- [x] 1. `SnapshotData.cs` — 데이터 클래스 정의 (TextureSignature, TextureEntry, ScalarEntry, ShaderEntry, VariantEntry, MaterialEntry, RendererEntry 등)
- [x] 2. `SceneSnapshot.cs` — 씬 기반 풀체인 수집 (카메라/렌더러/라이트/머티리얼/텍스처/스칼라)
- [x] 3. `VariantTracker.cs` — Shader variant 추적 + real/dummy 텍스처 판별
- [x] 4. `LookupEngine.cs` — 5개 인덱스 빌드 + 개별 검색 (텍스처/셰이더/CB/지오메트리/Variant)
- [x] 5. `DrawCallMatcher.cs` — 복합 매칭 스코어링 (가중치 기반)
- [x] 6. `SnapshotStore.cs` — JSON 저장/로드

## File Structure

```
Assets/Editor/UGDB/Core/
├── SnapshotData.cs
├── SceneSnapshot.cs
├── VariantTracker.cs
├── LookupEngine.cs
├── DrawCallMatcher.cs
└── SnapshotStore.cs
```
