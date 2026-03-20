using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UGDB.Parser
{
    /// <summary>
    /// RenderDoc에서 복사되는 텍스트의 패턴(A~K)을 정규식으로 정의한다.
    /// 계획서 2-1절 기반.
    /// </summary>
    public static class ClipboardPatterns
    {
        // ── 텍스처 관련 ──

        /// <summary>
        /// 패턴 A: Resource 패널 — "Texture2D 2048x2048 12 mips - BC7_UNORM"
        /// Groups: 1=width, 2=height, 3=mipCount, 4=format
        /// </summary>
        public static readonly Regex PatternA = new Regex(
            @"(\d+)x(\d+)\s+(\d+)\s*mips?\s*[-–]\s*(\w+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 B: Pipeline State 슬롯 (탭 구분) — "0\tTexture2D\t2048x2048\tBC7_UNORM\t12 mips"
        /// Groups: 1=slot, 2=width, 3=height, 4=format, 5=mipCount
        /// </summary>
        public static readonly Regex PatternB = new Regex(
            @"(\d+)\t\w+\t(\d+)x(\d+)\t(\w+)\t(\d+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 C: 해상도만 — "2048x2048"
        /// Groups: 1=width, 2=height
        /// </summary>
        public static readonly Regex PatternC = new Regex(
            @"^(\d+)x(\d+)$",
            RegexOptions.Compiled);

        // ── 드로우콜 관련 ──

        /// <summary>
        /// 패턴 D: Event Browser — "DrawIndexed(34560, 1, 0, 0, 0)"
        /// Groups: 1=indexCount
        /// </summary>
        public static readonly Regex PatternD = new Regex(
            @"DrawIndexed\((\d+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 E: API Inspector — "IndexCount: 34560"
        /// Groups: 1=indexCount
        /// </summary>
        public static readonly Regex PatternE = new Regex(
            @"IndexCount:\s*(\d+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 F: Draw call 이름 — "Draw(1200)"
        /// Groups: 1=vertexCount
        /// Note: DrawIndexed를 먼저 체크해야 함 (D 패턴 우선)
        /// </summary>
        public static readonly Regex PatternF = new Regex(
            @"(?<!DrawIndexed|Indexed)\bDraw\((\d+)\)",
            RegexOptions.Compiled);

        // ── Constant Buffer 관련 ──

        /// <summary>
        /// 패턴 G-float4: CB 구조체 뷰 — "float4  1.000  0.950  0.900  1.000"
        /// Groups: 1=v1, 2=v2, 3=v3, 4=v4
        /// </summary>
        public static readonly Regex PatternG_Float4 = new Regex(
            @"float4\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 G-float: CB 구조체 뷰 — "float   0.730"
        /// Groups: 1=value
        /// </summary>
        public static readonly Regex PatternG_Float = new Regex(
            @"(?<!\w)float\s+([-\d.]+)\s*$",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 H: CB 오프셋 뷰 — "[0]: 1, 0.95, 0.9, 1" 또는 "[16]: 0.73"
        /// Groups: 1=offset, 2=values (콤마 구분)
        /// </summary>
        public static readonly Regex PatternH = new Regex(
            @"\[(\d+)\]:\s*([-\d.,\s]+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 I: CB raw hex — "00000000: 3F800000 3F733333 ..."
        /// Groups: 1=offset(hex), 2=hex values (공백 구분)
        /// </summary>
        public static readonly Regex PatternI = new Regex(
            @"([0-9A-Fa-f]{8}):\s+((?:[0-9A-Fa-f]{8}\s*)+)",
            RegexOptions.Compiled);

        // ── 셰이더 관련 ──

        /// <summary>
        /// 패턴 J: 키워드 — "_NORMALMAP", "_EMISSION" 등 언더스코어로 시작하는 대문자 키워드
        /// Groups: 전체 매치
        /// </summary>
        public static readonly Regex PatternJ = new Regex(
            @"_[A-Z][A-Z_0-9]+",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 J-prefix: "Keywords:" 접두사 감지
        /// </summary>
        public static readonly Regex PatternJ_Prefix = new Regex(
            @"(?:Keywords|keywords|KEYWORDS)\s*:\s*(.+)",
            RegexOptions.Compiled);

        /// <summary>
        /// 패턴 K: 셰이더 이름 — "Custom/CharacterPBR" (슬래시 포함 경로)
        /// Groups: 전체 매치
        /// </summary>
        public static readonly Regex PatternK = new Regex(
            @"\b(\w+(?:/[\w]+)+)\b",
            RegexOptions.Compiled);

        // ── 포맷 별칭 매핑 (DX11 ↔ Vulkan ↔ Unity 간 대응) ──

        /// <summary>
        /// RenderDoc 포맷 이름 간 별칭 매핑.
        /// Key/Value 모두 대문자로 정규화된 상태.
        /// </summary>
        public static readonly Dictionary<string, string> FormatAliases = new Dictionary<string, string>
        {
            // BC 계열 (DX11) ↔ ASTC 계열 (모바일)
            { "BC7_UNORM", "ASTC_6x6" },
            { "ASTC_6X6", "BC7_UNORM" },
            { "BC7_UNORM_SRGB", "ASTC_6x6_SRGB" },
            { "ASTC_6X6_SRGB", "BC7_UNORM_SRGB" },

            { "BC3_UNORM", "ASTC_4x4" },
            { "ASTC_4X4", "BC3_UNORM" },
            { "BC3_UNORM_SRGB", "ASTC_4x4_SRGB" },
            { "ASTC_4X4_SRGB", "BC3_UNORM_SRGB" },

            { "BC1_UNORM", "ETC2_RGB" },
            { "ETC2_RGB", "BC1_UNORM" },
            { "BC1_UNORM_SRGB", "ETC2_RGB_SRGB" },
            { "ETC2_RGB_SRGB", "BC1_UNORM_SRGB" },

            // 노멀맵
            { "BC5_UNORM", "ASTC_4x4" },
            { "BC5_SNORM", "EAC_RG11_SNORM" },

            // 단일 채널
            { "BC4_UNORM", "EAC_R11" },
            { "EAC_R11", "BC4_UNORM" },

            // 깊이/스텐실
            { "D32_FLOAT", "D32_SFLOAT" },
            { "D32_SFLOAT", "D32_FLOAT" },
            { "D24_UNORM_S8_UINT", "D24S8" },
            { "D24S8", "D24_UNORM_S8_UINT" },

            // HDR
            { "R16G16B16A16_FLOAT", "RGBA16F" },
            { "RGBA16F", "R16G16B16A16_FLOAT" },
            { "R32G32B32A32_FLOAT", "RGBA32F" },
            { "RGBA32F", "R32G32B32A32_FLOAT" },

            // 일반
            { "R8G8B8A8_UNORM", "RGBA8" },
            { "RGBA8", "R8G8B8A8_UNORM" },
            { "R8G8B8A8_UNORM_SRGB", "RGBA8_SRGB" },
            { "RGBA8_SRGB", "R8G8B8A8_UNORM_SRGB" },
            { "B8G8R8A8_UNORM", "BGRA8" },
            { "BGRA8", "B8G8R8A8_UNORM" },
        };
    }
}
