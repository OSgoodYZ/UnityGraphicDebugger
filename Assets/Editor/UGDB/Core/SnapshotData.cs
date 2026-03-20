using System;
using System.Collections.Generic;
using UnityEngine;

namespace UGDB.Core
{
    #region Texture

    /// <summary>
    /// 텍스처의 GPU 레벨 시그니처 (해상도 + 포맷 + 밉맵).
    /// RenderDoc에서 보이는 정보와 매칭하기 위한 핵심 키.
    /// </summary>
    [Serializable]
    public struct TextureSignature : IEquatable<TextureSignature>
    {
        public int width;
        public int height;
        public string format;
        public int mipCount;

        public TextureSignature(int width, int height, string format, int mipCount)
        {
            this.width = width;
            this.height = height;
            this.format = format;
            this.mipCount = mipCount;
        }

        public bool MatchesResolutionAndFormat(TextureSignature other)
        {
            return width == other.width && height == other.height
                && string.Equals(format, other.format, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(TextureSignature other)
        {
            return width == other.width && height == other.height
                && string.Equals(format, other.format, StringComparison.OrdinalIgnoreCase)
                && mipCount == other.mipCount;
        }

        public override bool Equals(object obj) => obj is TextureSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + width;
                hash = hash * 31 + height;
                hash = hash * 31 + (format != null ? format.ToLowerInvariant().GetHashCode() : 0);
                hash = hash * 31 + mipCount;
                return hash;
            }
        }

        public override string ToString() => $"{width}x{height} {format} Mips:{mipCount}";

        public static bool operator ==(TextureSignature a, TextureSignature b) => a.Equals(b);
        public static bool operator !=(TextureSignature a, TextureSignature b) => !a.Equals(b);
    }

    [Serializable]
    public class TextureEntry
    {
        public string propertyName;
        public int nameID;
        public string assetPath;
        public TextureSignature signature;
        public string filterMode;
        public string wrapMode;
        public long memorySizeBytes;
        public string textureType; // Texture2D, RenderTexture, Cubemap, None

        // 역참조
        public string materialName;
        public int materialInstanceId;
    }

    #endregion

    #region Scalar

    public enum ScalarType
    {
        Float,
        Range,
        Color,
        Vector,
        Int
    }

    [Serializable]
    public class ScalarEntry
    {
        public string propertyName;
        public int nameID;
        public ScalarType type;
        public float floatValue;
        public Color colorValue;
        public Vector4 vectorValue;
        public int intValue;

        // Texture ST (offset/scale)
        public Vector2 textureOffset;
        public Vector2 textureScale;
        public bool isTextureST;

        // 역참조
        public string materialName;
        public int materialInstanceId;
    }

    #endregion

    #region Shader

    [Serializable]
    public class ShaderPropertyInfo
    {
        public int index;
        public string name;
        public int nameID;
        public string type; // Texture, Float, Range, Color, Vector, Int
        public string[] attributes; // [Normal], [HDR], etc.
    }

    [Serializable]
    public class ShaderEntry
    {
        public string name;
        public string assetPath;
        public int passCount;
        public List<string> keywords = new List<string>();
        public List<ShaderPropertyInfo> properties = new List<ShaderPropertyInfo>();
        public List<string> variantKeys = new List<string>();
        public List<int> materialInstanceIds = new List<int>();
    }

    #endregion

    #region Variant

    [Serializable]
    public struct SlotState
    {
        public int slotIndex;
        public string propertyName;
        public bool isRealTexture;
        public TextureSignature signature;

        public override string ToString()
        {
            return isRealTexture
                ? $"[{slotIndex}] {propertyName} real({signature})"
                : $"[{slotIndex}] {propertyName} dummy";
        }
    }

    [Serializable]
    public class VariantEntry
    {
        public string shaderName;
        public string[] activeKeywords;
        public string variantKey; // "ShaderName|KW1|KW2" (sorted)

        public List<int> materialInstanceIds = new List<int>();
        public List<SlotState> textureSlotPattern = new List<SlotState>();
        public List<ScalarEntry> scalarPattern = new List<ScalarEntry>();

