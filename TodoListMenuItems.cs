using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor.TodoList
{
    /// <summary>
    /// TodoList右键菜单
    /// </summary>
    public static class TodoListMenuItems
    {
        private const string MenuPrefix = "Assets/Add to TodoList/";
        private const string HierarchyMenuPrefix = "GameObject/Add to TodoList/";

        #region Project窗口右键菜单 - 资源引用

        [MenuItem(MenuPrefix + "添加资源引用 (待修复)", false, 10)]
        private static void AddAssetPending()
        {
            AddSelectedAssetAsTodo(TodoStatus.Pending);
        }

        [MenuItem(MenuPrefix + "添加资源引用 (待修复)", true)]
        private static bool ValidateAddAssetPending()
        {
            return ValidateAssetSelection();
        }

        [MenuItem(MenuPrefix + "添加资源引用 (待验证)", false, 11)]
        private static void AddAssetVerify()
        {
            AddSelectedAssetAsTodo(TodoStatus.Verify);
        }

        [MenuItem(MenuPrefix + "添加资源引用 (待验证)", true)]
        private static bool ValidateAddAssetVerify()
        {
            return ValidateAssetSelection();
        }

        private static void AddSelectedAssetAsTodo(TodoStatus status)
        {
            var selection = Selection.activeObject;
            if (selection == null) return;

            var guid = Selection.assetGUIDs[0];
            var defaultDescription = $"[{selection.name}] - 请输入问题描述";

            string newDescription = EditorInputDialog.Show("添加待办", "请输入描述:", defaultDescription);
            if (string.IsNullOrEmpty(newDescription))
                return;

            // 使用统一的添加方法，会创建独立的ScriptableObject
            TodoListWindow.AddTodoItem(newDescription, TodoType.Asset, guid, null, null);
        }

        private static bool ValidateAssetSelection()
        {
            return Selection.activeObject != null && !string.IsNullOrEmpty(Selection.assetGUIDs[0]);
        }

        #endregion

        #region Hierarchy窗口右键菜单 - 场景物体

        [MenuItem(HierarchyMenuPrefix + "添加场景物体 (待修复)", false, 10)]
        private static void AddSceneObjectPending()
        {
            AddSelectedSceneObjectAsTodo(TodoStatus.Pending);
        }

        [MenuItem(HierarchyMenuPrefix + "添加场景物体 (待修复)", true)]
        private static bool ValidateAddSceneObjectPending()
        {
            return ValidateSceneObjectSelection();
        }

        [MenuItem(HierarchyMenuPrefix + "添加场景物体 (待验证)", false, 11)]
        private static void AddSceneObjectVerify()
        {
            AddSelectedSceneObjectAsTodo(TodoStatus.Verify);
        }

        [MenuItem(HierarchyMenuPrefix + "添加场景物体 (待验证)", true)]
        private static bool ValidateAddSceneObjectVerify()
        {
            return ValidateSceneObjectSelection();
        }

        private static void AddSelectedSceneObjectAsTodo(TodoStatus status)
        {
            var selection = Selection.activeGameObject;
            if (selection == null) return;

            var scene = selection.scene;
            if (scene == null)
            {
                EditorUtility.DisplayDialog("错误", "请选择场景中的物体", "确定");
                return;
            }

            var scenePath = scene.path;
            var gameObjectPath = GetGameObjectPath(selection);
            var defaultDescription = $"[{selection.name}] - 请输入问题描述";

            string newDescription = EditorInputDialog.Show("添加待办", "请输入描述:", defaultDescription);
            if (string.IsNullOrEmpty(newDescription))
                return;

            // 使用统一的添加方法，会创建独立的ScriptableObject
            TodoListWindow.AddTodoItem(newDescription, TodoType.SceneObject, null, scenePath, gameObjectPath);
        }

        private static bool ValidateSceneObjectSelection()
        {
            return Selection.activeGameObject != null;
        }

        /// <summary>
        /// 获取GameObject在场景中的完整路径
        /// </summary>
        private static string GetGameObjectPath(GameObject obj)
        {
            if (obj.transform.parent == null)
                return obj.name;

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        #endregion

        #region 添加纯文本待办

        [MenuItem("GameConsole/TodoList/添加纯文本待办", false, 101)]
        private static void AddTextTodo()
        {
            string description = EditorInputDialog.Show("添加纯文本待办", "请输入描述:", "");
            if (string.IsNullOrEmpty(description))
                return;

            // 使用统一的添加方法，会创建独立的ScriptableObject
            TodoListWindow.AddTodoItem(description, TodoType.Text, null, null, null);
        }

        #endregion

        #region 输入对话框辅助类

        /// <summary>
        /// 简单的输入对话框
        /// </summary>
        private class EditorInputDialog : EditorWindow
        {
            private string _inputText;
            private string _prompt;
            private bool _initialized = false;

            public static string Show(string title, string prompt, string defaultText = "")
            {
                var window = CreateInstance<EditorInputDialog>();
                window.titleContent = new GUIContent(title);
                window._prompt = prompt;
                window._inputText = defaultText;
                window._initialized = false;

                var rect = new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f - 75, 400, 150);
                window.position = rect;
                window.ShowModal();
                return window._inputText;
            }

            private void OnGUI()
            {
                // 首次初始化
                if (!_initialized)
                {
                    _initialized = true;
                }

                // 回车键关闭
                if (Event.current.type == EventType.KeyUp &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                {
                    Close();
                    return;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(_prompt, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(10);

                _inputText = EditorGUILayout.TextField(_inputText, GUILayout.Height(25));

                EditorGUILayout.Space(10);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("确定", GUILayout.Width(80)))
                    {
                        Close();
                    }

                    if (GUILayout.Button("取消", GUILayout.Width(80)))
                    {
                        _inputText = null;
                        Close();
                    }

                    GUILayout.FlexibleSpace();
                }
            }
        }

        #endregion
    }
}