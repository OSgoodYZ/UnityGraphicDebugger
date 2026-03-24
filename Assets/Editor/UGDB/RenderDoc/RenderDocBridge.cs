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
        /// BeginCaptureRenderDoc/EndCaptureRenderDoc API를 리플렉션으로 호출한다.
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

                // Begin/End 캡처 API 사용 (Unity 6 호환)
                var beginCapture = RenderDocType.GetMethod("BeginCaptureRenderDoc",
                    BindingFlags.Public | BindingFlags.Static);
                var endCapture = RenderDocType.GetMethod("EndCaptureRenderDoc",
                    BindingFlags.Public | BindingFlags.Static);

                if (beginCapture != null && endCapture != null)
                {
                    beginCapture.Invoke(null, new object[] { gameView });

                    // RepaintImmediately로 프레임을 즉시 렌더링하여 캡처에 포함시킨다
                    var repaintImmediate = typeof(EditorWindow).GetMethod("RepaintImmediately",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (repaintImmediate != null)
                    {
                        repaintImmediate.Invoke(gameView, null);
                    }
                    else
                    {
                        // RepaintImmediately를 못 찾으면 일반 Repaint + 동기 대기
                        gameView.Repaint();
                    }

                    endCapture.Invoke(null, new object[] { gameView });
                    Debug.Log("[UGDB] RenderDoc 캡처 완료 (BeginCapture/EndCapture)");
                    return true;
                }

                // 폴백: SendEvent 방식 (레거시 Unity)
                Debug.LogWarning("[UGDB] BeginCapture/EndCapture API를 찾을 수 없습니다. SendEvent 폴백 사용.");
                gameView.SendEvent(EditorGUIUtility.CommandEvent("RenderDocCapture"));
                Debug.Log("[UGDB] RenderDoc 캡처 트리거됨 (SendEvent 폴백)");
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
        /// Unity RenderDoc API → 프로젝트 폴더 → 임시 폴더 순으로 탐색.
        /// </summary>
        public static string GetLatestCapturePath()
        {
            // 1) Unity RenderDoc API로 직접 경로 가져오기 (여러 메서드명 시도)
            string[] methodNames = { "GetLastCaptureFilePath", "GetCapture", "GetLastCapturePath" };
            if (RenderDocType != null)
            {
                foreach (var name in methodNames)
                {
                    try
                    {
                        var method = RenderDocType.GetMethod(name,
                            BindingFlags.Public | BindingFlags.Static);
                        if (method == null) continue;

                        object result;
                        var parameters = method.GetParameters();
                        if (parameters.Length == 0)
                            result = method.Invoke(null, null);
                        else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                            result = method.Invoke(null, new object[] { 0 });
                        else
                            continue;

                        var path = result as string;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            Debug.Log($"[UGDB] .rdc 경로 (API: {name}): {path}");
                            return path;
                        }
                    }
                    catch (Exception)
                    {
                        // 다음 메서드 시도
                    }
                }
            }

            // 2) 프로젝트 루트 부근에서 최근 .rdc 검색 (RenderDoc 기본 캡처 위치)
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var rdcInProject = FindLatestRdc(projectPath, SearchOption.TopDirectoryOnly);
            if (rdcInProject != null) return rdcInProject;

            // 3) 임시 폴더에서 검색
            return FindLatestRdc(Path.GetTempPath(), SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// 지정 폴더에서 가장 최근 .rdc 파일을 검색한다.
        /// 캡처 직후이므로 최근 10초 이내 파일만 대상으로 한다.
        /// </summary>
        private static string FindLatestRdc(string searchPath, SearchOption option)
        {
            if (!Directory.Exists(searchPath)) return null;

            string latestFile = null;
            DateTime latestTime = DateTime.MinValue;
            var threshold = DateTime.Now.AddSeconds(-10);

            try
            {
                var rdcFiles = Directory.GetFiles(searchPath, "*.rdc", option);
                foreach (var file in rdcFiles)
                {
                    var writeTime = File.GetLastWriteTime(file);
                    if (writeTime > latestTime && writeTime > threshold)
                    {
                        latestTime = writeTime;
                        latestFile = file;
                    }
                }

                // RenderDoc 하위 폴더도 확인
                var rdocSubDir = Path.Combine(searchPath, "RenderDoc");
                if (Directory.Exists(rdocSubDir))
                {
                    var rdcInSub = Directory.GetFiles(rdocSubDir, "*.rdc", SearchOption.AllDirectories);
                    foreach (var file in rdcInSub)
                    {
                        var writeTime = File.GetLastWriteTime(file);
                        if (writeTime > latestTime && writeTime > threshold)
                        {
                            latestTime = writeTime;
                            latestFile = file;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UGDB] .rdc 검색 실패 ({searchPath}): {e.Message}");
            }

            if (latestFile != null)
                Debug.Log($"[UGDB] .rdc 발견: {latestFile}");

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
