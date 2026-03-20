using System.Collections.Generic;
using NUnit.Framework;
using UGDB.Core;
using UGDB.Parser;
using UnityEngine;

namespace UGDB.Tests
{
    /// <summary>
    /// ClipboardParser 유닛 테스트.
    /// 계획서 2-3절의 모든 패턴(A~K)에 대한 검증.
    /// </summary>
    public class ClipboardParserTests
    {
        private ClipboardParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new ClipboardParser();
        }

        // ── 패턴 A: Resource 패널 ──

        [Test]
        public void PatternA_ResourcePanel_Standard()
        {
            var result = _parser.Parse("Texture2D 2048x2048 12 mips - BC7_UNORM");
            Assert.AreEqual(QueryType.Texture, result.queryType);
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(2048, result.textures[0].width);
            Assert.AreEqual(2048, result.textures[0].height);
            Assert.AreEqual(12, result.textures[0].mipCount);
            Assert.AreEqual("BC7_UNORM", result.textures[0].format);
        }

        [Test]
        public void PatternA_ResourcePanel_ASTC()
        {
            var result = _parser.Parse("Texture2D 512x512 10 mips - ASTC_6x6");
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(512, result.textures[0].width);
            Assert.AreEqual(512, result.textures[0].height);
            Assert.AreEqual(10, result.textures[0].mipCount);
            Assert.AreEqual("ASTC_6x6", result.textures[0].format);
        }

        [Test]
        public void PatternA_ResourcePanel_SingleMip()
        {
            var result = _parser.Parse("Texture2D 4096x4096 1 mip - D32_FLOAT");
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(4096, result.textures[0].width);
            Assert.AreEqual(1, result.textures[0].mipCount);
            Assert.AreEqual("D32_FLOAT", result.textures[0].format);
        }

        // ── 패턴 B: Pipeline State 슬롯 ──

        [Test]
        public void PatternB_PipelineSlot_Standard()
        {
            var result = _parser.Parse("0\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips");
            Assert.AreEqual(QueryType.Texture, result.queryType);
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(2048, result.textures[0].width);
            Assert.AreEqual(2048, result.textures[0].height);
            Assert.AreEqual("BC7_UNORM", result.textures[0].format);
            Assert.AreEqual(12, result.textures[0].mipCount);
            Assert.AreEqual(1, result.textureSlotIndices.Count);
            Assert.AreEqual(0, result.textureSlotIndices[0]);
        }

        [Test]
        public void PatternB_MultipleSlots()
        {
            var input = "0\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips\n" +
                         "1\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips\n" +
                         "2\tTexture2D\t512x512\tBC7_UNORM\t10 mips";
            var result = _parser.Parse(input);
            Assert.AreEqual(3, result.textures.Count);
            Assert.AreEqual(0, result.textureSlotIndices[0]);
            Assert.AreEqual(1, result.textureSlotIndices[1]);
            Assert.AreEqual(2, result.textureSlotIndices[2]);
        }

        // ── 패턴 C: 해상도만 ──

        [Test]
        public void PatternC_ResolutionOnly()
        {
            var result = _parser.Parse("2048x2048");
            Assert.AreEqual(QueryType.Texture, result.queryType);
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(2048, result.textures[0].width);
            Assert.AreEqual(2048, result.textures[0].height);
            Assert.IsNull(result.textures[0].format);
            Assert.AreEqual(0, result.textures[0].mipCount);
        }

        [Test]
        public void PatternC_SmallResolution()
        {
            var result = _parser.Parse("256x256");
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(256, result.textures[0].width);
        }

        // ── 패턴 D: DrawIndexed ──

        [Test]
        public void PatternD_DrawIndexed()
        {
            var result = _parser.Parse("DrawIndexed(34560, 1, 0, 0, 0)");
            Assert.AreEqual(QueryType.Geometry, result.queryType);
            Assert.AreEqual(34560, result.indexCount);
        }

        [Test]
        public void PatternD_DrawIndexed_LargeCount()
        {
            var result = _parser.Parse("DrawIndexed(120000, 1, 0, 0, 0)");
            Assert.AreEqual(120000, result.indexCount);
        }

        // ── 패턴 E: API Inspector IndexCount ──

        [Test]
        public void PatternE_IndexCount()
        {
            var result = _parser.Parse("DrawIndexed()\n  IndexCount: 34560\n  InstanceCount: 1");
            Assert.AreEqual(QueryType.Geometry, result.queryType);
            Assert.AreEqual(34560, result.indexCount);
        }

