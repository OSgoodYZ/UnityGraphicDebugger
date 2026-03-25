using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace UGDB.Core
{
    /// <summary>
    /// Python 스크립트(extract_rdc_bindings.py)의 JSON 출력을 파싱하여
    /// 각 드로우콜에 대해 DrawCallMatcher로 자동 매칭을 수행한다.
    /// </summary>
    public static class AutoMatcher
    {
        /// <summary>
        /// 자동 매칭 결과 하나 (드로우콜 1개에 대한 매칭 정보).
        /// </summary>
        [Serializable]
        public class AutoMatchResult
        {
            public int drawCallIndex;
            public int eventId;
            public string drawCallName;
            public int indexCount;
            public int vertexCount;
            public int textureCount;
            public int cbFloatCount;

            public bool matched;
            public DrawCallMatcher.MatchResult bestMatch;
            public List<DrawCallMatcher.MatchResult> allMatches;
        }

        /// <summary>
        /// 전체 자동 매칭 보고서.
        /// </summary>
        [Serializable]
        public class AutoMatchReport
        {
            public string captureFile;
            public string graphicsAPI;
            public int totalDrawCalls;
            public int matchedCount;
            public int unmatchedCount;
            public int highConfidenceCount;
            public int mediumConfidenceCount;
            public int lowConfidenceCount;
            public List<AutoMatchResult> results = new List<AutoMatchResult>();
        }

        // JSON 역직렬화용 데이터 클래스 (Python 출력 포맷)
        [Serializable]
        private class RdcJsonRoot
        {
            public string captureFile;
            public string graphicsAPI;
            public int totalDrawCalls;
            public List<RdcDrawCall> drawCalls;
        }

        [Serializable]
        private class RdcDrawCall
        {
            public int eventId;
            public string name;
            public int indexCount;
            public int vertexCount;
            public List<RdcTexture> textures;
            public List<float> cbFloats;
            public List<float[]> cbColors;
            public List<string> shaderKeywords;
            public string shaderName;
        }

        [Serializable]
        private class RdcTexture
        {
            public int slot;
            public int width;
            public int height;
            public string format;
            public int mipCount;
        }

        /// <summary>
        /// .rdc JSON 파일과 스냅샷을 기반으로 자동 매칭을 수행한다.
        /// </summary>
        public static AutoMatchReport Match(string rdcJsonPath, SceneSnapshotData snapshot)
        {
            if (string.IsNullOrEmpty(rdcJsonPath))
                throw new ArgumentNullException("rdcJsonPath");

            if (snapshot == null)
                throw new ArgumentNullException("snapshot");

            if (!File.Exists(rdcJsonPath))
                throw new FileNotFoundException("RDC JSON not found: " + rdcJsonPath);

            // JSON 파싱
            var jsonText = File.ReadAllText(rdcJsonPath);
            var rdcData = JsonUtility.FromJson<RdcJsonRoot>(jsonText);

            if (rdcData == null || rdcData.drawCalls == null)
                throw new InvalidOperationException("Failed to parse RDC JSON: " + rdcJsonPath);

            // LookupEngine + DrawCallMatcher 초기화
            var engine = new LookupEngine();
            engine.BuildIndices(snapshot);
            var matcher = new DrawCallMatcher(engine);

            // 보고서 생성
            var report = new AutoMatchReport
            {
                captureFile = rdcData.captureFile,
                graphicsAPI = rdcData.graphicsAPI,
                totalDrawCalls = rdcData.drawCalls.Count,
            };

            // 각 드로우콜에 대해 매칭 수행
            for (int i = 0; i < rdcData.drawCalls.Count; i++)
            {
                var dc = rdcData.drawCalls[i];
                var query = BuildMatchQuery(dc);
                var matches = matcher.Match(query);

                var result = new AutoMatchResult
                {
                    drawCallIndex = i,
                    eventId = dc.eventId,
                    drawCallName = dc.name,
                    indexCount = dc.indexCount,
                    vertexCount = dc.vertexCount,
                    textureCount = dc.textures != null ? dc.textures.Count : 0,
                    cbFloatCount = dc.cbFloats != null ? dc.cbFloats.Count : 0,
                    matched = matches.Count > 0,
                    bestMatch = matches.Count > 0 ? matches[0] : null,
                    allMatches = matches,
                };

                report.results.Add(result);

                if (result.matched)
                {
                    report.matchedCount++;
                    switch (result.bestMatch.confidence)
                    {
                        case DrawCallMatcher.MatchConfidence.High:
                            report.highConfidenceCount++;
                            break;
                        case DrawCallMatcher.MatchConfidence.Medium:
                            report.mediumConfidenceCount++;
                            break;
                        case DrawCallMatcher.MatchConfidence.Low:
                            report.lowConfidenceCount++;
                            break;
                    }
                }
                else
                {
                    report.unmatchedCount++;
                }
            }

            Debug.Log(string.Format("[UGDB] Auto-Match 완료: {0} draw calls, {1} matched ({2} high, {3} medium, {4} low), {5} unmatched",
                report.totalDrawCalls, report.matchedCount,
                report.highConfidenceCount, report.mediumConfidenceCount, report.lowConfidenceCount,
                report.unmatchedCount));

            return report;
        }

        /// <summary>
        /// RDC 드로우콜 데이터에서 MatchQuery를 생성한다.
        /// </summary>
        private static DrawCallMatcher.MatchQuery BuildMatchQuery(RdcDrawCall dc)
        {
            var query = new DrawCallMatcher.MatchQuery();

            // Geometry
            if (dc.indexCount > 0)
                query.indexCount = dc.indexCount;
            if (dc.vertexCount > 0)
                query.vertexCount = dc.vertexCount;

            // Textures
            if (dc.textures != null && dc.textures.Count > 0)
            {
                query.textures = new List<TextureSignature>();
                foreach (var tex in dc.textures)
                {
                    // VS 슬롯(100+)은 스킵 — PS 텍스처만 매칭에 사용
                    if (tex.slot >= 100)
                        continue;

                    query.textures.Add(new TextureSignature(
                        tex.width, tex.height, tex.format, tex.mipCount));
                }
            }

            // CB Floats
            if (dc.cbFloats != null && dc.cbFloats.Count > 0)
            {
                query.cbFloats = new List<float>();
                // 너무 많으면 앞쪽만 사용 (성능)
                int maxFloats = Mathf.Min(dc.cbFloats.Count, 32);
                for (int i = 0; i < maxFloats; i++)
                    query.cbFloats.Add(dc.cbFloats[i]);
            }

            // CB Colors
            if (dc.cbColors != null && dc.cbColors.Count > 0)
            {
                query.cbColors = new List<Color>();
                foreach (var c in dc.cbColors)
                {
                    if (c != null && c.Length >= 4)
                        query.cbColors.Add(new Color(c[0], c[1], c[2], c[3]));
                }
            }

            // Shader
            if (dc.shaderKeywords != null && dc.shaderKeywords.Count > 0)
                query.shaderKeywords = dc.shaderKeywords;

            if (!string.IsNullOrEmpty(dc.shaderName))
                query.shaderName = dc.shaderName;

            return query;
        }

        /// <summary>
        /// Python 스크립트를 실행하여 .rdc 파일에서 JSON을 추출한다.
        /// </summary>
        /// <param name="rdcPath">.rdc 파일 경로</param>
        /// <param name="outputJsonPath">출력 JSON 파일 경로</param>
        /// <param name="renderDocCmdPath">renderdoccmd 실행 파일 경로 (null이면 EditorPrefs에서 가져옴)</param>
        /// <returns>성공 여부</returns>
        public static bool RunExtractScript(string rdcPath, string outputJsonPath, string renderDocCmdPath = null)
        {
            if (string.IsNullOrEmpty(renderDocCmdPath))
                renderDocCmdPath = EditorPrefs.GetString("UGDB_RenderDocCmdPath", "");

            if (string.IsNullOrEmpty(renderDocCmdPath) || !File.Exists(renderDocCmdPath))
            {
                Debug.LogError("[UGDB] renderdoccmd 경로가 설정되지 않았거나 파일이 없습니다. " +
                    "Edit > Preferences에서 UGDB > RenderDocCmd Path를 설정하세요.");
                return false;
            }

            if (!File.Exists(rdcPath))
            {
                Debug.LogError("[UGDB] .rdc 파일을 찾을 수 없습니다: " + rdcPath);
                return false;
            }

            // Python 스크립트 경로
            var scriptPath = GetExtractScriptPath();
            if (!File.Exists(scriptPath))
            {
                Debug.LogError("[UGDB] extract_rdc_bindings.py를 찾을 수 없습니다: " + scriptPath);
                return false;
            }

            // qrenderdoc --python 방식: RenderDoc 내장 Python에서 pyrenderdoc 사용
            var renderDocDir = Path.GetDirectoryName(renderDocCmdPath);
            var qrenderdocPath = Path.Combine(renderDocDir, "qrenderdoc.exe");

            if (!File.Exists(qrenderdocPath))
            {
                Debug.LogError("[UGDB] qrenderdoc.exe를 찾을 수 없습니다: " + qrenderdocPath);
                return false;
            }

            // 래퍼 스크립트: 파일 기반 로깅 + 인자 하드코딩 + 완료/에러 시 강제 종료
            var wrapperPath = Path.Combine(Path.GetDirectoryName(outputJsonPath), "_ugdb_extract_wrapper.py");
            var logPath = Path.Combine(Path.GetDirectoryName(outputJsonPath), "_ugdb_python.log");
            File.WriteAllText(wrapperPath, string.Format(
@"import sys, os

LOG = r'{3}'
def log(msg):
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write(msg + '\n')

log('=== UGDB wrapper start ===')
log('Python: ' + sys.version)
log('argv will be: extract, {0}, {1}')

# rd 모듈 확인
try:
    log('rd module type: ' + str(type(rd)))
    log('rd available: True')
except NameError:
    log('rd available: False (NameError)')
    try:
        import renderdoc as rd
        log('imported renderdoc as rd')
    except ImportError as ie:
        log('renderdoc import failed: ' + str(ie))

# 사용 가능한 전역 변수 덤프
log('globals: ' + str([k for k in dir() if not k.startswith('_')]))

sys.argv = ['extract', r'{0}', r'{1}']
try:
    log('executing main script: {2}')
    exec(open(r'{2}', encoding='utf-8').read())
    log('main script finished OK')
except Exception as e:
    log('SCRIPT ERROR: ' + str(e))
    import traceback
    log(traceback.format_exc())

log('=== UGDB wrapper end ===')
os._exit(0)
",
                rdcPath, outputJsonPath, scriptPath, logPath));

            // .rdc를 넘기지 않음 (파일 락 방지) — 스크립트가 직접 연다
            var args = string.Format("--python \"{0}\"", wrapperPath);
            var fullCommand = qrenderdocPath + " " + args;
            Debug.Log("[UGDB] Running: " + fullCommand);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = qrenderdocPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                };

                using (var proc = Process.Start(psi))
                {
                    // GUI 프로세스: 파이프 리다이렉트 없이 파일 기반 통신만 사용
                    // os._exit(0)으로 빠르게 종료됨. 60초 타임아웃은 안전장치.
                    bool exited = proc.WaitForExit(60000);

                    if (!exited)
                    {
                        Debug.LogWarning("[UGDB] qrenderdoc 타임아웃 — 프로세스를 강제 종료합니다.");
                        try { proc.Kill(); } catch (Exception) { }
                    }

                    // 파일 기반 로그 출력
                    if (File.Exists(logPath))
                    {
                        var pyLog = File.ReadAllText(logPath);
                        Debug.Log("[UGDB] Python 로그:\n" + pyLog);
                    }
                    else
                    {
                        Debug.LogWarning("[UGDB] Python 로그 파일이 생성되지 않았습니다. qrenderdoc이 스크립트를 실행하지 않았을 수 있습니다.");
                    }

                    // JSON 파일 생성 여부로 성공 판단
                    if (File.Exists(outputJsonPath))
                    {
                        Debug.Log("[UGDB] RDC 파싱 완료: " + outputJsonPath);
                        return true;
                    }

                    // 로그 파일에서 에러 원인 확인
                    string pyLogContent = "";
                    if (File.Exists(logPath))
                        pyLogContent = File.ReadAllText(logPath);

                    Debug.LogError(string.Format(
                        "[UGDB] Auto-Match 실패: 출력 JSON이 생성되지 않았습니다.\n" +
                        "실행 명령: {0}\nPython 로그:\n{1}",
                        fullCommand, pyLogContent));
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(string.Format(
                    "[UGDB] qrenderdoc 실행 오류: {0}\n실행 명령: {1}", e.Message, fullCommand));
                return false;
            }
            finally
            {
                try { if (File.Exists(wrapperPath)) File.Delete(wrapperPath); }
                catch (Exception) { }
                try { if (File.Exists(logPath)) File.Delete(logPath); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// 시스템에서 Python 실행 파일을 검색한다.
        /// </summary>
        private static string FindPythonExecutable()
        {
            // 1) EditorPrefs에 사용자 지정 경로
            var userPython = UnityEditor.EditorPrefs.GetString("UGDB_PythonPath", "");
            if (!string.IsNullOrEmpty(userPython) && File.Exists(userPython))
                return userPython;

            // 2) PATH에서 python / python3 검색
            string[] names = { "python", "python3", "py" };
            var pathEnv = System.Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(';'))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in names)
                    {
                        var candidate = Path.Combine(dir.Trim(), name + ".exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            // 3) 폴백: "python"을 그대로 반환 (PATH에 있으면 동작)
            return "python";
        }

        /// <summary>
        /// extract_rdc_bindings.py 스크립트의 경로를 반환한다.
        /// </summary>
        public static string GetExtractScriptPath()
        {
            // Assets/Editor/UGDB/Python/extract_rdc_bindings.py
            return Path.Combine(Application.dataPath, "Editor", "UGDB", "Python", "extract_rdc_bindings.py");
        }

        /// <summary>
        /// 세션 디렉토리에서 .rdc 파일을 파싱하여 자동 매칭을 수행한다.
        /// 이미 JSON이 존재하면 재사용한다.
        /// </summary>
        public static AutoMatchReport MatchFromSession(string sessionDir, SceneSnapshotData snapshot, string renderDocCmdPath = null)
        {
            var rdcPath = Path.Combine(sessionDir, "capture.rdc");
            var jsonPath = Path.Combine(sessionDir, "rdc_bindings.json");

            // JSON이 없으면 Python 스크립트로 생성
            if (!File.Exists(jsonPath))
            {
                if (!File.Exists(rdcPath))
                {
                    Debug.LogError("[UGDB] 세션에 .rdc 파일이 없습니다: " + sessionDir);
                    return null;
                }

                if (!RunExtractScript(rdcPath, jsonPath, renderDocCmdPath))
                    return null;
            }

            return Match(jsonPath, snapshot);
        }
    }
}
