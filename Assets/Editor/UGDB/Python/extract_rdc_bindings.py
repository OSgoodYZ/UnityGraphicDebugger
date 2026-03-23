#!/usr/bin/env python3
"""
extract_rdc_bindings.py
=======================
pyrenderdoc API를 사용하여 .rdc 캡처 파일에서 전체 드로우콜의 GPU 바인딩 정보를 추출한다.
결과는 JSON으로 stdout 또는 파일에 출력된다.

사용법:
  renderdoccmd python extract_rdc_bindings.py <input.rdc> <output.json>

또는 RenderDoc 내장 Python 콘솔에서:
  exec(open("extract_rdc_bindings.py").read())
  extract("capture.rdc", "output.json")

출력 JSON 포맷:
{
  "captureFile": "capture.rdc",
  "graphicsAPI": "D3D11",
  "drawCalls": [
    {
      "eventId": 47,
      "name": "DrawIndexed(34560)",
      "indexCount": 34560,
      "vertexCount": 0,
      "textures": [
        {"slot": 0, "width": 2048, "height": 2048, "format": "BC7_UNORM", "mipCount": 12},
        ...
      ],
      "cbFloats": [1.0, 0.95, 0.9, 1.0, ...],
      "cbColors": [],
      "shaderKeywords": [],
      "shaderName": ""
    },
    ...
  ]
}
"""

import sys
import json
import os


def is_draw_action(action):
    """드로우콜인지 판별한다 (DrawIndexed, Draw, Dispatch 등)."""
    flags = action.flags
    # renderdoc의 ActionFlags에서 Drawcall 비트 체크
    return bool(flags & rd.ActionFlags.Drawcall)


def get_index_vertex_count(action):
    """드로우콜에서 index/vertex count를 추출한다."""
    index_count = 0
    vertex_count = 0

    if bool(action.flags & rd.ActionFlags.Indexed):
        index_count = action.numIndices
    else:
        vertex_count = action.numIndices  # non-indexed draw의 경우 numIndices가 vertex count

    return index_count, vertex_count


def extract_textures(controller, state, stage):
    """지정된 셰이더 스테이지에 바인딩된 텍스처 정보를 추출한다."""
    textures = []
    pipe = controller.GetPipelineState()

    # 읽기 전용 리소스 (SRV/texture bindings)
    ro_bindings = state.GetReadOnlyResources(stage)

    for bind_idx, bind in enumerate(ro_bindings):
        for res_idx, res in enumerate(bind.resources):
            res_id = res.resourceId

            if res_id == rd.ResourceId.Null():
                continue

            # 리소스 상세 정보
            tex_desc = controller.GetTexture(res_id)
            if tex_desc is None or tex_desc.resourceId == rd.ResourceId.Null():
                continue

            # 1D, Buffer 등은 스킵
            if tex_desc.dimension != rd.TextureDim.Texture2D and \
               tex_desc.dimension != rd.TextureDim.TextureCube and \
               tex_desc.dimension != rd.TextureDim.Texture2DArray:
                continue

            tex_info = {
                "slot": bind_idx,
                "width": tex_desc.width,
                "height": tex_desc.height,
                "format": str(tex_desc.format.Name()),
                "mipCount": tex_desc.mips,
                "arraySize": tex_desc.arraysize,
                "dimension": str(tex_desc.dimension),
            }
            textures.append(tex_info)

    return textures


def extract_cb_values(controller, state, stage):
    """지정된 셰이더 스테이지의 Constant Buffer 값을 추출한다."""
    floats = []
    colors = []

    cb_bindings = state.GetConstantBuffers(stage, False)

    for cb_idx, cb in enumerate(cb_bindings):
        if cb.resourceId == rd.ResourceId.Null():
            continue

        # CB 데이터 읽기
        cb_data = controller.GetBufferData(cb.resourceId, cb.byteOffset, cb.byteSize)
        if cb_data is None or len(cb_data) == 0:
            continue

        # float 배열로 변환 (4 bytes per float)
        import struct
        num_floats = len(cb_data) // 4
        # 최대 64개 float까지만 (너무 큰 CB는 잘라냄)
        num_floats = min(num_floats, 64)

        for i in range(num_floats):
            offset = i * 4
            if offset + 4 <= len(cb_data):
                val = struct.unpack('f', cb_data[offset:offset + 4])[0]
                # NaN이나 무한대, 극단적으로 큰 값은 스킵
                if val != val or abs(val) > 1e10:
                    continue
                floats.append(round(val, 6))

        # float4 단위로 color 후보 추출 (0~1 범위의 4개 연속 값)
        for i in range(0, min(num_floats, 64) - 3, 4):
            vals = floats[i:i + 4] if i + 4 <= len(floats) else []
            if len(vals) == 4 and all(0.0 <= v <= 1.0 for v in vals):
                colors.append(vals)

    return floats, colors


