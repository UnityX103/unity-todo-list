using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.TodoList
{
    /// <summary>
    /// Todo 输入弹窗（UI Toolkit 版本）
    /// </summary>
    public static class TodoInputDialog
    {
        public struct TodoInputResult
        {
            public bool Confirmed;
            public string Text;
            public bool SaveAsScene;
        }

        public static string Show(string title, string prompt, string defaultText = "")
        {
            var result = ShowInternal(title, prompt, defaultText, false, false);
            return result.Text;
        }

        public static TodoInputResult ShowWithOptions(string title, string prompt, string defaultText = "",
            bool showSaveAsScene = false,
            bool defaultSaveAsScene = false)
        {
            return ShowInternal(title, prompt, defaultText, showSaveAsScene, defaultSaveAsScene);
        }

        private static TodoInputResult ShowInternal(string title, string prompt, string defaultText, bool showSaveAsScene,
            bool defaultSaveAsScene)
        {
            var window = ScriptableObject.CreateInstance<TodoInputDialogWindow>();
            window.Init(title, prompt, defaultText, showSaveAsScene, defaultSaveAsScene);
            window.ShowModal();
            return new TodoInputResult
            {
                Confirmed = window.Confirmed,
                Text = window.Result,
                SaveAsScene = window.SaveAsScene
            };
        }

        private class TodoInputDialogWindow : EditorWindow
        {
            private const string PromptLabelName = "PromptLabel";
            private const string InputFieldName = "InputField";
            private const string ConfirmButtonName = "ConfirmButton";
            private const string CancelButtonName = "CancelButton";
            private const string SaveAsSceneRowName = "SaveAsSceneRow";
            private const string SaveAsSceneToggleName = "SaveAsSceneToggle";

            private string _windowTitle = "添加待办";
            private string _prompt = "请输入描述:";
            private string _inputText = "";
            private bool _confirmed;
            private bool _showSaveAsScene;
            private bool _saveAsScene;

            private TextField _inputField;
            private Label _promptLabel;
            private Button _confirmButton;
            private Button _cancelButton;
            private VisualElement _saveAsSceneRow;
            private Toggle _saveAsSceneToggle;

            public string Result => _confirmed ? _inputText : null;
            public bool Confirmed => _confirmed;
            public bool SaveAsScene => _saveAsScene;

            public void Init(string title, string prompt, string defaultText, bool showSaveAsScene, bool defaultSaveAsScene)
            {
                _windowTitle = string.IsNullOrWhiteSpace(title) ? "添加待办" : title;
                _prompt = string.IsNullOrWhiteSpace(prompt) ? "请输入描述:" : prompt;
                _inputText = defaultText ?? "";
                _showSaveAsScene = showSaveAsScene;
                _saveAsScene = defaultSaveAsScene;
                _confirmed = false;

                titleContent = new GUIContent(_windowTitle);
            }

            public void CreateGUI()
            {
                RebuildUI();
            }

            private void RebuildUI()
            {
                var root = rootVisualElement;

                root.Clear();
                var treeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.InputDialogUxmlPath);
                if (treeAsset == null)
                {
                    BuildMissingAssetFallback(root, $"未找到 UXML: {TodoPackagePaths.InputDialogUxmlPath}");
                    return;
                }

                treeAsset.CloneTree(root);

                BindUIElements(root);
                ApplyStateToUI();

                root.UnregisterCallback<KeyDownEvent>(OnKeyDown);
                root.RegisterCallback<KeyDownEvent>(OnKeyDown);
                root.schedule.Execute(() =>
                {
                    _inputField?.Focus();
                    _inputField?.SelectAll();
                }).ExecuteLater(1);
            }

            private void BindUIElements(VisualElement root)
            {
                _promptLabel = root.Q<Label>(PromptLabelName);
                _inputField = root.Q<TextField>(InputFieldName);
                _confirmButton = root.Q<Button>(ConfirmButtonName);
                _cancelButton = root.Q<Button>(CancelButtonName);
                _saveAsSceneRow = root.Q<VisualElement>(SaveAsSceneRowName);
                _saveAsSceneToggle = root.Q<Toggle>(SaveAsSceneToggleName);

                if (_inputField != null)
                {
                    _inputField.RegisterValueChangedCallback(evt => { _inputText = evt.newValue; });
                }

                if (_saveAsSceneToggle != null)
                {
                    _saveAsSceneToggle.RegisterValueChangedCallback(evt => { _saveAsScene = evt.newValue; });
                }

                if (_confirmButton != null)
                {
                    _confirmButton.clicked += ConfirmAndClose;
                }

                if (_cancelButton != null)
                {
                    _cancelButton.clicked += CancelAndClose;
                }
            }

            private void ApplyStateToUI()
            {
                if (_promptLabel != null)
                {
                    _promptLabel.text = _prompt;
                }

                if (_inputField != null)
                {
                    _inputField.value = _inputText;
                }

                if (_saveAsSceneRow != null)
                {
                    _saveAsSceneRow.style.display = _showSaveAsScene ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (_saveAsSceneToggle != null)
                {
                    _saveAsSceneToggle.SetValueWithoutNotify(_saveAsScene);
                }
            }

            private void BuildMissingAssetFallback(VisualElement root, string message)
            {
                root.Add(new Label(message));
                var button = new Button(Close) { text = "关闭" };
                root.Add(button);
            }

            private void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmAndClose();
                    evt.StopImmediatePropagation();
                    return;
                }

                if (evt.keyCode == KeyCode.Escape)
                {
                    CancelAndClose();
                    evt.StopImmediatePropagation();
                }
            }

            private void ConfirmAndClose()
            {
                _inputText = _inputField?.value ?? _inputText;
                _saveAsScene = _saveAsSceneToggle?.value ?? _saveAsScene;
                _confirmed = true;
                Close();
            }

            private void CancelAndClose()
            {
                _confirmed = false;
                Close();
            }
        }
    }
}
