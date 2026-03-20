using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;

namespace UGDB.Core
{
    /// <summary>
    /// Play 모드에서 현재 씬의 모든 렌더링 관련 정보를 수집하여
    /// 검색 가능한 SceneSnapshotData를 생성한다.
    /// </summary>
    public static class SceneSnapshot
    {
        /// <summary>
        /// 현재 씬의 모든 렌더링 데이터를 수집하여 스냅샷을 생성한다.
        /// Play 모드에서 호출해야 런타임 머티리얼/텍스처 바인딩을 정확히 수집할 수 있다.
        /// </summary>
        public static SceneSnapshotData Capture()
        {
            var data = new SceneSnapshotData
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unityVersion = Application.unityVersion,
                graphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
                screenWidth = Screen.width,
                screenHeight = Screen.height
            };

            var processedShaders = new Dictionary<string, ShaderEntry>();

            CollectScenes(data);
            CollectCameras(data);
            CollectRenderers(data, processedShaders);
            CollectLights(data);
            CollectGlobalState(data);

            // Variant 추적은 수집 완료 후 수행
            VariantTracker.TrackVariants(data);

            ComputeStatistics(data);

            return data;
        }

        #region Scene / Camera

        private static void CollectScenes(SceneSnapshotData data)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    data.loadedScenes.Add(scene.name);
            }
        }

        private static void CollectCameras(SceneSnapshotData data)
        {
            foreach (var cam in Camera.allCameras)
            {
                data.cameras.Add(new CameraEntry
                {
                    name = cam.name,
                    hierarchyPath = GetHierarchyPath(cam.transform),
                    depth = (int)cam.depth,
                    fieldOfView = cam.fieldOfView,
                    nearClip = cam.nearClipPlane,
                    farClip = cam.farClipPlane,
                    clearFlags = cam.clearFlags.ToString(),
                    cullingMask = cam.cullingMask
                });
            }
        }

        #endregion

        #region Renderer

        private static void CollectRenderers(SceneSnapshotData data, Dictionary<string, ShaderEntry> processedShaders)
        {
            var allRenderers = UnityEngine.Object.FindObjectsOfType<Renderer>();

            // 카메라별 프러스텀 플레인 캐시
            var cameraPlanes = new Dictionary<Camera, Plane[]>();
            foreach (var cam in Camera.allCameras)
            {
                cameraPlanes[cam] = GeometryUtility.CalculateFrustumPlanes(cam);
            }

            foreach (var renderer in allRenderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                var entry = new RendererEntry
                {
                    gameObjectName = renderer.gameObject.name,
                    hierarchyPath = GetHierarchyPath(renderer.transform),
                    sceneName = renderer.gameObject.scene.name,
                    lightmapIndex = renderer.lightmapIndex,
                    lightProbeUsage = renderer.lightProbeUsage.ToString(),
                    reflectionProbeUsage = renderer.reflectionProbeUsage.ToString()
                };

                // 카메라 가시성 체크
                foreach (var kvp in cameraPlanes)
                {
                    if (GeometryUtility.TestPlanesAABB(kvp.Value, renderer.bounds))
                        entry.visibleFromCameras.Add(kvp.Key.name);
                }

                // 메시 정보
                entry.mesh = CollectMeshInfo(renderer);

                // 머티리얼 체인 수집
                var sharedMats = renderer.sharedMaterials;
                for (int i = 0; i < sharedMats.Length; i++)
                {
                    var mat = sharedMats[i];
                    if (mat == null) continue;
                    var matEntry = CollectMaterial(mat, processedShaders, data);
                    entry.materials.Add(matEntry);
                }

                // MaterialPropertyBlock 오버라이드 수집
                CollectPropertyBlock(renderer, entry);

                data.renderers.Add(entry);
            }
        }

        private static MeshInfo CollectMeshInfo(Renderer renderer)
        {
            var meshInfo = new MeshInfo();

            Mesh mesh = null;
            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                    mesh = filter.sharedMesh;
            }
            else if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }

            if (mesh != null)
            {
                meshInfo.name = mesh.name;
                meshInfo.vertexCount = mesh.vertexCount;
                meshInfo.subMeshCount = mesh.subMeshCount;

                int totalIdx = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    totalIdx += (int)mesh.GetIndexCount(i);
                meshInfo.indexCount = totalIdx;
            }

            return meshInfo;
        }

        #endregion

        #region Material / Shader

        private static MaterialEntry CollectMaterial(Material mat, Dictionary<string, ShaderEntry> processedShaders, SceneSnapshotData data)
        {
            var entry = new MaterialEntry
            {
                name = mat.name,
                instanceId = mat.GetInstanceID(),
                isInstance = mat.name.Contains("(Instance)"),
                renderQueue = mat.renderQueue,
                shaderName = mat.shader != null ? mat.shader.name : "None"
            };

            if (mat.shader == null)
                return entry;

            // 셰이더 정보 수집 (중복 방지)
            if (!processedShaders.ContainsKey(mat.shader.name))
            {
                var shaderEntry = CollectShader(mat.shader);
                processedShaders[mat.shader.name] = shaderEntry;
                data.shaders.Add(shaderEntry);
            }

            var shader = processedShaders[mat.shader.name];
            if (!shader.materialInstanceIds.Contains(mat.GetInstanceID()))
                shader.materialInstanceIds.Add(mat.GetInstanceID());

            // 활성 키워드
            entry.activeKeywords = mat.shaderKeywords;

            // 프로퍼티 값 수집
            CollectMaterialProperties(mat, shader, entry);

            return entry;
        }

        private static ShaderEntry CollectShader(Shader shader)
        {
            var entry = new ShaderEntry
            {
                name = shader.name,
                assetPath = AssetDatabase.GetAssetPath(shader)
            };

            // pass count (temporary material 경유)
            try
            {
                var tempMat = new Material(shader);
                entry.passCount = tempMat.passCount;
                UnityEngine.Object.DestroyImmediate(tempMat);
            }
            catch
            {
                entry.passCount = 0;
            }

            // 프로퍼티 수집
            int propCount = shader.GetPropertyCount();
            for (int i = 0; i < propCount; i++)
            {
                var prop = new ShaderPropertyInfo
                {
                    index = i,
                    name = shader.GetPropertyName(i),
                    nameID = shader.GetPropertyNameId(i),
                    type = shader.GetPropertyType(i).ToString(),
                    attributes = shader.GetPropertyAttributes(i)
                };
                entry.properties.Add(prop);
            }

            return entry;
        }

        private static void CollectMaterialProperties(Material mat, ShaderEntry shader, MaterialEntry matEntry)
        {
            foreach (var prop in shader.properties)
            {
                if (!mat.HasProperty(prop.nameID))
                    continue;

                switch (prop.type)
                {
                    case "Texture":
                        CollectTextureProperty(mat, prop, matEntry);
                        break;
                    case "Float":
                    case "Range":
                        matEntry.scalars.Add(new ScalarEntry
                        {
                            propertyName = prop.name,
                            nameID = prop.nameID,
                            type = prop.type == "Range" ? ScalarType.Range : ScalarType.Float,
                            floatValue = mat.GetFloat(prop.nameID),
                            materialName = matEntry.name,
                            materialInstanceId = matEntry.instanceId
                        });
                        break;
                    case "Color":
                        matEntry.scalars.Add(new ScalarEntry
                        {
                            propertyName = prop.name,
                            nameID = prop.nameID,
                            type = ScalarType.Color,
                            colorValue = mat.GetColor(prop.nameID),
                            materialName = matEntry.name,
                            materialInstanceId = matEntry.instanceId
                        });
                        break;
                    case "Vector":
                        matEntry.scalars.Add(new ScalarEntry
                        {
                            propertyName = prop.name,
                            nameID = prop.nameID,
                            type = ScalarType.Vector,
                            vectorValue = mat.GetVector(prop.nameID),
                            materialName = matEntry.name,
                            materialInstanceId = matEntry.instanceId
                        });
                        break;
                    case "Int":
                    case "Integer":
                        matEntry.scalars.Add(new ScalarEntry
                        {
                            propertyName = prop.name,
                            nameID = prop.nameID,
                            type = ScalarType.Int,
                            intValue = mat.GetInt(prop.nameID),
                            materialName = matEntry.name,
                            materialInstanceId = matEntry.instanceId
                        });
                        break;
                }
            }
        }

        private static void CollectTextureProperty(Material mat, ShaderPropertyInfo prop, MaterialEntry matEntry)
        {
            var tex = mat.GetTexture(prop.nameID);

            var texEntry = new TextureEntry
            {
                propertyName = prop.name,
                nameID = prop.nameID,
                materialName = matEntry.name,
                materialInstanceId = matEntry.instanceId
            };

            if (tex != null)
            {
                texEntry.signature = new TextureSignature(
                    tex.width,
                    tex.height,
                    GetTextureFormat(tex),
                    GetMipCount(tex)
                );
                texEntry.textureType = tex.GetType().Name;
                texEntry.filterMode = tex.filterMode.ToString();
                texEntry.wrapMode = tex.wrapMode.ToString();
                texEntry.assetPath = AssetDatabase.GetAssetPath(tex);
                texEntry.memorySizeBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
            }
            else
            {
                texEntry.textureType = "None";
            }

            matEntry.textures.Add(texEntry);

            // Texture ST (offset/scale) 수집
            var offset = mat.GetTextureOffset(prop.name);
            var scale = mat.GetTextureScale(prop.name);

            if (offset != Vector2.zero || scale != Vector2.one)
            {
                matEntry.scalars.Add(new ScalarEntry
                {
                    propertyName = prop.name + "_ST",
                    nameID = prop.nameID,
                    type = ScalarType.Vector,
                    vectorValue = new Vector4(scale.x, scale.y, offset.x, offset.y),
                    textureOffset = offset,
                    textureScale = scale,
                    isTextureST = true,
                    materialName = matEntry.name,
                    materialInstanceId = matEntry.instanceId
                });
            }
        }

        #endregion

        #region MaterialPropertyBlock

        private static void CollectPropertyBlock(Renderer renderer, RendererEntry entry)
        {
            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);

            if (mpb.isEmpty)
                return;

            // 각 머티리얼의 셰이더 프로퍼티에 대해 MPB 오버라이드 확인
            foreach (var matEntry in entry.materials)
            {
                foreach (var scalar in matEntry.scalars)
                {
                    float mpbFloat;
                    switch (scalar.type)
                    {
                        case ScalarType.Float:
                        case ScalarType.Range:
                            mpbFloat = mpb.GetFloat(scalar.nameID);
                            if (Mathf.Abs(mpbFloat - scalar.floatValue) > 0.0001f && mpbFloat != 0f)
                            {
                                matEntry.propertyBlockOverrides.Add(new ScalarEntry
                                {
                                    propertyName = scalar.propertyName,
                                    nameID = scalar.nameID,
                                    type = scalar.type,
                                    floatValue = mpbFloat,
                                    materialName = matEntry.name,
                                    materialInstanceId = matEntry.instanceId
                                });
                            }
                            break;

                        case ScalarType.Color:
                            var mpbColor = mpb.GetColor(scalar.nameID);
                            if (mpbColor != default && mpbColor != scalar.colorValue)
                            {
                                matEntry.propertyBlockOverrides.Add(new ScalarEntry
                                {
                                    propertyName = scalar.propertyName,
                                    nameID = scalar.nameID,
                                    type = ScalarType.Color,
                                    colorValue = mpbColor,
                                    materialName = matEntry.name,
                                    materialInstanceId = matEntry.instanceId
                                });
                            }
                            break;

                        case ScalarType.Vector:
                            if (scalar.isTextureST) continue;
                            var mpbVec = mpb.GetVector(scalar.nameID);
                            if (mpbVec != default && mpbVec != scalar.vectorValue)
                            {
                                matEntry.propertyBlockOverrides.Add(new ScalarEntry
                                {
                                    propertyName = scalar.propertyName,
                                    nameID = scalar.nameID,
                                    type = ScalarType.Vector,
                                    vectorValue = mpbVec,
                                    materialName = matEntry.name,
                                    materialInstanceId = matEntry.instanceId
                                });
                            }
                            break;
                    }
                }

                // MPB 텍스처 오버라이드
                foreach (var texEntry in matEntry.textures)
                {
                    var mpbTex = mpb.GetTexture(texEntry.nameID);
                    if (mpbTex != null)
                    {
                        matEntry.propertyBlockTextures.Add(new TextureEntry
                        {
                            propertyName = texEntry.propertyName,
                            nameID = texEntry.nameID,
                            signature = new TextureSignature(
                                mpbTex.width, mpbTex.height,
                                GetTextureFormat(mpbTex), GetMipCount(mpbTex)
                            ),
                            textureType = mpbTex.GetType().Name,
                            assetPath = AssetDatabase.GetAssetPath(mpbTex),
                            materialName = matEntry.name,
                            materialInstanceId = matEntry.instanceId
                        });
                    }
                }
            }
        }

        #endregion

        #region Light

        private static void CollectLights(SceneSnapshotData data)
        {
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (!light.enabled || !light.gameObject.activeInHierarchy)
                    continue;

                var entry = new LightEntry
                {
                    name = light.name,
                    hierarchyPath = GetHierarchyPath(light.transform),
                    type = light.type.ToString(),
                    color = light.color,
                    intensity = light.intensity,
                    range = light.range,
                    shadowsEnabled = light.shadows != LightShadows.None,
                    shadowType = light.shadows.ToString(),
                    shadowmapResolution = light.shadowResolution == UnityEngine.Rendering.LightShadowResolution.FromQualitySettings
                        ? QualitySettings.shadowResolution == UnityEngine.ShadowResolution.Low ? 512
                          : QualitySettings.shadowResolution == UnityEngine.ShadowResolution.Medium ? 1024
                          : QualitySettings.shadowResolution == UnityEngine.ShadowResolution.High ? 2048
                          : 4096
                        : (int)light.shadowResolution
                };

                if (light.cookie != null)
                    entry.cookieTexturePath = AssetDatabase.GetAssetPath(light.cookie);

                data.lights.Add(entry);
            }
        }

        #endregion

        #region Global State

        private static void CollectGlobalState(SceneSnapshotData data)
        {
            var gs = data.globalState;

            // RenderSettings
            gs.ambientMode = RenderSettings.ambientMode.ToString();
            gs.ambientLight = RenderSettings.ambientLight;
            gs.fogMode = RenderSettings.fogMode.ToString();
            gs.fogColor = RenderSettings.fogColor;
            gs.fogDensity = RenderSettings.fogDensity;

            // QualitySettings
            gs.qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()];
            gs.shadowResolution = (int)QualitySettings.shadowResolution;
            gs.shadowQuality = QualitySettings.shadows.ToString();

            // Lightmap 텍스처
            var lightmaps = LightmapSettings.lightmaps;
            if (lightmaps != null)
            {
                foreach (var lm in lightmaps)
                {
                    if (lm.lightmapColor != null)
                    {
                        gs.lightmapTextures.Add(new TextureSignature(
                            lm.lightmapColor.width,
                            lm.lightmapColor.height,
                            GetTextureFormat(lm.lightmapColor),
                            GetMipCount(lm.lightmapColor)
                        ));
                    }
                }
            }

            // 글로벌 텍스처 (잘 알려진 글로벌 프로퍼티)
            CollectGlobalTexture(gs, "_CameraDepthTexture");
            CollectGlobalTexture(gs, "_CameraOpaqueTexture");
            CollectGlobalTexture(gs, "_CameraColorTexture");
            CollectGlobalTexture(gs, "_ShadowMapTexture");
            CollectGlobalTexture(gs, "unity_SpecCube0");
            CollectGlobalTexture(gs, "unity_Lightmap");

            // 활성 RenderTexture 목록
            var activeRTs = RenderTexture.FindObjectsOfType<RenderTexture>();
            foreach (var rt in activeRTs)
            {
                if (rt != null && rt.IsCreated())
                {
                    gs.activeRenderTextures.Add(new TextureSignature(
                        rt.width, rt.height,
                        rt.format.ToString(),
                        rt.useMipMap ? Mathf.FloorToInt(Mathf.Log(Mathf.Max(rt.width, rt.height), 2)) + 1 : 1
                    ));
                }
            }
        }

        private static void CollectGlobalTexture(GlobalState gs, string propertyName)
        {
            int nameID = Shader.PropertyToID(propertyName);
            var tex = Shader.GetGlobalTexture(nameID);
            if (tex == null) return;

            gs.globalTextures.Add(new GlobalTextureEntry
            {
                propertyName = propertyName,
                nameID = nameID,
                signature = new TextureSignature(
                    tex.width, tex.height,
                    GetTextureFormat(tex), GetMipCount(tex)
                ),
                textureType = tex.GetType().Name
            });
        }

        #endregion

        #region Statistics

        private static void ComputeStatistics(SceneSnapshotData data)
        {
            var stats = data.statistics;
            stats.totalRenderers = data.renderers.Count;
            stats.totalShaders = data.shaders.Count;
            stats.totalVariants = data.variants.Count;

            var uniqueMaterials = new HashSet<int>();
            var uniqueTextures = new HashSet<string>();
            long totalTexMem = 0;

            foreach (var renderer in data.renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    uniqueMaterials.Add(mat.instanceId);
                    foreach (var tex in mat.textures)
                    {
                        if (tex.textureType != "None" && !string.IsNullOrEmpty(tex.assetPath))
                        {
                            if (uniqueTextures.Add(tex.assetPath))
                                totalTexMem += tex.memorySizeBytes;
                        }
                    }
                }
            }

            stats.totalMaterials = uniqueMaterials.Count;
            stats.totalTextures = uniqueTextures.Count;
            stats.totalTextureMemoryBytes = totalTexMem;
        }

        #endregion

        #region Utility

        public static string GetHierarchyPath(Transform t)
        {
            var path = t.name;
            var parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return "/" + path;
        }

        public static string GetTextureFormat(Texture tex)
        {
            if (tex is Texture2D tex2D)
                return tex2D.format.ToString();
            if (tex is RenderTexture rt)
                return rt.format.ToString();
            if (tex is Cubemap cube)
                return cube.format.ToString();
            return "Unknown";
        }

        public static int GetMipCount(Texture tex)
        {
            if (tex is Texture2D tex2D)
                return tex2D.mipmapCount;
            if (tex is RenderTexture rt)
                return rt.useMipMap
                    ? Mathf.FloorToInt(Mathf.Log(Mathf.Max(rt.width, rt.height), 2)) + 1
                    : 1;
            if (tex is Cubemap cube)
                return cube.mipmapCount;
            return 1;
        }

        #endregion
    }
}
