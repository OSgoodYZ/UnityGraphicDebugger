using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UGDB.Core;

namespace UGDB.UI
{
    /// <summary>
    /// 검색 결과를 카드 형태로 렌더링하는 패널.
    /// 계획서 2-4절 결과 카드 UI.
    /// </summary>
    public class ResultsPanel
    {
        private List<DrawCallMatcher.MatchResult> _results;
        private Vector2 _scrollPos;
        private HashSet<int> _expandedCards = new HashSet<int>();

        public void SetResults(List<DrawCallMatcher.MatchResult> results)
        {
            _results = results;
            _scrollPos = Vector2.zero;
            _expandedCards.Clear();
        }

        public void Draw()
        {
            if (_results == null || _results.Count == 0)
            {
                if (_results != null)
                    EditorGUILayout.HelpBox("매칭 결과 없음", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                string.Format("Results ({0} match{1})", _results.Count, _results.Count > 1 ? "es" : ""),
                EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < _results.Count; i++)
            {
                DrawResultCard(i, _results[i]);
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawResultCard(int index, DrawCallMatcher.MatchResult result)
        {
            var renderer = result.renderer;
            var material = result.material;

            // 카드 배경
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 헤더: GameObject 이름 + 신뢰도
            EditorGUILayout.BeginHorizontal();

            string goName = renderer != null ? renderer.gameObjectName : "(unknown)";
            bool expanded = _expandedCards.Contains(index);

            if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                if (expanded) _expandedCards.Remove(index);
                else _expandedCards.Add(index);
            }

            EditorGUILayout.LabelField(goName, EditorStyles.boldLabel);

            // 신뢰도 배지
            GUILayout.FlexibleSpace();
            DrawConfidenceBadge(result.totalScore, result.confidence);

            EditorGUILayout.EndHorizontal();

            // Hierarchy 경로
            if (renderer != null && !string.IsNullOrEmpty(renderer.hierarchyPath))
            {
                EditorGUILayout.LabelField(renderer.hierarchyPath, EditorStyles.miniLabel);
            }

            // 상세 정보 (펼쳐진 경우)
            if (expanded)
            {
                DrawExpandedDetails(result);
            }
            else
            {
                // 축약 정보
                DrawCompactDetails(result);
            }

            // 액션 버튼
            DrawActionButtons(result);

            EditorGUILayout.EndVertical();
        }

        private void DrawCompactDetails(DrawCallMatcher.MatchResult result)
        {
            var renderer = result.renderer;
            var material = result.material;

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
        }

        private void DrawExpandedDetails(DrawCallMatcher.MatchResult result)
        {
            var renderer = result.renderer;
            var material = result.material;

            EditorGUI.indentLevel++;

            // Mesh 정보
            if (renderer != null && renderer.mesh != null)
            {
                EditorGUILayout.LabelField(
                    string.Format("Mesh: {0} (vtx:{1} idx:{2} subMesh:{3})",
                        renderer.mesh.name, renderer.mesh.vertexCount,
                        renderer.mesh.indexCount, renderer.mesh.subMeshCount),
                    EditorStyles.miniLabel);
            }

            // Material + Shader
            if (material != null)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(string.Format("Material: {0}", material.name));
                EditorGUILayout.LabelField(string.Format("Shader: {0}", material.shaderName), EditorStyles.miniLabel);

                // Keywords
                if (material.activeKeywords != null && material.activeKeywords.Length > 0)
                {
                    EditorGUILayout.LabelField(
                        string.Format("Keywords: {0}", string.Join(", ", material.activeKeywords)),
                        EditorStyles.miniLabel);
                }

                // 텍스처 슬롯
                if (material.textures != null && material.textures.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Textures:", EditorStyles.miniLabel);
                    for (int i = 0; i < material.textures.Count; i++)
                    {
                        var tex = material.textures[i];
                        string assetName = !string.IsNullOrEmpty(tex.assetPath) ?
                            System.IO.Path.GetFileName(tex.assetPath) : "(none)";
                        EditorGUILayout.LabelField(
                            string.Format("  [{0}] {1} → {2} ({3}x{4} {5})",
                                i, tex.propertyName, assetName,
                                tex.signature.width, tex.signature.height, tex.signature.format),
                            EditorStyles.miniLabel);
                    }
                }

                // 슬롯 매핑 (RenderDoc → Unity)
                if (result.slotMappings != null && result.slotMappings.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Slot Mapping:", EditorStyles.miniLabel);
                    foreach (var mapping in result.slotMappings)
                    {
                        string assetName = !string.IsNullOrEmpty(mapping.unityAssetPath) ?
                            System.IO.Path.GetFileName(mapping.unityAssetPath) : "?";
                        EditorGUILayout.LabelField(
                            string.Format("  Slot {0} ({1}x{2} {3}) → {4} ({5})",
                                mapping.rdSlot, mapping.rdSignature.width, mapping.rdSignature.height,
                                mapping.rdSignature.format, mapping.unityPropertyName, assetName),
                            EditorStyles.miniLabel);
                    }
                }

                // 스칼라 값 (CB Layout 추정)
                if (material.scalars != null && material.scalars.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("CB Layout (추정):", EditorStyles.miniLabel);
                    int offset = 0;
                    foreach (var scalar in material.scalars)
                    {
                        string valueStr = "";
                        switch (scalar.type)
                        {
                            case ScalarType.Float:
                            case ScalarType.Range:
                                valueStr = scalar.floatValue.ToString("F3");
                                break;
                            case ScalarType.Color:
                                valueStr = string.Format("({0:F2}, {1:F2}, {2:F2}, {3:F2})",
                                    scalar.colorValue.r, scalar.colorValue.g, scalar.colorValue.b, scalar.colorValue.a);
                                break;
                            case ScalarType.Vector:
                                valueStr = string.Format("({0:F2}, {1:F2}, {2:F2}, {3:F2})",
                                    scalar.vectorValue.x, scalar.vectorValue.y, scalar.vectorValue.z, scalar.vectorValue.w);
                                break;
                            case ScalarType.Int:
                                valueStr = scalar.intValue.ToString();
                                break;
                        }
                        EditorGUILayout.LabelField(
                            string.Format("  [{0}] {1} = {2}", offset, scalar.propertyName, valueStr),
                            EditorStyles.miniLabel);
                        offset += (scalar.type == ScalarType.Color || scalar.type == ScalarType.Vector) ? 16 : 4;
                    }
                }
            }

            // 스코어 상세
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Score Breakdown:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                string.Format("  Geometry:{0:F0}% Texture:{1:F0}% Variant:{2:F0}% Scalar:{3:F0}% Shader:{4:F0}%",
                    result.geometryScore * 100, result.textureScore * 100,
                    result.variantScore * 100, result.scalarScore * 100, result.shaderScore * 100),
                EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
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

        private void DrawActionButtons(DrawCallMatcher.MatchResult result)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Ping Asset
            if (result.material != null)
            {
                if (GUILayout.Button("Ping Asset", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    var obj = EditorUtility.InstanceIDToObject(result.material.instanceId);
                    if (obj != null)
                        EditorGUIUtility.PingObject(obj);
                }
            }

            // Select GO (Play 모드에서만)
            if (result.renderer != null)
            {
                if (GUILayout.Button("Select GO", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    if (Application.isPlaying)
                    {
                        var go = GameObject.Find(result.renderer.hierarchyPath);
                        if (go != null)
                            Selection.activeGameObject = go;
                        else
                            Debug.Log(string.Format("[UGDB] GameObject를 찾을 수 없습니다: {0}", result.renderer.hierarchyPath));
                    }
                    else
                    {
                        Debug.Log("[UGDB] Play 모드에서만 Select GO를 사용할 수 있습니다.");
                    }
                }
            }

            // Copy Path
            if (result.renderer != null)
            {
                if (GUILayout.Button("Copy Path", EditorStyles.miniButton, GUILayout.Width(75)))
                {
                    EditorGUIUtility.systemCopyBuffer = result.renderer.hierarchyPath;
                    Debug.Log(string.Format("[UGDB] 경로 복사됨: {0}", result.renderer.hierarchyPath));
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
