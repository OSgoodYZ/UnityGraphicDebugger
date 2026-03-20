using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UGDB.Core
{
    /// <summary>
    /// 머티리얼별 활성 키워드 조합으로 셰이더 variant를 식별하고,
    /// 각 variant의 텍스처 슬롯 패턴(real/dummy)을 수집한다.
    /// RenderDoc에서 텍스처 슬롯 테이블을 붙여넣었을 때
    /// 어떤 variant인지 특정하는 데 사용된다.
    /// </summary>
    public static class VariantTracker
    {
        // Unity 기본 텍스처 이름 (dummy 판별용)
        private static readonly HashSet<string> s_DefaultTextureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "", "unity_default", "UnityWhite", "UnityBlack",
            "UnityNormalMap", "UnityGray", "unity_Lightmap",
            "unity_ShadowMask", "unity_DynamicLightmap"
        };

        /// <summary>
        /// SceneSnapshotData에 수집된 머티리얼 정보를 기반으로
        /// 셰이더 variant를 추적하고 data.variants에 추가한다.
        /// SceneSnapshot.Capture() 내부에서 호출된다.
        /// </summary>
        public static void TrackVariants(SceneSnapshotData data)
        {
            // shaderName -> (variantKey -> VariantEntry)
            var variantMap = new Dictionary<string, Dictionary<string, VariantEntry>>();

            foreach (var renderer in data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (string.IsNullOrEmpty(mat.shaderName) || mat.shaderName == "None")
                        continue;

                    var variantKey = BuildVariantKey(mat.shaderName, mat.activeKeywords);
                    mat.variantKey = variantKey;

                    if (!variantMap.TryGetValue(mat.shaderName, out var shaderVariants))
                    {
                        shaderVariants = new Dictionary<string, VariantEntry>();
                        variantMap[mat.shaderName] = shaderVariants;
                    }

                    if (!shaderVariants.TryGetValue(variantKey, out var variant))
                    {
                        variant = new VariantEntry
                        {
                            shaderName = mat.shaderName,
                            activeKeywords = mat.activeKeywords != null
                                ? (string[])mat.activeKeywords.Clone()
                                : Array.Empty<string>(),
                            variantKey = variantKey
                        };

                        // 텍스처 슬롯 패턴 빌드
                        variant.textureSlotPattern = BuildSlotPattern(mat);
                        variant.slotPatternKey = BuildSlotPatternKey(variant.textureSlotPattern);

                        // 스칼라 패턴 (variant의 대표 스칼라 값 세트)
                        variant.scalarPattern = new List<ScalarEntry>(mat.scalars);

                        shaderVariants[variantKey] = variant;
                    }

                    // 이 variant를 사용하는 머티리얼 등록
                    if (!variant.materialInstanceIds.Contains(mat.instanceId))
                        variant.materialInstanceIds.Add(mat.instanceId);
                }
            }

            // 결과를 data에 반영
            foreach (var shaderVariants in variantMap.Values)
            {
                foreach (var variant in shaderVariants.Values)
                {
                    data.variants.Add(variant);
                }
            }

            // 셰이더 엔트리에 variant key 목록 연결
            foreach (var shader in data.shaders)
            {
                if (variantMap.TryGetValue(shader.name, out var variants))
                {
                    shader.variantKeys = new List<string>(variants.Keys);
                }
            }
        }

        /// <summary>
        /// 셰이더 이름 + 정렬된 키워드 조합으로 variant 식별 키를 생성한다.
        /// 예: "Custom/CharacterPBR|_EMISSION|_NORMALMAP"
        /// </summary>
        public static string BuildVariantKey(string shaderName, string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
                return shaderName;

            var sorted = new string[keywords.Length];
            Array.Copy(keywords, sorted, keywords.Length);
            Array.Sort(sorted, StringComparer.Ordinal);

            var sb = new StringBuilder(shaderName);
            foreach (var kw in sorted)
            {
                sb.Append('|');
                sb.Append(kw);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 머티리얼의 텍스처 프로퍼티별로 real/dummy 상태를 판별하여
        /// 슬롯 패턴 목록을 생성한다.
        /// </summary>
        public static List<SlotState> BuildSlotPattern(MaterialEntry mat)
        {
            var pattern = new List<SlotState>();

            for (int i = 0; i < mat.textures.Count; i++)
            {
                var tex = mat.textures[i];
                bool isReal = IsRealTexture(tex);

                pattern.Add(new SlotState
                {
                    slotIndex = i,
                    propertyName = tex.propertyName,
                    isRealTexture = isReal,
                    signature = isReal ? tex.signature : default
                });
            }

            return pattern;
        }

        /// <summary>
        /// 텍스처가 실제 사용 중인 텍스처인지 판별한다.
        /// null, 4x4 이하, Unity 기본 텍스처 이름이면 dummy로 판정.
        /// </summary>
        public static bool IsRealTexture(TextureEntry tex)
        {
            // 텍스처가 없으면 dummy
            if (tex.textureType == "None" || string.IsNullOrEmpty(tex.textureType))
                return false;

            // 4x4 이하는 Unity 기본 텍스처 (white, black, bump 등)
            if (tex.signature.width <= 4 && tex.signature.height <= 4)
                return false;

            // 에셋 경로에서 파일명 추출하여 기본 텍스처 이름 체크
            if (!string.IsNullOrEmpty(tex.assetPath))
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(tex.assetPath);
                if (s_DefaultTextureNames.Contains(fileName))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 슬롯 패턴을 검색 가능한 문자열 키로 변환한다.
        /// 예: "real:2048x2048_BC7|real:2048x2048_BC7|dummy"
        /// </summary>
        public static string BuildSlotPatternKey(List<SlotState> pattern)
        {
            if (pattern == null || pattern.Count == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < pattern.Count; i++)
            {
                if (i > 0) sb.Append('|');

                var slot = pattern[i];
                if (slot.isRealTexture)
                {
                    sb.Append("real:");
                    sb.Append(slot.signature.width);
                    sb.Append('x');
                    sb.Append(slot.signature.height);
                    sb.Append('_');
                    sb.Append(slot.signature.format);
                }
                else
                {
                    sb.Append("dummy");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// RenderDoc에서 복사한 텍스처 슬롯 정보로부터 슬롯 패턴 키를 생성한다.
        /// real/dummy 판별은 해상도 기준: 4x4 이하면 dummy.
        /// </summary>
        public static string BuildSlotPatternKeyFromSignatures(List<TextureSignature> signatures)
        {
            if (signatures == null || signatures.Count == 0)
                return "";

            var sb = new StringBuilder();
            for (int i = 0; i < signatures.Count; i++)
            {
                if (i > 0) sb.Append('|');

                var sig = signatures[i];
                if (sig.width > 4 && sig.height > 4)
                {
                    sb.Append("real:");
                    sb.Append(sig.width);
                    sb.Append('x');
                    sb.Append(sig.height);
                    sb.Append('_');
                    sb.Append(sig.format);
                }
                else
                {
                    sb.Append("dummy");
                }
            }

            return sb.ToString();
        }
    }
}
