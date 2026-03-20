using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace UGDB.Core
{
    /// <summary>
    /// SceneSnapshotData를 JSON 파일로 저장/로드한다.
    /// 저장 경로: {ProjectRoot}/UGDBCaptures/{timestamp}/snapshot.json
    /// </summary>
    public static class SnapshotStore
    {
        public const string CapturesFolder = "UGDBCaptures";
        public const string SnapshotFileName = "snapshot.json";
        public const string MetadataFileName = "metadata.json";

        /// <summary>
        /// 스냅샷의 기본 저장 디렉토리 경로.
        /// </summary>
        public static string CapturesRootPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), CapturesFolder);

        #region Save

        /// <summary>
        /// 스냅샷을 JSON으로 저장하고 저장된 세션 디렉토리 경로를 반환한다.
        /// </summary>
        public static string Save(SceneSnapshotData data)
        {
            return Save(data, null);
        }

        /// <summary>
        /// 스냅샷을 지정된 세션 디렉토리에 JSON으로 저장한다.
        /// sessionDir이 null이면 타임스탬프 기반 디렉토리를 자동 생성한다.
        /// </summary>
        public static string Save(SceneSnapshotData data, string sessionDir)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (string.IsNullOrEmpty(sessionDir))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                sessionDir = Path.Combine(CapturesRootPath, timestamp);
            }

            Directory.CreateDirectory(sessionDir);

            // snapshot.json
            var snapshotPath = Path.Combine(sessionDir, SnapshotFileName);
            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(snapshotPath, json);

            // metadata.json
            var metadata = new SessionMetadata
            {
                captureTime = data.timestamp,
                unityVersion = data.unityVersion,
                graphicsAPI = data.graphicsAPI,
                screenWidth = data.screenWidth,
                screenHeight = data.screenHeight,
                rendererCount = data.statistics.totalRenderers,
                materialCount = data.statistics.totalMaterials,
                textureCount = data.statistics.totalTextures,
                textureMemoryMB = data.statistics.totalTextureMemoryBytes / (1024f * 1024f)
            };

            var metadataPath = Path.Combine(sessionDir, MetadataFileName);
            File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));

            Debug.Log($"[UGDB] Snapshot saved to: {sessionDir}");
            return sessionDir;
        }

        #endregion

        #region Load

        /// <summary>
        /// 세션 디렉토리에서 스냅샷을 로드한다.
        /// </summary>
        public static SceneSnapshotData Load(string sessionDir)
        {
            if (string.IsNullOrEmpty(sessionDir))
                throw new ArgumentNullException(nameof(sessionDir));

            var snapshotPath = Path.Combine(sessionDir, SnapshotFileName);
            if (!File.Exists(snapshotPath))
                throw new FileNotFoundException($"Snapshot not found: {snapshotPath}");

            var json = File.ReadAllText(snapshotPath);
            var data = JsonUtility.FromJson<SceneSnapshotData>(json);

            if (data == null)
                throw new InvalidOperationException($"Failed to deserialize snapshot: {snapshotPath}");

            Debug.Log($"[UGDB] Snapshot loaded from: {sessionDir} ({data.statistics.totalRenderers} renderers)");
            return data;
        }

        /// <summary>
        /// JSON 파일 경로에서 직접 스냅샷을 로드한다.
        /// </summary>
        public static SceneSnapshotData LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var json = File.ReadAllText(filePath);
            return JsonUtility.FromJson<SceneSnapshotData>(json);
        }

        #endregion

        #region Session Management

        /// <summary>
        /// 저장된 모든 세션 디렉토리 목록을 반환한다 (최신순).
        /// </summary>
        public static string[] GetAllSessions()
        {
            if (!Directory.Exists(CapturesRootPath))
                return Array.Empty<string>();

            var dirs = Directory.GetDirectories(CapturesRootPath);
            // 타임스탬프 기반 폴더명이므로 역순 정렬 = 최신순
            Array.Sort(dirs);
            Array.Reverse(dirs);
            return dirs;
        }

        /// <summary>
        /// 세션의 메타데이터를 로드한다.
        /// </summary>
        public static SessionMetadata LoadMetadata(string sessionDir)
        {
            var metadataPath = Path.Combine(sessionDir, MetadataFileName);
            if (!File.Exists(metadataPath))
                return null;

            var json = File.ReadAllText(metadataPath);
            return JsonUtility.FromJson<SessionMetadata>(json);
        }

        /// <summary>
        /// 세션 디렉토리를 삭제한다.
        /// </summary>
        public static void DeleteSession(string sessionDir)
        {
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, true);
                Debug.Log($"[UGDB] Session deleted: {sessionDir}");
            }
        }

        /// <summary>
        /// 가장 최근 세션의 스냅샷을 로드한다.
        /// </summary>
        public static SceneSnapshotData LoadLatest()
        {
            var sessions = GetAllSessions();
            if (sessions.Length == 0)
                return null;

            return Load(sessions[0]);
        }

        #endregion

        #region Metadata

        [Serializable]
        public class SessionMetadata
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
        }

        #endregion
    }
}