        // 슬롯 패턴 문자열 (검색용)
        // e.g., "real:2048x2048_BC7|real:2048x2048_BC7|dummy"
        public string slotPatternKey;
    }

    #endregion

    #region Material

    [Serializable]
    public class MaterialEntry
    {
        public string name;
        public int instanceId;
        public bool isInstance;
        public int renderQueue;

        public string shaderName;
        public string[] activeKeywords;
        public string variantKey;

        public List<TextureEntry> textures = new List<TextureEntry>();
        public List<ScalarEntry> scalars = new List<ScalarEntry>();

        // MaterialPropertyBlock overrides
        public List<ScalarEntry> propertyBlockOverrides = new List<ScalarEntry>();
        public List<TextureEntry> propertyBlockTextures = new List<TextureEntry>();
    }

    #endregion

    #region Renderer

    [Serializable]
    public class MeshInfo
    {
        public string name;
        public int vertexCount;
        public int indexCount;
        public int subMeshCount;
    }

    [Serializable]
    public class RendererEntry
    {
        public string gameObjectName;
        public string hierarchyPath;
        public string sceneName;

        public MeshInfo mesh;

        public List<string> visibleFromCameras = new List<string>();
        public int lightmapIndex;
        public string lightProbeUsage;
        public string reflectionProbeUsage;

        public List<MaterialEntry> materials = new List<MaterialEntry>();
    }

    #endregion

    #region Camera / Light

    [Serializable]
    public class CameraEntry
    {
        public string name;
        public string hierarchyPath;
        public int depth;
        public float fieldOfView;
        public float nearClip;
        public float farClip;
        public string clearFlags;
        public int cullingMask;
    }

    [Serializable]
    public class LightEntry
    {
        public string name;
        public string hierarchyPath;
        public string type; // Directional, Point, Spot, Area
        public Color color;
        public float intensity;
        public float range;
        public bool shadowsEnabled;
        public string shadowType;
        public int shadowmapResolution;
        public string cookieTexturePath;
    }

    #endregion

    #region Global State

    [Serializable]
    public class GlobalTextureEntry
    {
        public string propertyName;
        public int nameID;
        public TextureSignature signature;
        public string textureType;
    }

    [Serializable]
    public class GlobalState
    {
        public List<GlobalTextureEntry> globalTextures = new List<GlobalTextureEntry>();
        public List<TextureSignature> activeRenderTextures = new List<TextureSignature>();
        public List<TextureSignature> lightmapTextures = new List<TextureSignature>();

        // RenderSettings
        public string ambientMode;
        public Color ambientLight;
        public string fogMode;
        public Color fogColor;
        public float fogDensity;

        // QualitySettings
        public string qualityLevel;
        public int shadowResolution;
        public string shadowQuality;
    }

    #endregion

    #region Snapshot

    [Serializable]
    public class SnapshotStatistics
    {
        public int totalRenderers;
        public int totalMaterials;
        public int totalShaders;
        public int totalTextures;
        public int totalVariants;
        public long totalTextureMemoryBytes;
    }

    [Serializable]
    public class SceneSnapshotData
    {
        public string timestamp;
        public string unityVersion;
        public string graphicsAPI;
        public int screenWidth;
        public int screenHeight;

        public List<string> loadedScenes = new List<string>();
        public List<CameraEntry> cameras = new List<CameraEntry>();
        public List<RendererEntry> renderers = new List<RendererEntry>();
        public List<LightEntry> lights = new List<LightEntry>();
        public GlobalState globalState = new GlobalState();
        public SnapshotStatistics statistics = new SnapshotStatistics();

        // 독립 저장용 (중복 제거된 셰이더/variant 목록)
        public List<ShaderEntry> shaders = new List<ShaderEntry>();
        public List<VariantEntry> variants = new List<VariantEntry>();
    }

    #endregion

    #region Format Mapping