def extract_shader_info(controller, state, stage):
    """셰이더 이름 및 키워드 정보를 추출한다."""
    shader_name = ""
    keywords = []

    refl = state.GetShaderReflection(stage)
    if refl is not None:
        shader_name = refl.debugInfo.files[0].filename if refl.debugInfo and refl.debugInfo.files else ""
        # 셰이더 이름에서 경로 형식 추출 시도
        entry = refl.entryPoint
        if entry:
            shader_name = entry

    # RenderDoc의 셰이더 리플렉션에서 키워드 직접 추출은 제한적
    # Unity 셰이더의 경우 variant 키워드가 #pragma multi_compile으로 정의됨
    # 리플렉션 디버그 정보에서 키워드 힌트를 찾는다
    if refl is not None and refl.debugInfo:
        for f in refl.debugInfo.files:
            content = f.contents if hasattr(f, 'contents') else ""
            # #pragma multi_compile / shader_feature 라인에서 키워드 추출
            for line in content.split('\n'):
                line = line.strip()
                if '#pragma multi_compile' in line or '#pragma shader_feature' in line:
                    parts = line.split()
                    for part in parts:
                        part = part.strip()
                        if part.startswith('_') and part.isupper():
                            keywords.append(part)

    return shader_name, list(set(keywords))


def collect_draw_calls(controller):
    """캡처에서 모든 드로우콜의 GPU 바인딩을 수집한다."""
    draw_calls = []

    # 루트 액션부터 재귀 순회
    actions = controller.GetRootActions()

    def walk_actions(action_list):
        for action in action_list:
            if is_draw_action(action):
                # 해당 드로우콜 시점으로 이동
                controller.SetFrameEvent(action.eventId, True)
                state = controller.GetPipelineState()

                index_count, vertex_count = get_index_vertex_count(action)

                # Pixel Shader 스테이지의 바인딩 추출 (텍스처는 주로 PS에 바인딩)
                textures = extract_textures(controller, state, rd.ShaderStage.Pixel)

                # Vertex Shader의 바인딩도 추가 (일부 텍스처가 VS에 바인딩될 수 있음)
                vs_textures = extract_textures(controller, state, rd.ShaderStage.Vertex)
                for t in vs_textures:
                    t["slot"] = t["slot"] + 100  # VS 슬롯은 100+ 오프셋
                    textures.append(t)

                # CB 값 추출 (PS의 CB)
                cb_floats, cb_colors = extract_cb_values(controller, state, rd.ShaderStage.Pixel)

                # 셰이더 정보
                shader_name, keywords = extract_shader_info(controller, state, rd.ShaderStage.Pixel)

                draw_entry = {
                    "eventId": action.eventId,
                    "name": action.GetName(controller.GetStructuredFile()),
                    "indexCount": index_count,
                    "vertexCount": vertex_count,
                    "textures": textures,
                    "cbFloats": cb_floats,
                    "cbColors": cb_colors,
                    "shaderKeywords": keywords,
                    "shaderName": shader_name,
                }
                draw_calls.append(draw_entry)

            # 자식 액션 순회
            if action.children:
                walk_actions(action.children)

    walk_actions(actions)
    return draw_calls


def detect_graphics_api(controller):
    """캡처의 그래픽스 API를 감지한다."""
    api = controller.GetAPIProperties()
    if api is not None:
        return str(api.pipelineType)
    return "Unknown"


def extract(input_rdc, output_json):
    """
    .rdc 파일을 파싱하여 드로우콜 바인딩 정보를 JSON으로 추출한다.

    Parameters:
        input_rdc: 입력 .rdc 파일 경로
        output_json: 출력 JSON 파일 경로 (None이면 stdout)
    """
    cap = rd.OpenCaptureFile()
    status = cap.OpenFile(input_rdc, '', None)

    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(f"Failed to open capture file: {input_rdc} (status: {status})")

    if not cap.LocalReplaySupport():
        raise RuntimeError(f"Capture requires remote replay (not supported): {input_rdc}")

    status, controller = cap.OpenCapture(rd.ReplayOptions(), None)

    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(f"Failed to open replay: {input_rdc} (status: {status})")

    try:
        graphics_api = detect_graphics_api(controller)
        draw_calls = collect_draw_calls(controller)

        result = {
            "captureFile": os.path.basename(input_rdc),
            "graphicsAPI": graphics_api,
            "totalDrawCalls": len(draw_calls),
            "drawCalls": draw_calls,
        }

        json_str = json.dumps(result, indent=2, ensure_ascii=False)

        if output_json:
            with open(output_json, 'w', encoding='utf-8') as f:
                f.write(json_str)
            print(f"[UGDB] Extracted {len(draw_calls)} draw calls -> {output_json}")
        else:
            print(json_str)

    finally:
        controller.Shutdown()
        cap.Shutdown()


# CLI 진입점
if __name__ == "__main__" or "renderdoc" in sys.modules:
    if len(sys.argv) >= 3:
        extract(sys.argv[1], sys.argv[2])
    elif len(sys.argv) == 2:
        extract(sys.argv[1], None)
    else:
        print("Usage: extract_rdc_bindings.py <input.rdc> [output.json]")
        print("  Run via: renderdoccmd python extract_rdc_bindings.py <input.rdc> <output.json>")
        sys.exit(1)
