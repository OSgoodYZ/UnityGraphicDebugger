using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UGDB.RenderDoc
{
    /// <summary>
    /// RenderDoc 연동 브릿지.
    /// 리플렉션으로 UnityEditorInternal.RenderDoc에 접근하여 캡처를 트리거한다.
    /// </summary>
    public static class RenderDocBridge
    {
        private static Type s_RenderDocType;
        private static bool s_TypeResolved;

        /// <summary>
        /// UnityEditorInternal.RenderDoc 타입을 리플렉션으로 가져온다.
        /// </summary>
        private static Type RenderDocType
        {
            get
            {
                if (!s_TypeResolved)
                {
                    s_TypeResolved = true;
                    s_RenderDocType = typeof(Editor).Assembly.GetType("UnityEditorInternal.RenderDoc");
                }
                return s_RenderDocType;
            }
        }

        /// <summary>
        /// RenderDoc이 로드되어 사용 가능한지 확인한다.
        /// </summary>
        public static bool IsAvailable()
        {
            if (RenderDocType == null)
                return false;

            try
            {
                var isLoaded = RenderDocType.GetMethod("IsLoaded",
                    BindingFlags.Public | BindingFlags.Static);
                var isSupported = RenderDocType.GetMethod("IsSupported",
                    BindingFlags.Public | BindingFlags.Static);

                if (isLoaded == null || isSupported == null)
                    return false;

                return (bool)isLoaded.Invoke(null, null) && (bool)isSupported.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UGDB] RenderDoc 상태 확인 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// RenderDoc 캡처를 트리거한다.
        /// Game View 윈도우를 찾아 RenderDocCapture 커맨드 이벤트를 전송한다.
        /// </summary>
        public static bool TriggerCapture()
        {
            if (!IsAvailable())
            {
                Debug.LogWarning("[UGDB] RenderDoc이 연결되어 있지 않습니다. 캡처를 건너뜁니다.");
                return false;
            }

            try
            {
                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null)
                {
                    Debug.LogError("[UGDB] GameView 타입을 찾을 수 없습니다.");
                    return false;
                }

                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView == null)
                {
                    Debug.LogError("[UGDB] Game View 윈도우를 찾을 수 없습니다.");
                    return false;
                }

                gameView.SendEvent(EditorGUIUtility.CommandEvent("RenderDocCapture"));
                Debug.Log("[UGDB] RenderDoc 캡처 트리거됨");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UGDB] RenderDoc 캡처 트리거 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// RenderDoc이 생성한 가장 최근 .rdc 파일의 경로를 반환한다.
        /// </summary>
        public static string GetLatestCapturePath()
        {
            try
            {
                var getLastCapturePathMethod = RenderDocType != null
                    ? RenderDocType.GetMethod("GetLastCaptureFilePath",
                        BindingFlags.Public | BindingFlags.Static)
                    : null;

                if (getLastCapturePathMethod != null)
                {
                    var path = getLastCapturePathMethod.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return path;
                }
            }
            catch (Exception)
            {
                // 리플렉션 실패 시 폴백
            }

            // 폴백: 임시 폴더에서 가장 최근 .rdc 파일 검색
            return FindLatestRdcInTempFolder();
        }

        /// <summary>
        /// 임시 폴더에서 가장 최근에 생성된 .rdc 파일을 검색한다.
        /// </summary>
        private static string FindLatestRdcInTempFolder()
        {
            var tempPath = Path.GetTempPath();
            string latestFile = null;
            DateTime latestTime = DateTime.MinValue;

            try
            {
                var rdcFiles = Directory.GetFiles(tempPath, "*.rdc", SearchOption.TopDirectoryOnly);
                foreach (var file in rdcFiles)
                {
                    var writeTime = File.GetLastWriteTime(file);
                    if (writeTime > latestTime)
                    {
                        latestTime = writeTime;
                        latestFile = file;
                    }
                }

                // RenderDoc 기본 캡처 폴더도 확인
                var rdocCapturePath = Path.Combine(tempPath, "RenderDoc");
                if (Directory.Exists(rdocCapturePath))
                {
                    var rdcInRdoc = Directory.GetFiles(rdocCapturePath, "*.rdc", SearchOption.AllDirectories);
                    foreach (var file in rdcInRdoc)
                    {
                        var writeTime = File.GetLastWriteTime(file);
                        if (writeTime > latestTime)
                        {
                            latestTime = writeTime;
                            latestFile = file;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UGDB] .rdc 파일 검색 실패: {e.Message}");
            }

            return latestFile;
        }

        /// <summary>
        /// .rdc 파일을 세션 폴더로 복사한다.
        /// </summary>
        public static bool CopyRdcToSession(string rdcSourcePath, string sessionDir)
        {
            if (string.IsNullOrEmpty(rdcSourcePath) || !File.Exists(rdcSourcePath))
                return false;

            try
            {
                var destPath = Path.Combine(sessionDir, "capture.rdc");
                File.Copy(rdcSourcePath, destPath, true);
                Debug.Log($"[UGDB] .rdc 파일 복사 완료: {destPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UGDB] .rdc 파일 복사 실패: {e.Message}");
                return false;
            }
        }
    }
}