    /// <summary>
    /// Unity TextureFormat과 RenderDoc 포맷 이름 간의 매핑.
    /// DX11/Vulkan에서 보이는 포맷 이름을 Unity 포맷으로 변환.
    /// </summary>
    public static class TextureFormatMap
    {
        private static readonly Dictionary<string, string> s_UnityToRenderDoc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // DX11 format names
            { "DXT1", "BC1_UNORM" },
            { "DXT1Crunched", "BC1_UNORM" },
            { "DXT5", "BC3_UNORM" },
            { "DXT5Crunched", "BC3_UNORM" },
            { "BC4", "BC4_UNORM" },
            { "BC5", "BC5_UNORM" },
            { "BC6H", "BC6H_UF16" },
            { "BC7", "BC7_UNORM" },
            { "RGBA32", "R8G8B8A8_UNORM" },
            { "ARGB32", "R8G8B8A8_UNORM" },
            { "RGB24", "R8G8B8_UNORM" },
            { "R8", "R8_UNORM" },
            { "R16", "R16_UNORM" },
            { "RFloat", "R32_FLOAT" },
            { "RGFloat", "R32G32_FLOAT" },
            { "RGBAFloat", "R32G32B32A32_FLOAT" },
            { "RHalf", "R16_FLOAT" },
            { "RGHalf", "R16G16_FLOAT" },
            { "RGBAHalf", "R16G16B16A16_FLOAT" },
            { "ASTC_4x4", "ASTC_4x4_UNORM" },
            { "ASTC_5x5", "ASTC_5x5_UNORM" },
            { "ASTC_6x6", "ASTC_6x6_UNORM" },
            { "ASTC_8x8", "ASTC_8x8_UNORM" },
            { "ASTC_10x10", "ASTC_10x10_UNORM" },
            { "ASTC_12x12", "ASTC_12x12_UNORM" },
            { "ETC2_RGB", "ETC2_R8G8B8_UNORM" },
            { "ETC2_RGBA8", "ETC2_R8G8B8A8_UNORM" },
        };

        private static readonly Dictionary<string, string> s_RenderDocToUnity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static TextureFormatMap()
        {
            foreach (var kvp in s_UnityToRenderDoc)
            {
                s_RenderDocToUnity[kvp.Value] = kvp.Key;
            }
            // SRGB variants
            s_RenderDocToUnity["BC1_UNORM_SRGB"] = "DXT1";
            s_RenderDocToUnity["BC3_UNORM_SRGB"] = "DXT5";
            s_RenderDocToUnity["BC7_UNORM_SRGB"] = "BC7";
            s_RenderDocToUnity["R8G8B8A8_UNORM_SRGB"] = "RGBA32";
            s_RenderDocToUnity["D32_FLOAT"] = "RFloat";
            s_RenderDocToUnity["D24_UNORM_S8_UINT"] = "Depth";
            s_RenderDocToUnity["D16_UNORM"] = "R16";
        }

        public static string UnityToRenderDoc(string unityFormat)
        {
            return s_UnityToRenderDoc.TryGetValue(unityFormat, out var rdFormat) ? rdFormat : unityFormat;
        }

        public static string RenderDocToUnity(string rdFormat)
        {
            return s_RenderDocToUnity.TryGetValue(rdFormat, out var unityFormat) ? unityFormat : rdFormat;
        }

        /// <summary>
        /// 두 포맷이 동일한지 비교 (Unity/RenderDoc 이름 모두 지원)
        /// </summary>
        public static bool FormatsMatch(string formatA, string formatB)
        {
            if (string.Equals(formatA, formatB, StringComparison.OrdinalIgnoreCase))
                return true;

            // A를 RenderDoc 이름으로 변환해서 비교
            var rdA = UnityToRenderDoc(formatA);
            var rdB = UnityToRenderDoc(formatB);
            if (string.Equals(rdA, rdB, StringComparison.OrdinalIgnoreCase))
                return true;

            // SRGB suffix 무시 비교
            var baseA = StripSrgbSuffix(rdA);
            var baseB = StripSrgbSuffix(rdB);
            return string.Equals(baseA, baseB, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripSrgbSuffix(string format)
        {
            if (format != null && format.EndsWith("_SRGB", StringComparison.OrdinalIgnoreCase))
                return format.Substring(0, format.Length - 5);
            return format;
        }
    }

    #endregion
}
