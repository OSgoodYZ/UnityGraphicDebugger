using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UGDB.Core;
using UnityEngine;

namespace UGDB.Parser
{
    /// <summary>
    /// RenderDoc에서 복사한 텍스트를 자동 판별하여 검색 키를 추출한다.
    /// 계획서 2-2절 기반.
    /// </summary>
    public class ClipboardParser
    {
        /// <summary>
        /// 메인 파싱 함수: 클립보드 텍스트를 받아 ParseResult를 반환한다.
        /// </summary>
        public ParseResult Parse(string clipboardText)
        {
            if (string.IsNullOrEmpty(clipboardText))
                return new ParseResult();

            var result = new ParseResult();
            result.rawText = clipboardText;

            var lines = clipboardText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (TryParseDrawCall(line, result)) continue;
                if (TryParseTexture(line, result)) continue;
                if (TryParseCBStruct(line, result)) continue;
                if (TryParseCBOffset(line, result)) continue;
                if (TryParseCBHex(line, result)) continue;
                if (TryParseKeywords(line, result)) continue;
                if (TryParseShaderName(line, result)) continue;
            }

            result.queryType = DetermineQueryType(result);
            return result;
        }

        // ── 텍스처 패턴 파싱 (A, B, C) ──

        private bool TryParseTexture(string line, ParseResult result)
        {
            // 패턴 B: 탭 구분 슬롯 (우선)
            var matchB = ClipboardPatterns.PatternB.Match(line);
            if (matchB.Success)
            {
                int slot = int.Parse(matchB.Groups[1].Value);
                int w = int.Parse(matchB.Groups[2].Value);
                int h = int.Parse(matchB.Groups[3].Value);
                string fmt = matchB.Groups[4].Value;
                int mips = int.Parse(matchB.Groups[5].Value);

                var sig = new TextureSignature(w, h, fmt, mips);
                result.textures.Add(sig);
                result.textureSlotIndices.Add(slot);
                return true;
            }

            // 패턴 A: Resource 패널
            var matchA = ClipboardPatterns.PatternA.Match(line);
            if (matchA.Success)
            {
                int w = int.Parse(matchA.Groups[1].Value);
                int h = int.Parse(matchA.Groups[2].Value);
                int mips = int.Parse(matchA.Groups[3].Value);
                string fmt = matchA.Groups[4].Value;

                var sig = new TextureSignature(w, h, fmt, mips);
                result.textures.Add(sig);
                return true;
            }

            // 패턴 C: 해상도만
            var matchC = ClipboardPatterns.PatternC.Match(line);
            if (matchC.Success)
            {
                int w = int.Parse(matchC.Groups[1].Value);
                int h = int.Parse(matchC.Groups[2].Value);

                var sig = new TextureSignature(w, h, null, 0);
                result.textures.Add(sig);
                return true;
            }

            return false;
        }

        // ── 드로우콜 패턴 파싱 (D, E, F) ──

        private bool TryParseDrawCall(string line, ParseResult result)
        {
            // 패턴 D: DrawIndexed(34560, 1, 0, 0, 0)
            var matchD = ClipboardPatterns.PatternD.Match(line);
            if (matchD.Success)
            {
                result.indexCount = int.Parse(matchD.Groups[1].Value);
                return true;
            }

            // 패턴 E: IndexCount: 34560
            var matchE = ClipboardPatterns.PatternE.Match(line);
            if (matchE.Success)
            {
                result.indexCount = int.Parse(matchE.Groups[1].Value);
                return true;
            }

            // 패턴 F: Draw(1200) — DrawIndexed가 아닌 것만
            var matchF = ClipboardPatterns.PatternF.Match(line);
            if (matchF.Success)
            {
                result.vertexCount = int.Parse(matchF.Groups[1].Value);
                return true;
            }

            return false;
        }

        // ── CB 패턴 파싱 (G, H, I) ──

        private bool TryParseCBStruct(string line, ParseResult result)
        {
            // 패턴 G-float4: "float4  1.000  0.950  0.900  1.000"
            var match4 = ClipboardPatterns.PatternG_Float4.Match(line);
            if (match4.Success)
            {
                float x = float.Parse(match4.Groups[1].Value, CultureInfo.InvariantCulture);
                float y = float.Parse(match4.Groups[2].Value, CultureInfo.InvariantCulture);
                float z = float.Parse(match4.Groups[3].Value, CultureInfo.InvariantCulture);
                float w = float.Parse(match4.Groups[4].Value, CultureInfo.InvariantCulture);
                result.cbVectors.Add(new Vector4(x, y, z, w));
                return true;
            }

            // 패턴 G-float: "float   0.730"
            var match1 = ClipboardPatterns.PatternG_Float.Match(line);
            if (match1.Success)
            {
                float val = float.Parse(match1.Groups[1].Value, CultureInfo.InvariantCulture);
                result.cbFloats.Add(val);
                return true;
            }

            return false;
        }

