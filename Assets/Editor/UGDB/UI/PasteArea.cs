using UnityEditor;
using UnityEngine;
using UGDB.Parser;

namespace UGDB.UI
{
    /// <summary>
    /// RenderDoc에서 복사한 텍스트를 붙여넣는 영역.
    /// Ctrl+V 감지 시 자동 파싱 트리거.
    /// </summary>
    public class PasteArea
    {
        private string _pastedText = "";
        private Vector2 _scrollPos;
        private ParseResult _lastParseResult;
        private readonly ClipboardParser _parser = new ClipboardParser();

        public ParseResult LastParseResult { get { return _lastParseResult; } }
        public string PastedText { get { return _pastedText; } }

        /// <summary>
        /// 검색 버튼이 눌렸을 때 호출되는 콜백.
        /// </summary>
        public System.Action<ParseResult> OnSearchRequested;

        public void Draw()
        {
            EditorGUILayout.LabelField("RenderDoc에서 복사한 내용을 붙여넣기:", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));
            string newText = EditorGUILayout.TextArea(_pastedText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // 텍스트 변경 감지 → 자동 파싱
            if (newText != _pastedText)
            {
                _pastedText = newText;
                if (!string.IsNullOrEmpty(_pastedText))
                {
                    _lastParseResult = _parser.Parse(_pastedText);
                }
                else
                {
                    _lastParseResult = null;
                }
            }

            // 감지 결과 라벨
            EditorGUILayout.BeginHorizontal();

            if (_lastParseResult != null && _lastParseResult.queryType != QueryType.Unknown)
            {
                EditorGUILayout.LabelField(
                    string.Format("감지됨: {0}", _lastParseResult.GetSummary()),
                    EditorStyles.helpBox);
            }
            else if (!string.IsNullOrEmpty(_pastedText))
            {
                EditorGUILayout.LabelField("인식된 패턴 없음", EditorStyles.helpBox);
            }

            // 검색 버튼
            GUI.enabled = _lastParseResult != null && _lastParseResult.queryType != QueryType.Unknown;
            if (GUILayout.Button("검색", GUILayout.Width(60)))
            {
                if (OnSearchRequested != null)
                    OnSearchRequested(_lastParseResult);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        public void Clear()
        {
            _pastedText = "";
            _lastParseResult = null;
        }
    }
}
