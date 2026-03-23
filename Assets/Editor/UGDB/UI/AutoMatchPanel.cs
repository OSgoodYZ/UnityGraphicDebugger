using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UGDB.Core;

namespace UGDB.UI
{
    /// <summary>
    /// 전체 드로우콜의 자동 매칭 결과를 표시하는 패널.
    /// 필터/정렬 지원, 행 클릭 시 상세 ResultsPanel 카드 연동.
    /// </summary>
    public class AutoMatchPanel
    {
        public enum FilterMode
        {
            All,
            MatchedOnly,
            UnmatchedOnly,
        }

        public enum SortMode
        {
            DrawCallOrder,
            ConfidenceDesc,
        }

        private AutoMatcher.AutoMatchReport _report;
        private List<AutoMatcher.AutoMatchResult> _filteredResults;

        private FilterMode _filterMode = FilterMode.All;
        private SortMode _sortMode = SortMode.DrawCallOrder;
        private string _searchFilter = "";

        private Vector2 _listScrollPos;
        private int _selectedIndex = -1;

        // 상세 결과 표시용
        private Vector2 _detailScrollPos;
        private HashSet<int> _expandedDetails = new HashSet<int>();

        public void SetReport(AutoMatcher.AutoMatchReport report)
        {
            _report = report;
            _selectedIndex = -1;
            _listScrollPos = Vector2.zero;
            _detailScrollPos = Vector2.zero;
            _expandedDetails.Clear();
            ApplyFilter();
        }

        public bool HasReport => _report != null;

        public void Draw()
        {
            if (_report == null)
            {
                EditorGUILayout.HelpBox("Auto-Match 결과 없음.\n세션의 .rdc 파일이 필요합니다.", MessageType.Info);
                return;
            }

            DrawSummaryBar();
            DrawFilterBar();

            EditorGUILayout.Space(4);

            // 분할 뷰: 좌측 목록 + 우측 상세
            EditorGUILayout.BeginHorizontal();

            // 좌측: 드로우콜 목록
            EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(280, EditorGUIUtility.currentViewWidth * 0.45f)));
            DrawCallList();
            EditorGUILayout.EndVertical();

            // 구분선
            GUILayout.Box("", GUILayout.Width(1), GUILayout.ExpandHeight(true));

            // 우측: 선택된 드로우콜 상세
            EditorGUILayout.BeginVertical();
            DrawSelectedDetail();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummaryBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                string.Format("Capture: {0} ({1})", _report.captureFile, _report.graphicsAPI),
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            // 통계 배지
            var prevColor = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            GUILayout.Label(string.Format("HIGH {0}", _report.highConfidenceCount),
                EditorStyles.miniButton, GUILayout.Width(60));