        [Test]
        public void PatternE_IndexCount_Spacing()
        {
            var result = _parser.Parse("IndexCount:  5400");
            Assert.AreEqual(5400, result.indexCount);
        }

        // ── 패턴 F: Draw() ──

        [Test]
        public void PatternF_Draw()
        {
            var result = _parser.Parse("Draw(1200)");
            Assert.AreEqual(QueryType.Geometry, result.queryType);
            Assert.AreEqual(1200, result.vertexCount);
        }

        [Test]
        public void PatternF_DrawNotIndexed()
        {
            // DrawIndexed는 패턴 D가 처리해야 함
            var result = _parser.Parse("DrawIndexed(34560, 1, 0, 0, 0)");
            Assert.IsNull(result.vertexCount);
            Assert.AreEqual(34560, result.indexCount);
        }

        // ── 패턴 G: CB 구조체 뷰 ──

        [Test]
        public void PatternG_Float4()
        {
            var result = _parser.Parse("float4  1.000  0.950  0.900  1.000");
            Assert.AreEqual(QueryType.ConstantBuffer, result.queryType);
            Assert.AreEqual(1, result.cbVectors.Count);
            Assert.AreEqual(1.0f, result.cbVectors[0].x, 0.001f);
            Assert.AreEqual(0.95f, result.cbVectors[0].y, 0.001f);
            Assert.AreEqual(0.9f, result.cbVectors[0].z, 0.001f);
            Assert.AreEqual(1.0f, result.cbVectors[0].w, 0.001f);
        }

        [Test]
        public void PatternG_Float()
        {
            var result = _parser.Parse("float   0.730");
            Assert.AreEqual(QueryType.ConstantBuffer, result.queryType);
            Assert.AreEqual(1, result.cbFloats.Count);
            Assert.AreEqual(0.73f, result.cbFloats[0], 0.001f);
        }

        [Test]
        public void PatternG_MultipleLines()
        {
            var input = "float4  1.000  0.950  0.900  1.000\n" +
                         "float   0.730\n" +
                         "float   1.000";
            var result = _parser.Parse(input);
            Assert.AreEqual(1, result.cbVectors.Count);
            Assert.AreEqual(2, result.cbFloats.Count);
            Assert.AreEqual(0.73f, result.cbFloats[0], 0.001f);
            Assert.AreEqual(1.0f, result.cbFloats[1], 0.001f);
        }

        // ── 패턴 H: CB 오프셋 뷰 ──

        [Test]
        public void PatternH_SingleValue()
        {
            var result = _parser.Parse("[16]: 0.73");
            Assert.AreEqual(QueryType.ConstantBuffer, result.queryType);
            Assert.IsTrue(result.cbOffsetValues.ContainsKey(16));
            Assert.AreEqual(0.73f, result.cbOffsetValues[16], 0.001f);
        }

        [Test]
        public void PatternH_MultipleValues()
        {
            var result = _parser.Parse("[0]: 1, 0.95, 0.9, 1");
            Assert.AreEqual(4, result.cbFloats.Count);
            Assert.IsTrue(result.cbOffsetValues.ContainsKey(0));
            Assert.IsTrue(result.cbOffsetValues.ContainsKey(4));
            Assert.IsTrue(result.cbOffsetValues.ContainsKey(8));
            Assert.IsTrue(result.cbOffsetValues.ContainsKey(12));
            Assert.AreEqual(1.0f, result.cbOffsetValues[0], 0.001f);
            Assert.AreEqual(0.95f, result.cbOffsetValues[4], 0.001f);
        }

        [Test]
        public void PatternH_MultipleLines()
        {
            var input = "[0]:  1, 0.95, 0.9, 1\n[16]: 0.73\n[20]: 1";
            var result = _parser.Parse(input);
            Assert.AreEqual(6, result.cbFloats.Count);
            Assert.AreEqual(0.73f, result.cbOffsetValues[16], 0.001f);
            Assert.AreEqual(1.0f, result.cbOffsetValues[20], 0.001f);
        }

        // ── 패턴 I: CB raw hex ──

        [Test]
        public void PatternI_HexToFloat()
        {
            // 3F800000 = 1.0f
            var result = _parser.Parse("00000000: 3F800000 3F733333");
            Assert.AreEqual(QueryType.ConstantBuffer, result.queryType);
            Assert.AreEqual(2, result.cbFloats.Count);
            Assert.AreEqual(1.0f, result.cbFloats[0], 0.001f);
            Assert.AreEqual(0.95f, result.cbFloats[1], 0.01f);
        }

