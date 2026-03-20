using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UGDB.Core
{
    /// <summary>
    /// 여러 검색 키를 동시에 사용하여 가중치 스코어 기반의 복합 매칭을 수행한다.
    /// RenderDoc에서 여러 정보를 한번에 붙여넣었을 때 가장 매칭도가 높은 렌더러를 찾는다.
    /// </summary>
    public class DrawCallMatcher
    {
        // 가중치 (합계 1.0)
        public const float WeightGeometry = 0.30f;
        public const float WeightTexture = 0.25f;
        public const float WeightVariant = 0.15f;
        public const float WeightScalar = 0.20f;
        public const float WeightShader = 0.10f;

        // 신뢰도 등급
        public const float ThresholdHigh = 0.90f;
        public const float ThresholdMedium = 0.70f;
        public const float ThresholdLow = 0.50f;

        private readonly LookupEngine _engine;

        public DrawCallMatcher(LookupEngine engine)
        {
            _engine = engine;
        }

        /// <summary>
        /// 복합 매칭 입력 데이터.
        /// 각 필드는 optional — 있는 것만 채워서 전달한다.
        /// </summary>
        public class MatchQuery
        {
            public int? indexCount;
            public int? vertexCount;
            public List<TextureSignature> textures;
            public List<float> cbFloats;
            public List<Color> cbColors;
            public List<string> shaderKeywords;
            public string shaderName;
        }

        /// <summary>
        /// 매칭 결과 하나.
        /// </summary>
        public class MatchResult
        {
            public RendererEntry renderer;
            public MaterialEntry material;
            public VariantEntry variant;

            public float totalScore;
            public float geometryScore;
            public float textureScore;
            public float variantScore;
            public float scalarScore;
            public float shaderScore;

            public MatchConfidence confidence;

            // 슬롯 매핑 (RenderDoc 슬롯 → Unity 프로퍼티)
            public List<SlotMapping> slotMappings;
        }

        public class SlotMapping
        {
            public int rdSlot;
            public TextureSignature rdSignature;
            public string unityPropertyName;
            public string unityAssetPath;
        }

        public enum MatchConfidence
        {
            High,   // 90~100%
            Medium, // 70~89%
            Low,    // 50~69%
            None    // <50%
        }

        /// <summary>
        /// 복합 매칭을 수행하고 스코어 순으로 정렬된 결과를 반환한다.
        /// </summary>
        public List<MatchResult> Match(MatchQuery query)
        {
            // 1. 각 검색 키로 후보 렌더러 수집
            var candidates = GatherCandidates(query);

            if (candidates.Count == 0)
                return new List<MatchResult>();

            // 2. 각 후보에 대해 스코어 계산
            var results = new List<MatchResult>();

            foreach (var renderer in candidates)
            {
                foreach (var mat in renderer.materials)
                {
                    var result = ScoreCandidate(renderer, mat, query);
                    if (result.totalScore >= ThresholdLow)
                        results.Add(result);
                }
            }

            // 3. 스코어 내림차순 정렬
            results.Sort((a, b) => b.totalScore.CompareTo(a.totalScore));

            return results;
        }

        /// <summary>
        /// 검색 키별로 후보 렌더러를 수집한다.
        /// 하나라도 매칭되면 후보에 포함 (union).
        /// </summary>
        private HashSet<RendererEntry> GatherCandidates(MatchQuery query)
        {
            var candidates = new HashSet<RendererEntry>();

            // Geometry 후보
            if (query.indexCount.HasValue)
            {
                var geoResults = query.vertexCount.HasValue
                    ? _engine.SearchByGeometry(query.vertexCount.Value, query.indexCount.Value)
                    : _engine.SearchByIndexCount(query.indexCount.Value);
                foreach (var r in geoResults)
                    candidates.Add(r);
            }

            // Texture 후보
            if (query.textures != null && query.textures.Count > 0)
            {
                foreach (var sig in query.textures)
                {
                    var texResults = _engine.SearchByTextureWithFormatMapping(sig);
                    foreach (var tex in texResults)
                    {
                        var renderers = _engine.GetRenderersByMaterialId(tex.materialInstanceId);
                        foreach (var r in renderers)
                            candidates.Add(r);
                    }
                }
            }

            // Shader 후보
            if (!string.IsNullOrEmpty(query.shaderName))
            {
                var shaderResults = _engine.SearchByShaderName(query.shaderName);
                foreach (var shader in shaderResults)
                {
                    foreach (var matId in shader.materialInstanceIds)
                    {
                        var renderers = _engine.GetRenderersByMaterialId(matId);
                        foreach (var r in renderers)
                            candidates.Add(r);
                    }
                }
            }

            if (query.shaderKeywords != null && query.shaderKeywords.Count > 0)
            {
                var kwResults = _engine.SearchByKeywords(query.shaderKeywords);
                foreach (var shader in kwResults)
                {
                    foreach (var matId in shader.materialInstanceIds)
                    {
                        var renderers = _engine.GetRenderersByMaterialId(matId);
                        foreach (var r in renderers)
                            candidates.Add(r);
                    }
                }
            }

            // CB 후보
            if (query.cbFloats != null && query.cbFloats.Count > 0)
            {
                var matResults = _engine.SearchByFloatSet(query.cbFloats.ToArray());
                foreach (var mat in matResults)
                {
                    var renderers = _engine.GetRenderersByMaterialId(mat.instanceId);
                    foreach (var r in renderers)
                        candidates.Add(r);
                }
            }

            return candidates;
        }

        /// <summary>
        /// 후보 (Renderer + Material) 조합에 대해 각 카테고리별 스코어를 계산한다.
        /// </summary>
        private MatchResult ScoreCandidate(RendererEntry renderer, MaterialEntry mat, MatchQuery query)
        {
            var result = new MatchResult
            {
                renderer = renderer,
                material = mat
            };

            float totalWeight = 0f;
            float totalScore = 0f;

            // Geometry 스코어
            if (query.indexCount.HasValue && renderer.mesh != null)
            {
                totalWeight += WeightGeometry;
                bool idxMatch = renderer.mesh.indexCount == query.indexCount.Value;
                bool vtxMatch = !query.vertexCount.HasValue || renderer.mesh.vertexCount == query.vertexCount.Value;

                result.geometryScore = idxMatch && vtxMatch ? 1f : idxMatch ? 0.7f : 0f;
                totalScore += result.geometryScore * WeightGeometry;
            }

            // Texture 스코어
            if (query.textures != null && query.textures.Count > 0)
            {
                totalWeight += WeightTexture;
                result.textureScore = ScoreTextures(mat, query.textures, out result.slotMappings);
                totalScore += result.textureScore * WeightTexture;
            }

            // Variant 스코어
            if (query.textures != null && query.textures.Count > 0)
            {
                totalWeight += WeightVariant;
                result.variantScore = ScoreVariant(mat, query.textures, out result.variant);
                totalScore += result.variantScore * WeightVariant;
            }

            // Scalar 스코어
            if (query.cbFloats != null && query.cbFloats.Count > 0)
            {
                totalWeight += WeightScalar;
                result.scalarScore = ScoreScalars(mat, query.cbFloats, query.cbColors);
                totalScore += result.scalarScore * WeightScalar;
            }

            // Shader 스코어
            if (!string.IsNullOrEmpty(query.shaderName) || (query.shaderKeywords != null && query.shaderKeywords.Count > 0))
            {
                totalWeight += WeightShader;
                result.shaderScore = ScoreShader(mat, query.shaderName, query.shaderKeywords);
                totalScore += result.shaderScore * WeightShader;
            }

            // 가중치 정규화 (없는 카테고리 제외)
            result.totalScore = totalWeight > 0f ? totalScore / totalWeight : 0f;

            // 신뢰도 등급
            result.confidence = result.totalScore >= ThresholdHigh ? MatchConfidence.High
                : result.totalScore >= ThresholdMedium ? MatchConfidence.Medium
                : result.totalScore >= ThresholdLow ? MatchConfidence.Low
                : MatchConfidence.None;

            return result;
        }

        /// <summary>
        /// 텍스처 시그니처 매칭 스코어 계산.
        /// 각 RenderDoc 텍스처가 머티리얼의 어떤 슬롯에 대응하는지도 매핑.
        /// </summary>
        private float ScoreTextures(MaterialEntry mat, List<TextureSignature> queryTextures, out List<SlotMapping> mappings)
        {
            mappings = new List<SlotMapping>();
            if (mat.textures.Count == 0 || queryTextures.Count == 0)
                return 0f;

            int matchCount = 0;
            var usedSlots = new HashSet<int>();

            for (int i = 0; i < queryTextures.Count; i++)
            {
                var qSig = queryTextures[i];
                int bestSlot = -1;
                float bestScore = 0f;

                for (int j = 0; j < mat.textures.Count; j++)
                {
                    if (usedSlots.Contains(j)) continue;
                    if (mat.textures[j].textureType == "None") continue;

                    var tSig = mat.textures[j].signature;
                    float score = 0f;

                    if (tSig.width == qSig.width && tSig.height == qSig.height)
                        score += 0.5f;

                    if (TextureFormatMap.FormatsMatch(tSig.format, qSig.format))
                        score += 0.3f;

                    if (qSig.mipCount > 0 && tSig.mipCount == qSig.mipCount)
                        score += 0.2f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSlot = j;
                    }
                }

                if (bestSlot >= 0 && bestScore > 0.3f)
                {
                    usedSlots.Add(bestSlot);
                    matchCount++;
                    mappings.Add(new SlotMapping
                    {
                        rdSlot = i,
                        rdSignature = qSig,
                        unityPropertyName = mat.textures[bestSlot].propertyName,
                        unityAssetPath = mat.textures[bestSlot].assetPath
                    });
                }
            }

            return queryTextures.Count > 0 ? (float)matchCount / queryTextures.Count : 0f;
        }

        /// <summary>
        /// Variant 슬롯 패턴 매칭 스코어 계산.
        /// </summary>
        private float ScoreVariant(MaterialEntry mat, List<TextureSignature> queryTextures, out VariantEntry matchedVariant)
        {
            matchedVariant = null;

            if (string.IsNullOrEmpty(mat.variantKey))
                return 0f;

            var variant = _engine.SearchByVariantKey(mat.variantKey);
            if (variant == null)
                return 0f;

            matchedVariant = variant;

            // 쿼리 텍스처에서 real/dummy 패턴 추출
            var queryPatternKey = VariantTracker.BuildSlotPatternKeyFromSignatures(queryTextures);

            if (string.IsNullOrEmpty(queryPatternKey) || string.IsNullOrEmpty(variant.slotPatternKey))
                return 0f;

            // 정확히 일치하면 1.0
            if (queryPatternKey == variant.slotPatternKey)
                return 1f;

            // 부분 매칭: real/dummy 패턴의 일치 비율
            var queryParts = queryPatternKey.Split('|');
            var variantParts = variant.slotPatternKey.Split('|');

            int minLen = Mathf.Min(queryParts.Length, variantParts.Length);
            int matchCount = 0;

            for (int i = 0; i < minLen; i++)
            {
                // real/dummy 타입만 비교 (시그니처 세부사항은 무시)
                bool queryIsReal = queryParts[i].StartsWith("real");
                bool variantIsReal = variantParts[i].StartsWith("real");

                if (queryIsReal == variantIsReal)
                    matchCount++;
            }

            int maxLen = Mathf.Max(queryParts.Length, variantParts.Length);
            return maxLen > 0 ? (float)matchCount / maxLen : 0f;
        }

        /// <summary>
        /// CB float 값 매칭 스코어 계산.
        /// 머티리얼의 스칼라 프로퍼티 중 일치하는 비율.
        /// </summary>
        private float ScoreScalars(MaterialEntry mat, List<float> queryFloats, List<Color> queryColors)
        {
            int totalQueries = 0;
            int matchCount = 0;

            // Float 값 매칭
            if (queryFloats != null)
            {
                totalQueries += queryFloats.Count;
                foreach (var qv in queryFloats)
                {
                    foreach (var scalar in mat.scalars)
                    {
                        if (scalar.isTextureST) continue;

                        bool matched = false;
                        switch (scalar.type)
                        {
                            case ScalarType.Float:
                            case ScalarType.Range:
                                matched = Mathf.Abs(scalar.floatValue - qv) <= LookupEngine.FloatEpsilon;
                                break;
                            case ScalarType.Color:
                                matched = Mathf.Abs(scalar.colorValue.r - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.colorValue.g - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.colorValue.b - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.colorValue.a - qv) <= LookupEngine.FloatEpsilon;
                                break;
                            case ScalarType.Vector:
                                matched = Mathf.Abs(scalar.vectorValue.x - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.vectorValue.y - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.vectorValue.z - qv) <= LookupEngine.FloatEpsilon
                                    || Mathf.Abs(scalar.vectorValue.w - qv) <= LookupEngine.FloatEpsilon;
                                break;
                            case ScalarType.Int:
                                matched = Mathf.Abs(scalar.intValue - qv) <= LookupEngine.FloatEpsilon;
                                break;
                        }

                        if (matched)
                        {
                            matchCount++;
                            break;
                        }
                    }
                }
            }

            // Color 값 매칭
            if (queryColors != null)
            {
                totalQueries += queryColors.Count;
                foreach (var qc in queryColors)
                {
                    foreach (var scalar in mat.scalars)
                    {
                        if (scalar.type == ScalarType.Color)
                        {
                            if (Mathf.Abs(scalar.colorValue.r - qc.r) <= LookupEngine.FloatEpsilon
                                && Mathf.Abs(scalar.colorValue.g - qc.g) <= LookupEngine.FloatEpsilon
                                && Mathf.Abs(scalar.colorValue.b - qc.b) <= LookupEngine.FloatEpsilon
                                && Mathf.Abs(scalar.colorValue.a - qc.a) <= LookupEngine.FloatEpsilon)
                            {
                                matchCount++;
                                break;
                            }
                        }
                    }
                }
            }

            return totalQueries > 0 ? (float)matchCount / totalQueries : 0f;
        }

        /// <summary>
        /// 셰이더 이름/키워드 매칭 스코어 계산.
        /// </summary>
        private float ScoreShader(MaterialEntry mat, string queryShaderName, List<string> queryKeywords)
        {
            float score = 0f;
            float maxScore = 0f;

            // 셰이더 이름 매칭
            if (!string.IsNullOrEmpty(queryShaderName))
            {
                maxScore += 1f;
                if (string.Equals(mat.shaderName, queryShaderName, StringComparison.OrdinalIgnoreCase))
                    score += 1f;
                else if (mat.shaderName.IndexOf(queryShaderName, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 0.5f;
            }

            // 키워드 매칭
            if (queryKeywords != null && queryKeywords.Count > 0)
            {
                maxScore += 1f;
                if (mat.activeKeywords != null && mat.activeKeywords.Length > 0)
                {
                    var matKwSet = new HashSet<string>(mat.activeKeywords, StringComparer.OrdinalIgnoreCase);
                    int kwMatch = queryKeywords.Count(kw => matKwSet.Contains(kw));
                    score += (float)kwMatch / queryKeywords.Count;
                }
            }

            return maxScore > 0f ? score / maxScore : 0f;
        }
    }
}