            GUI.backgroundColor = new Color(0.9f, 0.7f, 0.1f);
            GUILayout.Label(string.Format("MED {0}", _report.mediumConfidenceCount),
                EditorStyles.miniButton, GUILayout.Width(60));

            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.1f);
            GUILayout.Label(string.Format("LOW {0}", _report.lowConfidenceCount),
                EditorStyles.miniButton, GUILayout.Width(60));

            GUI.backgroundColor = Color.gray;
            GUILayout.Label(string.Format("N/A {0}", _report.unmatchedCount),
                EditorStyles.miniButton, GUILayout.Width(60));

            GUI.backgroundColor = prevColor;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFilterBar()
        {
            EditorGUILayout.BeginHorizontal();

            // 필터 모드
            EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
            var newFilter = (FilterMode)EditorGUILayout.EnumPopup(_filterMode, GUILayout.Width(120));
            if (newFilter != _filterMode)
            {
                _filterMode = newFilter;
                ApplyFilter();
            }

            // 정렬
            EditorGUILayout.LabelField("Sort:", GUILayout.Width(30));
            var newSort = (SortMode)EditorGUILayout.EnumPopup(_sortMode, GUILayout.Width(130));
            if (newSort != _sortMode)
            {
                _sortMode = newSort;
                ApplyFilter();
            }

            // 텍스트 검색
            GUILayout.FlexibleSpace();
            var newSearch = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(150));
            if (newSearch != _searchFilter)
            {
                _searchFilter = newSearch;
                ApplyFilter();
            }

            EditorGUILayout.EndHorizontal();

            // 결과 수 표시
            EditorGUILayout.LabelField(
                string.Format("Showing {0} / {1} draw calls", _filteredResults.Count, _report.totalDrawCalls),
                EditorStyles.miniLabel);
        }

        private void DrawCallList()
        {
            _listScrollPos = EditorGUILayout.BeginScrollView(_listScrollPos);

            for (int i = 0; i < _filteredResults.Count; i++)
            {
                var result = _filteredResults[i];
                DrawCallListRow(i, result);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCallListRow(int displayIndex, AutoMatcher.AutoMatchResult result)
        {
            bool isSelected = _selectedIndex == displayIndex;

            // 행 배경색
            var prevBg = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // 이벤트 ID
            EditorGUILayout.LabelField(
                string.Format("#{0}", result.eventId),
                EditorStyles.miniLabel, GUILayout.Width(45));

            // 드로우콜 이름 (클릭 가능)
            if (GUILayout.Button(result.drawCallName, EditorStyles.label))
            {
                _selectedIndex = isSelected ? -1 : displayIndex;
                _detailScrollPos = Vector2.zero;
                _expandedDetails.Clear();
            }

            GUILayout.FlexibleSpace();

            // 매칭 결과
            if (result.matched && result.bestMatch != null)
            {
                string goName = result.bestMatch.renderer != null
                    ? result.bestMatch.renderer.gameObjectName : "?";
                string matName = result.bestMatch.material != null
                    ? result.bestMatch.material.name : "";

                EditorGUILayout.LabelField(
                    string.Format("{0} / {1}", goName, matName),
                    EditorStyles.miniLabel, GUILayout.Width(150));

                DrawConfidenceBadge(result.bestMatch.totalScore, result.bestMatch.confidence);
            }
            else
            {
                EditorGUILayout.LabelField("(unmatched)", EditorStyles.miniLabel, GUILayout.Width(150));
            }

            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = prevBg;
        }

        private void DrawSelectedDetail()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _filteredResults.Count)
            {
                EditorGUILayout.HelpBox("드로우콜을 선택하면 상세 정보가 표시됩니다.", MessageType.Info);
                return;
            }

            var result = _filteredResults[_selectedIndex];

            _detailScrollPos = EditorGUILayout.BeginScrollView(_detailScrollPos);

            // 헤더
            EditorGUILayout.LabelField(
                string.Format("Draw #{0}: {1}", result.eventId, result.drawCallName),
                EditorStyles.boldLabel);

            // GPU 바인딩 요약
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("GPU Bindings:", EditorStyles.boldLabel);

            if (result.indexCount > 0)
                EditorGUILayout.LabelField(string.Format("  IndexCount: {0}", result.indexCount), EditorStyles.miniLabel);
            if (result.vertexCount > 0)
                EditorGUILayout.LabelField(string.Format("  VertexCount: {0}", result.vertexCount), EditorStyles.miniLabel);

            EditorGUILayout.LabelField(string.Format("  Textures: {0}", result.textureCount), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(string.Format("  CB Floats: {0}", result.cbFloatCount), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // 매칭 결과
            if (result.matched && result.allMatches != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("Matches ({0}):", result.allMatches.Count),
                    EditorStyles.boldLabel);

                for (int i = 0; i < result.allMatches.Count; i++)
                {
                    DrawMatchResultCard(i, result.allMatches[i]);
                    EditorGUILayout.Space(2);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("매칭 결과 없음 (unmatched draw call)", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMatchResultCard(int index, DrawCallMatcher.MatchResult match)
        {
            var renderer = match.renderer;
            var material = match.material;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 헤더
            EditorGUILayout.BeginHorizontal();

            string goName = renderer != null ? renderer.gameObjectName : "(unknown)";
            bool expanded = _expandedDetails.Contains(index);

            if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (expanded) _expandedDetails.Remove(index);
                else _expandedDetails.Add(index);
            }

            EditorGUILayout.LabelField(goName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            DrawConfidenceBadge(match.totalScore, match.confidence);

            EditorGUILayout.EndHorizontal();

            // Hierarchy
            if (renderer != null && !string.IsNullOrEmpty(renderer.hierarchyPath))
                EditorGUILayout.LabelField(renderer.hierarchyPath, EditorStyles.miniLabel);

            // 축약 정보
            if (renderer != null && renderer.mesh != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("Mesh: {0} (vtx:{1} idx:{2})",
                        renderer.mesh.name, renderer.mesh.vertexCount, renderer.mesh.indexCount),
                    EditorStyles.miniLabel);
            }

            if (material != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("Material: {0} ({1})", material.name, material.shaderName),
                    EditorStyles.miniLabel);
            }

            // 상세 (펼침)
            if (expanded)
            {
                EditorGUI.indentLevel++;

                // Keywords
                if (material != null && material.activeKeywords != null && material.activeKeywords.Length > 0)
                {
                    EditorGUILayout.LabelField(
                        string.Format("Keywords: {0}", string.Join(", ", material.activeKeywords)),
                        EditorStyles.miniLabel);
                }

                // 슬롯 매핑
                if (match.slotMappings != null && match.slotMappings.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Slot Mapping:", EditorStyles.miniLabel);
                    foreach (var mapping in match.slotMappings)
                    {
                        string assetName = !string.IsNullOrEmpty(mapping.unityAssetPath)
                            ? System.IO.Path.GetFileName(mapping.unityAssetPath) : "?";
                        EditorGUILayout.LabelField(
                            string.Format("  Slot {0} ({1}x{2} {3}) -> {4} ({5})",
                                mapping.rdSlot, mapping.rdSignature.width, mapping.rdSignature.height,
                                mapping.rdSignature.format, mapping.unityPropertyName, assetName),
                            EditorStyles.miniLabel);
                    }
                }

                // 스코어 상세
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Score Breakdown:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    string.Format("  Geo:{0:F0}% Tex:{1:F0}% Var:{2:F0}% Scalar:{3:F0}% Shader:{4:F0}%",
                        match.geometryScore * 100, match.textureScore * 100,
                        match.variantScore * 100, match.scalarScore * 100, match.shaderScore * 100),
                    EditorStyles.miniLabel);

                EditorGUI.indentLevel--;
            }

            // 액션 버튼
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (material != null)
            {
                if (GUILayout.Button("Ping Asset", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    var obj = EditorUtility.InstanceIDToObject(material.instanceId);
                    if (obj != null)
                        EditorGUIUtility.PingObject(obj);
                }
            }

            if (renderer != null)
            {
                if (GUILayout.Button("Select GO", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    if (UnityEngine.Application.isPlaying)
                    {
                        var go = GameObject.Find(renderer.hierarchyPath);
                        if (go != null)
                            Selection.activeGameObject = go;
                    }
                }

                if (GUILayout.Button("Copy Path", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    EditorGUIUtility.systemCopyBuffer = renderer.hierarchyPath;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawConfidenceBadge(float score, DrawCallMatcher.MatchConfidence confidence)
        {
            Color badgeColor;
            switch (confidence)
            {
                case DrawCallMatcher.MatchConfidence.High:
                    badgeColor = new Color(0.2f, 0.8f, 0.2f);
                    break;
                case DrawCallMatcher.MatchConfidence.Medium:
                    badgeColor = new Color(0.9f, 0.7f, 0.1f);
                    break;
                case DrawCallMatcher.MatchConfidence.Low:
                    badgeColor = new Color(0.9f, 0.4f, 0.1f);
                    break;
                default:
                    badgeColor = Color.gray;
                    break;
            }

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = badgeColor;

            GUILayout.Label(
                string.Format("{0:F0}% {1}", score * 100, confidence),
                EditorStyles.miniButton, GUILayout.Width(90));

            GUI.backgroundColor = prevColor;
        }

        private void ApplyFilter()
        {
            if (_report == null || _report.results == null)
            {
                _filteredResults = new List<AutoMatcher.AutoMatchResult>();
                return;
            }

            // 필터 적용
            IEnumerable<AutoMatcher.AutoMatchResult> filtered = _report.results;

            switch (_filterMode)
            {
                case FilterMode.MatchedOnly:
                    filtered = filtered.Where(r => r.matched);
                    break;
                case FilterMode.UnmatchedOnly:
                    filtered = filtered.Where(r => !r.matched);
                    break;
            }

            // 텍스트 검색
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                var search = _searchFilter.ToLowerInvariant();
                filtered = filtered.Where(r =>
                {
                    if (r.drawCallName != null && r.drawCallName.ToLowerInvariant().Contains(search))
                        return true;
                    if (r.matched && r.bestMatch != null)
                    {
                        if (r.bestMatch.renderer != null &&
                            r.bestMatch.renderer.gameObjectName != null &&
                            r.bestMatch.renderer.gameObjectName.ToLowerInvariant().Contains(search))
                            return true;
                        if (r.bestMatch.material != null &&
                            r.bestMatch.material.name != null &&
                            r.bestMatch.material.name.ToLowerInvariant().Contains(search))
                            return true;
                    }
                    return false;
                });
            }

            // 정렬
            switch (_sortMode)
            {
                case SortMode.DrawCallOrder:
                    _filteredResults = filtered.OrderBy(r => r.drawCallIndex).ToList();
                    break;
                case SortMode.ConfidenceDesc:
                    _filteredResults = filtered.OrderByDescending(r =>
                        r.matched && r.bestMatch != null ? r.bestMatch.totalScore : -1f).ToList();
                    break;
                default:
                    _filteredResults = filtered.ToList();
                    break;
            }

            _selectedIndex = -1;
        }
    }
}