        private bool TryParseCBOffset(string line, ParseResult result)
        {
            // 패턴 H: "[0]: 1, 0.95, 0.9, 1" 또는 "[16]: 0.73"
            var match = ClipboardPatterns.PatternH.Match(line);
            if (match.Success)
            {
                int offset = int.Parse(match.Groups[1].Value);
                string valuesStr = match.Groups[2].Value.Trim();
                var parts = valuesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    float val;
                    if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        int currentOffset = offset + i * 4;
                        result.cbOffsetValues[currentOffset] = val;
                        result.cbFloats.Add(val);
                    }
                }
                return true;
            }

            return false;
        }

        private bool TryParseCBHex(string line, ParseResult result)
        {
            // 패턴 I: "00000000: 3F800000 3F733333 ..."
            var match = ClipboardPatterns.PatternI.Match(line);
            if (match.Success)
            {
                string hexValues = match.Groups[2].Value.Trim();
                var hexParts = hexValues.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var hex in hexParts)
                {
                    if (hex.Length == 8)
                    {
                        uint intBits;
                        if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out intBits))
                        {
                            float val = BitConverter.ToSingle(BitConverter.GetBytes(intBits), 0);
                            result.cbFloats.Add(val);
                        }
                    }
                }
                return true;
            }

            return false;
        }

        // ── 셰이더 패턴 파싱 (J, K) ──

        private bool TryParseKeywords(string line, ParseResult result)
        {
            // "Keywords: _NORMALMAP _EMISSION" 형태 우선 처리
            var prefixMatch = ClipboardPatterns.PatternJ_Prefix.Match(line);
            if (prefixMatch.Success)
            {
                var kwMatches = ClipboardPatterns.PatternJ.Matches(prefixMatch.Groups[1].Value);
                foreach (Match kw in kwMatches)
                {
                    string keyword = kw.Value;
                    if (!result.shaderKeywords.Contains(keyword))
                        result.shaderKeywords.Add(keyword);
                }
                return kwMatches.Count > 0;
            }

            // 패턴 J: 줄에서 _UPPERCASE_KEYWORD 추출 (최소 2개 이상이면 키워드 라인으로 간주)
            var matches = ClipboardPatterns.PatternJ.Matches(line);
            if (matches.Count >= 2)
            {
                foreach (Match m in matches)
                {
                    string keyword = m.Value;
                    if (!result.shaderKeywords.Contains(keyword))
                        result.shaderKeywords.Add(keyword);
                }
                return true;
            }

            return false;
        }

        private bool TryParseShaderName(string line, ParseResult result)
        {
            // 패턴 K: "Custom/CharacterPBR" 형태의 슬래시 포함 경로
            var match = ClipboardPatterns.PatternK.Match(line);
            if (match.Success)
            {
                string name = match.Groups[1].Value;
                // 이미 다른 패턴으로 파싱된 경우 스킵 (URL 등 오탐 방지)
                if (name.Contains("http") || name.Contains("www"))
                    return false;

                result.shaderName = name;
                return true;
            }

            return false;
        }

        // ── QueryType 결정 ──

        private QueryType DetermineQueryType(ParseResult result)
        {
            int categories = 0;
            if (result.textures.Count > 0) categories++;
            if (result.indexCount.HasValue || result.vertexCount.HasValue) categories++;
            if (result.cbFloats.Count > 0 || result.cbVectors.Count > 0 || result.cbOffsetValues.Count > 0) categories++;
            if (result.shaderKeywords.Count > 0 || !string.IsNullOrEmpty(result.shaderName)) categories++;

            if (categories == 0) return QueryType.Unknown;
            if (categories >= 2) return QueryType.Composite;

            if (result.textures.Count > 0) return QueryType.Texture;
            if (result.indexCount.HasValue || result.vertexCount.HasValue) return QueryType.Geometry;
            if (result.cbFloats.Count > 0 || result.cbVectors.Count > 0 || result.cbOffsetValues.Count > 0) return QueryType.ConstantBuffer;
            if (result.shaderKeywords.Count > 0 || !string.IsNullOrEmpty(result.shaderName)) return QueryType.Shader;

            return QueryType.Unknown;
        }
    }

    /// <summary>
    /// 클립보드 파싱 결과. 추출된 키만 채워진다.
    /// </summary>
    [Serializable]
    public class ParseResult
    {
        public QueryType queryType;
        public string rawText;

        // 텍스처
        public List<TextureSignature> textures = new List<TextureSignature>();
        public List<int> textureSlotIndices = new List<int>();

        // 드로우콜
        public int? indexCount;
        public int? vertexCount;

        // Constant Buffer
        public List<float> cbFloats = new List<float>();
        public List<Vector4> cbVectors = new List<Vector4>();
        public Dictionary<int, float> cbOffsetValues = new Dictionary<int, float>();

        // 셰이더
        public List<string> shaderKeywords = new List<string>();
        public string shaderName;

        /// <summary>
        /// 추출된 데이터의 요약 문자열을 반환한다.
        /// </summary>
        public string GetSummary()
        {
            var parts = new List<string>();
            if (textures.Count > 0)
                parts.Add(string.Format("Texture x{0}", textures.Count));
            if (indexCount.HasValue)
                parts.Add(string.Format("IndexCount: {0}", indexCount.Value));
            if (vertexCount.HasValue)
                parts.Add(string.Format("VertexCount: {0}", vertexCount.Value));
            if (cbFloats.Count > 0)
                parts.Add(string.Format("CB Float x{0}", cbFloats.Count));
            if (cbVectors.Count > 0)
                parts.Add(string.Format("CB Vector x{0}", cbVectors.Count));
            if (shaderKeywords.Count > 0)
                parts.Add(string.Format("Keywords: {0}", string.Join(", ", shaderKeywords)));
            if (!string.IsNullOrEmpty(shaderName))
                parts.Add(string.Format("Shader: {0}", shaderName));

            if (parts.Count == 0)
                return "인식된 패턴 없음";

            return string.Format("{0} ({1})", queryType, string.Join(" | ", parts));
        }
    }

    /// <summary>
    /// 검색 쿼리 타입.
    /// </summary>
    public enum QueryType
    {
        Unknown,
        Texture,
        Geometry,
        ConstantBuffer,
        Shader,
        Composite
    }
}
