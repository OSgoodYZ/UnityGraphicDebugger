using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UGDB.Core;
using UGDB.Parser;
using UGDB.RenderDoc;

namespace UGDB.UI
{
    /// <summary>
    /// UGDB 메인 에디터 윈도우.
    /// 계획서 2-4절 레이아웃 구현.
    /// </summary>
    public class UGDBWindow : EditorWindow
    {
        [MenuItem("Window/UGDB Lookup")]
        public static void ShowWindow()
        {
            var window = GetWindow<UGDBWindow>("UGDB Lookup");
            window.minSize = new Vector2(400, 500);
        }

        // 탭
        private enum Tab { AutoSearch, ManualSearch }
        private Tab _currentTab = Tab.AutoSearch;

        // 컴포넌트
        private PasteArea _pasteArea;
        private ResultsPanel _resultsPanel;
        private ManualSearchPanel _manualSearchPanel;

        // 데이터
        private LookupEngine _engine;
        private DrawCallMatcher _matcher;
        private SceneSnapshotData _snapshotData;

        // 히스토리
        private List<List<DrawCallMatcher.MatchResult>> _history = new List<List<DrawCallMatcher.MatchResult>>();
        private int _historyIndex = -1;

        // 스냅샷 세션
        private List<SessionManager.SessionInfo> _sessions = new List<SessionManager.SessionInfo>();
        private string[] _sessionNames;
        private int _selectedSession = -1;

        private void OnEnable()
        {
            _pasteArea = new PasteArea();
            _resultsPanel = new ResultsPanel();
            _manualSearchPanel = new ManualSearchPanel();

            _pasteArea.OnSearchRequested = OnAutoSearch;
            _manualSearchPanel.OnSearchRequested = OnManualSearch;

            RefreshSessionList();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSnapshotBar();

            EditorGUILayout.Space(4);

            // 탭
            _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, new[] { "자동 검색", "수동 검색" });

            EditorGUILayout.Space(4);

            switch (_currentTab)
            {
                case Tab.AutoSearch:
                    DrawAutoSearchTab();
                    break;
                case Tab.ManualSearch:
                    DrawManualSearchTab();
                    break;
            }

            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UGDB Lookup", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // RenderDoc 연동 상태 표시
            bool rdcAvailable = RenderDocBridge.IsAvailable();
            var rdcLabel = rdcAvailable ? "RenderDoc: ON" : "RenderDoc: OFF";
            var rdcStyle = new GUIStyle(EditorStyles.miniLabel);
            rdcStyle.normal.textColor = rdcAvailable ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;
            GUILayout.Label(rdcLabel, rdcStyle);