        [Test]
        public void PatternI_HexMultipleLines()
        {
            var input = "00000000: 3F800000 3F733333 3F666666 3F800000\n" +
                         "00000010: 3F3AE148";
            var result = _parser.Parse(input);
            Assert.AreEqual(5, result.cbFloats.Count);
            Assert.AreEqual(1.0f, result.cbFloats[0], 0.001f);
            // 3F3AE148 ≈ 0.73
            Assert.AreEqual(0.73f, result.cbFloats[4], 0.01f);
        }

        // ── 패턴 J: 키워드 ──

        [Test]
        public void PatternJ_Keywords()
        {
            var result = _parser.Parse("Keywords: _NORMALMAP _EMISSION");
            Assert.AreEqual(QueryType.Shader, result.queryType);
            Assert.IsTrue(result.shaderKeywords.Contains("_NORMALMAP"));
            Assert.IsTrue(result.shaderKeywords.Contains("_EMISSION"));
        }

        [Test]
        public void PatternJ_Keywords_SingleLine()
        {
            var result = _parser.Parse("_NORMALMAP _EMISSION _ALPHATEST_ON");
            Assert.AreEqual(3, result.shaderKeywords.Count);
        }

        // ── 패턴 K: 셰이더 이름 ──

        [Test]
        public void PatternK_ShaderName()
        {
            var result = _parser.Parse("Custom/CharacterPBR");
            Assert.AreEqual(QueryType.Shader, result.queryType);
            Assert.AreEqual("Custom/CharacterPBR", result.shaderName);
        }

        [Test]
        public void PatternK_ShaderName_Deep()
        {
            var result = _parser.Parse("Custom/Effects/Dissolve");
            Assert.AreEqual("Custom/Effects/Dissolve", result.shaderName);
        }

        // ── 복합 패턴 ──

        [Test]
        public void Composite_TextureAndDrawCall()
        {
            var input = "DrawIndexed(34560, 1, 0, 0, 0)\n" +
                         "Texture2D 2048x2048 12 mips - BC7_UNORM";
            var result = _parser.Parse(input);
            Assert.AreEqual(QueryType.Composite, result.queryType);
            Assert.AreEqual(34560, result.indexCount);
            Assert.AreEqual(1, result.textures.Count);
            Assert.AreEqual(2048, result.textures[0].width);
        }

        [Test]
        public void Composite_FullPipelineState()
        {
            var input = "0\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips\n" +
                         "1\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips\n" +
                         "2\tTexture2D\t512x512\tBC7_UNORM\t10 mips\n" +
                         "float4  1.000  0.950  0.900  1.000\n" +
                         "float   0.730";
            var result = _parser.Parse(input);
            Assert.AreEqual(QueryType.Composite, result.queryType);
            Assert.AreEqual(3, result.textures.Count);
            Assert.AreEqual(1, result.cbVectors.Count);
            Assert.AreEqual(1, result.cbFloats.Count);
        }

        // ── 에지 케이스 ──

        [Test]
        public void Empty_Input()
        {
            var result = _parser.Parse("");
            Assert.AreEqual(QueryType.Unknown, result.queryType);
            Assert.AreEqual(0, result.textures.Count);
        }

        [Test]
        public void Null_Input()
        {
            var result = _parser.Parse(null);
            Assert.AreEqual(QueryType.Unknown, result.queryType);
        }

        [Test]
        public void Unrecognized_Text()
        {
            var result = _parser.Parse("This is some random text that shouldn't match.");
            Assert.AreEqual(QueryType.Unknown, result.queryType);
        }

        [Test]
        public void Summary_TextureQuery()
        {
            var result = _parser.Parse("Texture2D 2048x2048 12 mips - BC7_UNORM");
            var summary = result.GetSummary();
            Assert.IsTrue(summary.Contains("Texture"));
        }

        // ── FormatAliases ──

        [Test]
        public void FormatAliases_BC7_to_ASTC()
        {
            Assert.IsTrue(ClipboardPatterns.FormatAliases.ContainsKey("BC7_UNORM"));
            Assert.AreEqual("ASTC_6x6", ClipboardPatterns.FormatAliases["BC7_UNORM"]);
        }

        [Test]
        public void FormatAliases_DepthFormat()
        {
            Assert.IsTrue(ClipboardPatterns.FormatAliases.ContainsKey("D32_FLOAT"));
            Assert.AreEqual("D32_SFLOAT", ClipboardPatterns.FormatAliases["D32_FLOAT"]);
        }
    }
}
