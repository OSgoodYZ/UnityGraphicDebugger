using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UGDB.Core;

namespace UGDB.UI
{
    /// <summary>
    /// 파서가 인식 못한 경우를 위한 수동 검색 UI.
    /// 검색 타입별 입력 필드 제공.
    /// </summary>
    public class ManualSearchPanel
    {
        private enum SearchType { Texture, Geometry, Shader, ConstantBuffer }
        private SearchType _searchType = SearchType.Texture;

        // Texture 입력
        private int _texWidth = 2048;
        private int _texHeight = 2048;
        private string _texFormat = "";

        // Geometry 입력
        private int _vtxCount;
        private int _idxCount;

        // Shader 입력
        private string _shaderNameInput = "";
        private string _keywordInput = "";

        // CB 입력
        private string _cbFloatInput = "";

        public System.Action<List<DrawCallMatcher.MatchResult>> OnSearchRequested;

        public void Draw(LookupEngine engine)
        {
            EditorGUILayout.LabelField("수동 검색", EditorStyles.boldLabel);

            _searchType = (SearchType)EditorGUILayout.EnumPopup("검색 타입", _searchType);

            EditorGUILayout.Space(4);

            switch (_searchType)
            {
                case SearchType.Texture:
                    DrawTextureSearch(engine);
                    break;
                case SearchType.Geometry:
                    DrawGeometrySearch(engine);
                    break;
                case SearchType.Shader:
                    DrawShaderSearch(engine);
                    break;
                case SearchType.ConstantBuffer:
                    DrawCBSearch(engine);
                    break;
            }
        }

        private void DrawTextureSearch(LookupEngine engine)
        {
            _texWidth = EditorGUILayout.IntField("Width", _texWidth);
            _texHeight = EditorGUILayout.IntField("Height", _texHeight);
            _texFormat = EditorGUILayout.TextField("Format (optional)", _texFormat);

            if (GUILayout.Button("검색"))
            {
                if (engine == null)
                {
                    ShowNoSnapshotWarning();
                    return;
                }

                var query = new DrawCallMatcher.MatchQuery();
                var sig = new TextureSignature(_texWidth, _texHeight,
                    string.IsNullOrEmpty(_texFormat) ? null : _texFormat, 0);
                query.textures = new List<TextureSignature> { sig };

                var matcher = new DrawCallMatcher(engine);
                var results = matcher.Match(query);

                if (OnSearchRequested != null)
                    OnSearchRequested(results);
            }
        }

        private void DrawGeometrySearch(LookupEngine engine)
        {
            _vtxCount = EditorGUILayout.IntField("Vertex Count", _vtxCount);
            _idxCount = EditorGUILayout.IntField("Index Count", _idxCount);

            if (GUILayout.Button("검색"))
            {
                if (engine == null)
                {
                    ShowNoSnapshotWarning();
                    return;
                }

                var query = new DrawCallMatcher.MatchQuery();
                if (_idxCount > 0) query.indexCount = _idxCount;
                if (_vtxCount > 0) query.vertexCount = _vtxCount;

                var matcher = new DrawCallMatcher(engine);
                var results = matcher.Match(query);

                if (OnSearchRequested != null)
                    OnSearchRequested(results);
            }
        }

        private void DrawShaderSearch(LookupEngine engine)
        {
            _shaderNameInput = EditorGUILayout.TextField("Shader Name", _shaderNameInput);
            _keywordInput = EditorGUILayout.TextField("Keywords (공백 구분)", _keywordInput);

            if (GUILayout.Button("검색"))
            {
                if (engine == null)
                {
                    ShowNoSnapshotWarning();
                    return;
                }

                var query = new DrawCallMatcher.MatchQuery();
                if (!string.IsNullOrEmpty(_shaderNameInput))
                    query.shaderName = _shaderNameInput;

                if (!string.IsNullOrEmpty(_keywordInput))
                {
                    query.shaderKeywords = new List<string>(
                        _keywordInput.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
                }

                var matcher = new DrawCallMatcher(engine);
                var results = matcher.Match(query);

                if (OnSearchRequested != null)
                    OnSearchRequested(results);
            }
        }

        private void DrawCBSearch(LookupEngine engine)
        {
            _cbFloatInput = EditorGUILayout.TextField("Float 값 (콤마 구분)", _cbFloatInput);

            if (GUILayout.Button("검색"))
            {
                if (engine == null)
                {
                    ShowNoSnapshotWarning();
                    return;
                }

                var query = new DrawCallMatcher.MatchQuery();
                if (!string.IsNullOrEmpty(_cbFloatInput))
                {
                    var parts = _cbFloatInput.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                    query.cbFloats = new List<float>();
                    foreach (var part in parts)
                    {
                        float val;
                        if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out val))
                        {
                            query.cbFloats.Add(val);
                        }
                    }
                }

                var matcher = new DrawCallMatcher(engine);
                var results = matcher.Match(query);

                if (OnSearchRequested != null)
                    OnSearchRequested(results);
            }
        }

        private void ShowNoSnapshotWarning()
        {
            Debug.Log("[UGDB] 먼저 Snap을 실행하세요.");
            EditorUtility.DisplayDialog("UGDB", "먼저 Snap을 실행하거나 세션을 로드하세요.", "확인");
        }
    }
}
