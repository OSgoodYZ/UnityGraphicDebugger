using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UGDB.Core;

namespace UGDB.RenderDoc
{
    /// <summary>
    /// 세션 폴더 생성/목록/삭제 관리.
    /// 기본 경로: {프로젝트 루트}/UGDBCaptures/
    /// 폴더 명명: yyyyMMdd_HHmmss/
    /// </summary>
    public static class SessionManager
    {
        /// <summary>
        /// 세션 정보 데이터 클래스.
        /// </summary>
        [Serializable]
        public class SessionInfo
        {
            public string path;
            public string folderName;
            public string captureTime;
            public int rendererCount;
            public int materialCount;
            public int textureCount;
            public float textureMemoryMB;
            public bool hasRdc;
            public bool hasSnapshot;
            public bool renderDocAvailable;
            public bool rdcCaptured;
        }

        /// <summary>
        /// 새 세션 폴더를 생성하고 경로를 반환한다.
        /// </summary>
        public static string CreateSession()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sessionDir = Path.Combine(SnapshotStore.CapturesRootPath, timestamp);
            Directory.CreateDirectory(sessionDir);
            Debug.Log($"[UGDB] 세션 폴더 생성: {sessionDir}");
            return sessionDir;
        }

        /// <summary>
        /// 기존 세션 목록을 반환한다 (최신순 정렬).
        /// </summary>
        public static List<SessionInfo> GetSessions()
        {
            var sessions = new List<SessionInfo>();
            var rootPath = SnapshotStore.CapturesRootPath;

            if (!Directory.Exists(rootPath))
                return sessions;

            var dirs = Directory.GetDirectories(rootPath);
            Array.Sort(dirs);
            Array.Reverse(dirs); // 최신순

            foreach (var dir in dirs)
            {
                var info = BuildSessionInfo(dir);
                if (info != null)
                    sessions.Add(info);
            }

            return sessions;
        }

        /// <summary>
        /// 세션 폴더를 삭제한다.
        /// </summary>
        public static void DeleteSession(string sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath))
                return;

            if (Directory.Exists(sessionPath))
            {
                Directory.Delete(sessionPath, true);
                Debug.Log($"[UGDB] 세션 삭제: {sessionPath}");
            }
        }

        /// <summary>
        /// 세션에 .rdc 파일이 있는지 확인한다.
        /// </summary>
        public static bool HasRdcFile(string sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath))
                return false;

            return File.Exists(Path.Combine(sessionPath, "capture.rdc"));
        }

        /// <summary>
        /// 세션에 snapshot.json이 있는지 확인한다.
        /// </summary>
        public static bool HasSnapshot(string sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath))
                return false;

            return File.Exists(Path.Combine(sessionPath, SnapshotStore.SnapshotFileName));
        }

        /// <summary>
        /// 세션의 .rdc 파일 경로를 반환한다 (없으면 null).
        /// </summary>
        public static string GetRdcPath(string sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath))
                return null;

            var rdcPath = Path.Combine(sessionPath, "capture.rdc");
            return File.Exists(rdcPath) ? rdcPath : null;
        }

        /// <summary>
        /// 세션 디렉토리에서 SessionInfo를 구성한다.
        /// </summary>
        private static SessionInfo BuildSessionInfo(string sessionDir)
        {
            var info = new SessionInfo
            {
                path = sessionDir,
                folderName = Path.GetFileName(sessionDir),
                hasRdc = HasRdcFile(sessionDir),
                hasSnapshot = HasSnapshot(sessionDir)
            };

            // snapshot이나 rdc가 하나도 없으면 유효하지 않은 세션
            if (!info.hasSnapshot && !info.hasRdc)
                return null;

            // metadata.json에서 상세 정보 로드
            var metadataPath = Path.Combine(sessionDir, SnapshotStore.MetadataFileName);
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = File.ReadAllText(metadataPath);

                    // CaptureCoordinator.CaptureMetadata와 SnapshotStore.SessionMetadata 둘 다 호환
                    var metadata = JsonUtility.FromJson<CaptureCoordinator.CaptureMetadata>(json);
                    if (metadata != null)
                    {
                        info.captureTime = metadata.captureTime;
                        info.rendererCount = metadata.rendererCount;
                        info.materialCount = metadata.materialCount;
                        info.textureCount = metadata.textureCount;
                        info.textureMemoryMB = metadata.textureMemoryMB;
                        info.renderDocAvailable = metadata.renderDocAvailable;
                        info.rdcCaptured = metadata.rdcCaptured;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UGDB] 메타데이터 로드 실패 ({sessionDir}): {e.Message}");
                }
            }

            // captureTime이 비었으면 폴더 이름에서 추출
            if (string.IsNullOrEmpty(info.captureTime))
                info.captureTime = info.folderName;

            return info;
        }

        /// <summary>
        /// 세션 표시 이름을 생성한다 (드롭다운용).
        /// </summary>
        public static string GetDisplayName(SessionInfo info)
        {
            if (info == null)
                return "(없음)";

            var rdcTag = info.hasRdc ? " [RDC]" : "";
            return string.Format("{0} ({1}r, {2}t){3}",
                info.captureTime,
                info.rendererCount,
                info.textureCount,
                rdcTag);
        }
    }
}