            // Snap 버튼
            var snapLabel = rdcAvailable ? "Snap + RDC" : "Snap";
            if (GUILayout.Button(snapLabel, EditorStyles.toolbarButton, GUILayout.Width(rdcAvailable ? 80 : 50)))
            {
                OnSnapClicked();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSnapshotBar()
        {
            EditorGUILayout.BeginHorizontal();

            if (_snapshotData != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("Snapshot: {0} ({1} renderers, {2} textures)",
                        _snapshotData.timestamp,
                        _snapshotData.statistics != null ? _snapshotData.statistics.totalRenderers : 0,
                        _snapshotData.statistics != null ? _snapshotData.statistics.totalTextures : 0),
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("스냅샷 없음 — Snap 버튼을 눌러 수집하세요", EditorStyles.miniLabel);
            }

            // 세션 드롭다운 (SessionManager 기반)
            if (_sessionNames != null && _sessionNames.Length > 0)
            {
                int newSelection = EditorGUILayout.Popup(_selectedSession, _sessionNames, GUILayout.Width(220));
                if (newSelection != _selectedSession && newSelection >= 0)
                {
                    _selectedSession = newSelection;
                    LoadSession(_sessions[_selectedSession].path);
                }
            }

            if (GUILayout.Button("새로고침", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                RefreshSessionList();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAutoSearchTab()
        {
            _pasteArea.Draw();

            EditorGUILayout.Space(8);

            // 히스토리 네비게이션
            if (_history.Count > 1)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _historyIndex > 0;
                if (GUILayout.Button("← 이전", GUILayout.Width(60)))
                {
                    _historyIndex--;
                    _resultsPanel.SetResults(_history[_historyIndex]);
                }
                GUI.enabled = _historyIndex < _history.Count - 1;
                if (GUILayout.Button("다음 →", GUILayout.Width(60)))
                {
                    _historyIndex++;
                    _resultsPanel.SetResults(_history[_historyIndex]);
                }
                GUI.enabled = true;
                EditorGUILayout.LabelField(
                    string.Format("({0}/{1})", _historyIndex + 1, _history.Count),
                    EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            _resultsPanel.Draw();
        }

        private void DrawManualSearchTab()
        {
            _manualSearchPanel.Draw(_engine);
            _resultsPanel.Draw();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (_snapshotData != null && _snapshotData.statistics != null)
            {
                var stats = _snapshotData.statistics;
                float memMB = stats.totalTextureMemoryBytes / (1024f * 1024f);
                EditorGUILayout.LabelField(
                    string.Format("{0} renderers | {1} materials | {2} textures ({3:F1} MB)",
                        stats.totalRenderers, stats.totalMaterials, stats.totalTextures, memMB),
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("대기 중", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 이벤트 핸들러 ──

        private void OnSnapClicked()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[UGDB] Play 모드에서만 Snap을 실행할 수 있습니다.");
                EditorUtility.DisplayDialog("UGDB", "Play 모드에서만 Snap을 실행할 수 있습니다.", "확인");
                return;
            }

            // CaptureCoordinator를 통해 RenderDoc + SceneSnapshot 동시 캡처
            var result = CaptureCoordinator.CaptureFrame();

            if (result.success && result.snapshotData != null)
            {
                _snapshotData = result.snapshotData;
                _engine = new LookupEngine();
                _engine.BuildIndices(_snapshotData);
                _matcher = new DrawCallMatcher(_engine);

                RefreshSessionList();

                // 새로 생성된 세션을 선택
                for (int i = 0; i < _sessions.Count; i++)
                {
                    if (_sessions[i].path == result.sessionDir)
                    {
                        _selectedSession = i;
                        break;
                    }
                }

                Repaint();
            }
            else if (!string.IsNullOrEmpty(result.errorMessage))
            {
                EditorUtility.DisplayDialog("UGDB", result.errorMessage, "확인");
            }
        }

        private void OnAutoSearch(ParseResult parseResult)
        {
            if (_engine == null || _matcher == null)
            {
                Debug.Log("[UGDB] 먼저 Snap을 실행하세요.");
                EditorUtility.DisplayDialog("UGDB", "먼저 Snap을 실행하거나 세션을 로드하세요.", "확인");
                return;
            }

            var query = BuildMatchQuery(parseResult);
            var results = _matcher.Match(query);

            _resultsPanel.SetResults(results);

            // 히스토리 추가
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(results);
            _historyIndex = _history.Count - 1;

            Repaint();
        }

        private void OnManualSearch(List<DrawCallMatcher.MatchResult> results)
        {
            _resultsPanel.SetResults(results);
            Repaint();
        }

        private DrawCallMatcher.MatchQuery BuildMatchQuery(ParseResult parseResult)
        {
            var query = new DrawCallMatcher.MatchQuery();
            query.indexCount = parseResult.indexCount;
            query.vertexCount = parseResult.vertexCount;

            if (parseResult.textures.Count > 0)
                query.textures = parseResult.textures;

            if (parseResult.cbFloats.Count > 0)
                query.cbFloats = parseResult.cbFloats;

            if (parseResult.cbVectors.Count > 0)
            {
                query.cbColors = new List<Color>();
                foreach (var v in parseResult.cbVectors)
                    query.cbColors.Add(new Color(v.x, v.y, v.z, v.w));
            }

            if (parseResult.shaderKeywords.Count > 0)
                query.shaderKeywords = parseResult.shaderKeywords;

            if (!string.IsNullOrEmpty(parseResult.shaderName))
                query.shaderName = parseResult.shaderName;

            return query;
        }

        // ── 세션 관리 (SessionManager 기반) ──

        private void RefreshSessionList()
        {
            _sessions = SessionManager.GetSessions();
            if (_sessions.Count > 0)
            {
                _sessionNames = new string[_sessions.Count];
                for (int i = 0; i < _sessions.Count; i++)
                {
                    _sessionNames[i] = SessionManager.GetDisplayName(_sessions[i]);
                }

                if (_selectedSession < 0 && _sessions.Count > 0)
                {
                    _selectedSession = 0;
                    LoadSession(_sessions[0].path);
                }
            }
            else
            {
                _sessionNames = new string[0];
                _selectedSession = -1;
            }
        }

        private void LoadSession(string sessionPath)
        {
            _snapshotData = SnapshotStore.Load(sessionPath);
            if (_snapshotData != null)
            {
                _engine = new LookupEngine();
                _engine.BuildIndices(_snapshotData);
                _matcher = new DrawCallMatcher(_engine);
                Debug.Log(string.Format("[UGDB] 세션 로드: {0}", sessionPath));
            }
            else
            {
                Debug.LogWarning(string.Format("[UGDB] 세션 로드 실패: {0}", sessionPath));
            }
        }
    }
}
