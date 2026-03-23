using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UGDB.Core;

namespace UGDB.RenderDoc
{
    /// <summary>
    /// Snap + RenderDoc 캡처 동기화 코디네이터.
    /// 계획서 3-1절: 같은 프레임에서 RenderDoc 캡처와 SceneSnapshot을 수행한다.
    /// </summary>
    public static class CaptureCoordinator
    {
        /// <summary>
        /// 캡처 결과 데이터.
        /// </summary>
        public class CaptureResult
        {
            public SceneSnapshotData snapshotData;
            public string sessionDir;
            public bool rdcCaptured;
            public string rdcPath;
            public bool success;
            public string errorMessage;
        }

        /// <summary>
        /// 프레임 캡처를 수행한다.
        /// 1) RenderDoc 캡처 트리거 (가능한 경우)
        /// 2) SceneSnapshot 수집
        /// 3) 세션 폴더에 snapshot.json + metadata.json + capture.rdc 저장
        /// </summary>
        public static CaptureResult CaptureFrame()
        {
            var result = new CaptureResult();

            // Play 모드 체크
            if (!Application.isPlaying)
            {
                result.success = false;
                result.errorMessage = "Play 모드에서만 Snap을 실행할 수 있습니다.";
                Debug.LogWarning($"[UGDB] {result.errorMessage}");
                return result;
            }

            try
            {
                // 1) RenderDoc 캡처 트리거 (선택적)
                bool rdcAvailable = RenderDocBridge.IsAvailable();
                if (rdcAvailable)
                {
                    result.rdcCaptured = RenderDocBridge.TriggerCapture();
                }
                else
                {
                    Debug.Log("[UGDB] RenderDoc 미연동 — 스냅샷만 수행합니다.");
                    result.rdcCaptured = false;
                }

                // 2) SceneSnapshot 수집
                result.snapshotData = SceneSnapshot.Capture();
                if (result.snapshotData == null)
                {
                    result.success = false;
                    result.errorMessage = "SceneSnapshot 수집 실패";
                    Debug.LogError($"[UGDB] {result.errorMessage}");
                    return result;
                }

                // 3) 세션 폴더 생성 + 저장
                result.sessionDir = SessionManager.CreateSession();
                SnapshotStore.Save(result.snapshotData, result.sessionDir);

                // 4) metadata.json에 RenderDoc 연동 정보 추가
                SaveCaptureMetadata(result);

                // 5) .rdc 파일 복사 (캡처 성공 시)
                if (result.rdcCaptured)
                {
                    // RenderDoc이 파일을 쓰는 데 약간의 지연이 있을 수 있음
                    var rdcPath = RenderDocBridge.GetLatestCapturePath();
                    if (!string.IsNullOrEmpty(rdcPath))
                    {
                        if (RenderDocBridge.CopyRdcToSession(rdcPath, result.sessionDir))
                        {
                            result.rdcPath = Path.Combine(result.sessionDir, "capture.rdc");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[UGDB] RenderDoc 캡처 파일을 찾을 수 없습니다. .rdc 없이 세션을 저장합니다.");
                        result.rdcCaptured = false;
                    }
                }

                result.success = true;

                Debug.Log(string.Format("[UGDB] Snap 완료: {0} renderers, {1} textures, RenderDoc={2}, 세션={3}",
                    result.snapshotData.statistics.totalRenderers,
                    result.snapshotData.statistics.totalTextures,
                    result.rdcCaptured ? "캡처됨" : "없음",
                    Path.GetFileName(result.sessionDir)));
            }
            catch (Exception e)
            {
                result.success = false;
                result.errorMessage = e.Message;
                Debug.LogError($"[UGDB] CaptureFrame 실패: {e}");
            }

            return result;
        }

        /// <summary>
        /// RenderDoc 없이 스냅샷만 수행한다.
        /// Editor 모드에서도 저장된 세션을 기반으로 작업할 수 있도록 지원.
        /// </summary>
        public static CaptureResult CaptureSnapshotOnly()
        {
            var result = new CaptureResult();

            if (!Application.isPlaying)
            {
                result.success = false;
                result.errorMessage = "Play 모드에서만 Snap을 실행할 수 있습니다.";
                Debug.LogWarning($"[UGDB] {result.errorMessage}");
                return result;
            }

            try
            {
                result.snapshotData = SceneSnapshot.Capture();
                if (result.snapshotData == null)
                {
                    result.success = false;
                    result.errorMessage = "SceneSnapshot 수집 실패";
                    return result;
                }

                result.sessionDir = SessionManager.CreateSession();
                SnapshotStore.Save(result.snapshotData, result.sessionDir);
                SaveCaptureMetadata(result);

                result.rdcCaptured = false;
                result.success = true;

                Debug.Log(string.Format("[UGDB] Snap(스냅샷만) 완료: {0} renderers, 세션={1}",
                    result.snapshotData.statistics.totalRenderers,
                    Path.GetFileName(result.sessionDir)));
            }
            catch (Exception e)
            {
                result.success = false;
                result.errorMessage = e.Message;
                Debug.LogError($"[UGDB] CaptureSnapshotOnly 실패: {e}");
            }

            return result;
        }

        private static void SaveCaptureMetadata(CaptureResult result)
        {
            var metadata = new CaptureMetadata
            {
                captureTime = result.snapshotData.timestamp,
                unityVersion = Application.unityVersion,
                graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                rendererCount = result.snapshotData.statistics.totalRenderers,
                materialCount = result.snapshotData.statistics.totalMaterials,
                textureCount = result.snapshotData.statistics.totalTextures,
                textureMemoryMB = result.snapshotData.statistics.totalTextureMemoryBytes / (1024f * 1024f),
                renderDocAvailable = RenderDocBridge.IsAvailable(),
                rdcCaptured = result.rdcCaptured
            };

            var metadataPath = Path.Combine(result.sessionDir, "metadata.json");
            File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));
        }

        [Serializable]
        public class CaptureMetadata
        {
            public string captureTime;
            public string unityVersion;
            public string graphicsAPI;
            public int screenWidth;
            public int screenHeight;
            public int rendererCount;
            public int materialCount;
            public int textureCount;
            public float textureMemoryMB;
            public bool renderDocAvailable;
            public bool rdcCaptured;
        }
    }
}
