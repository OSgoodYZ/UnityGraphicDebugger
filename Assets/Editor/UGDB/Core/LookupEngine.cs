using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UGDB.Core
{
    /// <summary>
    /// SceneSnapshotData로부터 5개의 검색 인덱스를 빌드하고,
    /// 텍스처/셰이더/CB/지오메트리/Variant 기준으로 검색 기능을 제공한다.
    /// </summary>
    public class LookupEngine
    {
        // epsilon for float comparison (CB 값 매칭용)
        public const float FloatEpsilon = 0.001f;

        // 1. 텍스처 인덱스: 해상도+포맷 → 텍스처 목록
        private readonly Dictionary<TextureSignature, List<TextureEntry>> _textureIndex
            = new Dictionary<TextureSignature, List<TextureEntry>>();

        // 해상도만으로 검색 (포맷 무시)
        private readonly Dictionary<(int w, int h), List<TextureEntry>> _textureByResolution
            = new Dictionary<(int, int), List<TextureEntry>>();

        // 2. 셰이더 인덱스: 이름/키워드 → 셰이더+사용 머티리얼
        private readonly Dictionary<string, ShaderEntry> _shaderByName
            = new Dictionary<string, ShaderEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<ShaderEntry>> _shaderByKeyword
            = new Dictionary<string, List<ShaderEntry>>(StringComparer.OrdinalIgnoreCase);

        // 3. CB 값 인덱스: 양자화된 float 값 → 프로퍼티
        private readonly Dictionary<int, List<ScalarEntry>> _scalarByQuantizedFloat
            = new Dictionary<int, List<ScalarEntry>>();

        private readonly Dictionary<string, List<ScalarEntry>> _scalarByColorKey
            = new Dictionary<string, List<ScalarEntry>>();

        // 4. 지오메트리 인덱스: (vtx, idx) → 렌더러
        private readonly Dictionary<(int vtx, int idx), List<RendererEntry>> _geometryIndex
            = new Dictionary<(int, int), List<RendererEntry>>();

        // idx만으로 검색 (vtx 모를 때)
        private readonly Dictionary<int, List<RendererEntry>> _geometryByIdx
            = new Dictionary<int, List<RendererEntry>>();

        // 5. Variant 인덱스
        private readonly Dictionary<string, VariantEntry> _variantByKey
            = new Dictionary<string, VariantEntry>();

        private readonly Dictionary<string, List<VariantEntry>> _variantBySlotPattern
            = new Dictionary<string, List<VariantEntry>>();

        // 원본 데이터 참조
        private SceneSnapshotData _data;

        // 머티리얼 instanceId -> MaterialEntry 빠른 검색
        private readonly Dictionary<int, MaterialEntry> _materialById
            = new Dictionary<int, MaterialEntry>();

        // 머티리얼 instanceId -> RendererEntry 빠른 검색
        private readonly Dictionary<int, List<RendererEntry>> _renderersByMaterialId
            = new Dictionary<int, List<RendererEntry>>();

        public SceneSnapshotData SnapshotData => _data;

        /// <summary>
        /// SceneSnapshotData로부터 모든 인덱스를 빌드한다.
        /// </summary>
        public void BuildIndices(SceneSnapshotData data)
        {
            _data = data;
            ClearIndices();

            BuildMaterialLookup();
            BuildTextureIndex();
            BuildShaderIndex();
            BuildScalarIndex();
            BuildGeometryIndex();
            BuildVariantIndex();
        }

        private void ClearIndices()
        {
            _textureIndex.Clear();
            _textureByResolution.Clear();
            _shaderByName.Clear();
            _shaderByKeyword.Clear();
            _scalarByQuantizedFloat.Clear();
            _scalarByColorKey.Clear();
            _geometryIndex.Clear();
            _geometryByIdx.Clear();
            _variantByKey.Clear();
            _variantBySlotPattern.Clear();
            _materialById.Clear();
            _renderersByMaterialId.Clear();
        }

        #region Index Building

        private void BuildMaterialLookup()
        {
            foreach (var renderer in _data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    _materialById[mat.instanceId] = mat;

                    if (!_renderersByMaterialId.TryGetValue(mat.instanceId, out var list))
                    {
                        list = new List<RendererEntry>();
                        _renderersByMaterialId[mat.instanceId] = list;
                    }
                    list.Add(renderer);
                }
            }
        }

        private void BuildTextureIndex()
        {
            foreach (var renderer in _data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    foreach (var tex in mat.textures)
                    {
                        if (tex.textureType == "None") continue;

                        // 시그니처 기반 인덱스
                        if (!_textureIndex.TryGetValue(tex.signature, out var sigList))
                        {
                            sigList = new List<TextureEntry>();
                            _textureIndex[tex.signature] = sigList;
                        }
                        sigList.Add(tex);

                        // 해상도만 인덱스
                        var resKey = (tex.signature.width, tex.signature.height);
                        if (!_textureByResolution.TryGetValue(resKey, out var resList))
                        {
                            resList = new List<TextureEntry>();
                            _textureByResolution[resKey] = resList;
                        }
                        resList.Add(tex);
                    }
                }
            }
        }

        private void BuildShaderIndex()
        {
            foreach (var shader in _data.shaders)
            {
                _shaderByName[shader.name] = shader;

                foreach (var kw in shader.keywords)
                {
                    if (!_shaderByKeyword.TryGetValue(kw, out var list))
                    {
                        list = new List<ShaderEntry>();
                        _shaderByKeyword[kw] = list;
                    }
                    list.Add(shader);
                }
            }

            // 머티리얼의 활성 키워드도 셰이더 키워드 인덱스에 추가
            foreach (var renderer in _data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.activeKeywords == null) continue;
                    if (!_shaderByName.TryGetValue(mat.shaderName, out var shader)) continue;

                    foreach (var kw in mat.activeKeywords)
                    {
                        if (!shader.keywords.Contains(kw))
                            shader.keywords.Add(kw);

                        if (!_shaderByKeyword.TryGetValue(kw, out var list))
                        {
                            list = new List<ShaderEntry>();
                            _shaderByKeyword[kw] = list;
                        }
                        if (!list.Contains(shader))
                            list.Add(shader);
                    }
                }
            }
        }

        private void BuildScalarIndex()
        {
            foreach (var renderer in _data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    foreach (var scalar in mat.scalars)
                    {
                        if (scalar.isTextureST) continue;

                        switch (scalar.type)
                        {
                            case ScalarType.Float:
                            case ScalarType.Range:
                                AddQuantizedFloat(scalar.floatValue, scalar);
                                break;

                            case ScalarType.Color:
                                // Color의 각 채널도 float로 검색 가능
                                AddQuantizedFloat(scalar.colorValue.r, scalar);
                                AddQuantizedFloat(scalar.colorValue.g, scalar);
                                AddQuantizedFloat(scalar.colorValue.b, scalar);
                                AddQuantizedFloat(scalar.colorValue.a, scalar);

                                // Color 전체로도 검색 가능
                                var colorKey = QuantizeColor(scalar.colorValue);
                                if (!_scalarByColorKey.TryGetValue(colorKey, out var cList))
                                {
                                    cList = new List<ScalarEntry>();
                                    _scalarByColorKey[colorKey] = cList;
                                }
                                cList.Add(scalar);
                                break;

                            case ScalarType.Vector:
                                AddQuantizedFloat(scalar.vectorValue.x, scalar);
                                AddQuantizedFloat(scalar.vectorValue.y, scalar);
                                AddQuantizedFloat(scalar.vectorValue.z, scalar);
                                AddQuantizedFloat(scalar.vectorValue.w, scalar);
                                break;

                            case ScalarType.Int:
                                AddQuantizedFloat(scalar.intValue, scalar);
                                break;
                        }
                    }
                }
            }
        }

        private void AddQuantizedFloat(float value, ScalarEntry scalar)
        {
            int key = QuantizeFloat(value);
            if (!_scalarByQuantizedFloat.TryGetValue(key, out var list))
            {
                list = new List<ScalarEntry>();
                _scalarByQuantizedFloat[key] = list;
            }
            list.Add(scalar);
        }

        private void BuildGeometryIndex()
        {
            foreach (var renderer in _data.renderers)
            {
                if (renderer.mesh == null) continue;

                var vtx = renderer.mesh.vertexCount;
                var idx = renderer.mesh.indexCount;

                // (vtx, idx) 쌍 인덱스
                var key = (vtx, idx);
                if (!_geometryIndex.TryGetValue(key, out var list))
                {
                    list = new List<RendererEntry>();
                    _geometryIndex[key] = list;
                }
                list.Add(renderer);

                // idx만 인덱스
                if (!_geometryByIdx.TryGetValue(idx, out var idxList))
                {
                    idxList = new List<RendererEntry>();
                    _geometryByIdx[idx] = idxList;
                }
                idxList.Add(renderer);
            }
        }

        private void BuildVariantIndex()
        {
            foreach (var variant in _data.variants)
            {
                _variantByKey[variant.variantKey] = variant;

                if (!string.IsNullOrEmpty(variant.slotPatternKey))
                {
                    if (!_variantBySlotPattern.TryGetValue(variant.slotPatternKey, out var list))
                    {
                        list = new List<VariantEntry>();
                        _variantBySlotPattern[variant.slotPatternKey] = list;
                    }
                    list.Add(variant);
                }
            }
        }

        #endregion

        #region Search: Texture

        /// <summary>
        /// 해상도 + 포맷으로 텍스처 검색.
        /// </summary>
        public List<TextureEntry> SearchByTexture(TextureSignature signature)
        {
            if (_textureIndex.TryGetValue(signature, out var results))
                return new List<TextureEntry>(results);
            return new List<TextureEntry>();
        }

        /// <summary>
        /// 해상도 + 포맷으로 텍스처 검색 (포맷 매핑 지원).
        /// RenderDoc 포맷 이름도 Unity 포맷으로 자동 변환하여 검색.
        /// </summary>
        public List<TextureEntry> SearchByTextureWithFormatMapping(TextureSignature signature)
        {
            var results = new List<TextureEntry>();

            foreach (var kvp in _textureIndex)
            {
                if (kvp.Key.width == signature.width && kvp.Key.height == signature.height
                    && TextureFormatMap.FormatsMatch(kvp.Key.format, signature.format))
                {
                    results.AddRange(kvp.Value);
                }
            }

            return results;
        }

        /// <summary>
        /// 해상도만으로 텍스처 검색 (포맷 모를 때).
        /// </summary>
        public List<TextureEntry> SearchByResolution(int width, int height)
        {
            if (_textureByResolution.TryGetValue((width, height), out var results))
                return new List<TextureEntry>(results);
            return new List<TextureEntry>();
        }

        #endregion

        #region Search: Shader

        /// <summary>
        /// 셰이더 이름으로 검색 (부분 매칭 지원).
        /// </summary>
        public List<ShaderEntry> SearchByShaderName(string name)
        {
            // 정확한 매칭
            if (_shaderByName.TryGetValue(name, out var exact))
                return new List<ShaderEntry> { exact };

            // 부분 매칭
            var results = new List<ShaderEntry>();
            foreach (var kvp in _shaderByName)
            {
                if (kvp.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    results.Add(kvp.Value);
            }
            return results;
        }

        /// <summary>
        /// 셰이더 키워드로 검색.
        /// </summary>
        public List<ShaderEntry> SearchByKeyword(string keyword)
        {
            if (_shaderByKeyword.TryGetValue(keyword, out var results))
                return new List<ShaderEntry>(results);
            return new List<ShaderEntry>();
        }

        /// <summary>
        /// 여러 키워드로 검색 (모든 키워드를 포함하는 셰이더).
        /// </summary>
        public List<ShaderEntry> SearchByKeywords(IEnumerable<string> keywords)
        {
            List<ShaderEntry> intersection = null;

            foreach (var kw in keywords)
            {
                var matches = SearchByKeyword(kw);
                if (intersection == null)
                    intersection = matches;
                else
                    intersection = intersection.Where(s => matches.Contains(s)).ToList();

                if (intersection.Count == 0)
                    break;
            }

            return intersection ?? new List<ShaderEntry>();
        }

        #endregion

        #region Search: CB (Scalar)

        /// <summary>
        /// float 값으로 CB 프로퍼티 검색 (epsilon 비교).
        /// </summary>
        public List<ScalarEntry> SearchByFloat(float value)
        {
            var results = new List<ScalarEntry>();

            // 양자화 키 기반으로 근처 값 검색
            int qKey = QuantizeFloat(value);
            for (int offset = -1; offset <= 1; offset++)
            {
                if (_scalarByQuantizedFloat.TryGetValue(qKey + offset, out var candidates))
                {
                    foreach (var s in candidates)
                    {
                        float sv = GetScalarFloat(s);
                        if (Mathf.Abs(sv - value) <= FloatEpsilon)
                            results.Add(s);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Color 값으로 CB 프로퍼티 검색 (epsilon 비교).
        /// </summary>
        public List<ScalarEntry> SearchByColor(Color color)
        {
            var results = new List<ScalarEntry>();
            var qKey = QuantizeColor(color);

            if (_scalarByColorKey.TryGetValue(qKey, out var candidates))
            {
                foreach (var s in candidates)
                {
                    if (ColorApproxEqual(s.colorValue, color))
                        results.Add(s);
                }
            }

            return results;
        }

        /// <summary>
        /// 여러 float 값의 세트로 같은 머티리얼에서 매칭되는 것을 검색.
        /// RenderDoc CB에서 여러 값을 한번에 붙여넣었을 때 사용.
        /// </summary>
        public List<MaterialEntry> SearchByFloatSet(float[] values)
        {
            if (values == null || values.Length == 0)
                return new List<MaterialEntry>();

            // 첫 번째 값으로 후보 머티리얼 필터링
            var firstMatches = SearchByFloat(values[0]);
            var candidateMaterials = new HashSet<int>(
                firstMatches.Select(s => s.materialInstanceId)
            );

            // 나머지 값들로 교집합 축소
            for (int i = 1; i < values.Length; i++)
            {
                var matches = SearchByFloat(values[i]);
                var matchMats = new HashSet<int>(matches.Select(s => s.materialInstanceId));
                candidateMaterials.IntersectWith(matchMats);

                if (candidateMaterials.Count == 0)
                    break;
            }

            return candidateMaterials
                .Where(id => _materialById.ContainsKey(id))
                .Select(id => _materialById[id])
                .ToList();
        }

        #endregion

        #region Search: Geometry

        /// <summary>
        /// vertexCount + indexCount로 렌더러 검색.
        /// </summary>
        public List<RendererEntry> SearchByGeometry(int vertexCount, int indexCount)
        {
            if (_geometryIndex.TryGetValue((vertexCount, indexCount), out var results))
                return new List<RendererEntry>(results);
            return new List<RendererEntry>();
        }

        /// <summary>
        /// indexCount만으로 렌더러 검색 (DrawIndexed에서 idx만 알 때).
        /// </summary>
        public List<RendererEntry> SearchByIndexCount(int indexCount)
        {
            if (_geometryByIdx.TryGetValue(indexCount, out var results))
                return new List<RendererEntry>(results);
            return new List<RendererEntry>();
        }

        #endregion

        #region Search: Variant

        /// <summary>
        /// variant 키로 검색.
        /// </summary>
        public VariantEntry SearchByVariantKey(string variantKey)
        {
            _variantByKey.TryGetValue(variantKey, out var result);
            return result;
        }

        /// <summary>
        /// 슬롯 패턴으로 variant 검색.
        /// </summary>
        public List<VariantEntry> SearchBySlotPattern(string slotPatternKey)
        {
            if (_variantBySlotPattern.TryGetValue(slotPatternKey, out var results))
                return new List<VariantEntry>(results);
            return new List<VariantEntry>();
        }

        /// <summary>
        /// RenderDoc에서 복사한 텍스처 슬롯 시그니처 목록으로 variant 검색.
        /// </summary>
        public List<VariantEntry> SearchByTextureSlots(List<TextureSignature> signatures)
        {
            var patternKey = VariantTracker.BuildSlotPatternKeyFromSignatures(signatures);
            return SearchBySlotPattern(patternKey);
        }

        #endregion

        #region Utility Lookups

        /// <summary>
        /// materialInstanceId로 MaterialEntry 검색.
        /// </summary>
        public MaterialEntry GetMaterialById(int instanceId)
        {
            _materialById.TryGetValue(instanceId, out var result);
            return result;
        }

        /// <summary>
        /// materialInstanceId로 해당 머티리얼을 사용하는 렌더러 목록 검색.
        /// </summary>
        public List<RendererEntry> GetRenderersByMaterialId(int materialInstanceId)
        {
            if (_renderersByMaterialId.TryGetValue(materialInstanceId, out var results))
                return new List<RendererEntry>(results);
            return new List<RendererEntry>();
        }

        #endregion

        #region Quantization Helpers

        /// <summary>
        /// float를 양자화하여 Dictionary 키로 사용 가능한 int로 변환.
        /// 소수점 3자리까지 유효.
        /// </summary>
        private static int QuantizeFloat(float value)
        {
            return Mathf.RoundToInt(value * 1000f);
        }

        /// <summary>
        /// Color를 양자화하여 Dictionary 키로 사용.
        /// </summary>
        private static string QuantizeColor(Color color)
        {
            return $"{Mathf.RoundToInt(color.r * 1000)},{Mathf.RoundToInt(color.g * 1000)},{Mathf.RoundToInt(color.b * 1000)},{Mathf.RoundToInt(color.a * 1000)}";
        }

        private static float GetScalarFloat(ScalarEntry s)
        {
            switch (s.type)
            {
                case ScalarType.Float:
                case ScalarType.Range:
                    return s.floatValue;
                case ScalarType.Int:
                    return s.intValue;
                default:
                    return 0f;
            }
        }

        private static bool ColorApproxEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= FloatEpsilon
                && Mathf.Abs(a.g - b.g) <= FloatEpsilon
                && Mathf.Abs(a.b - b.b) <= FloatEpsilon
                && Mathf.Abs(a.a - b.a) <= FloatEpsilon;
        }

        #endregion
    }
}
