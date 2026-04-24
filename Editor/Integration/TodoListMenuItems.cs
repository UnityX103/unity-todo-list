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

            var input = TodoInputDialog.ShowWithOptions("添加待办", "请输入描述:", defaultDescription);
            if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Text))
                return;

            // 使用统一的添加方法，会创建独立的ScriptableObject
            TodoListWindow.AddTodoItem(input.Text.Trim(), TodoType.Asset, guid);
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

            var gameObjectPath = GetGameObjectPath(selection);
            var defaultDescription = $"[{selection.name}] - 请输入问题描述";

            var input = TodoInputDialog.ShowWithOptions("添加待办", "请输入描述:", defaultDescription,
                showSaveAsScene: true);
            if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Text))
                return;

            if (!TodoSceneReferenceUtility.TryGetSceneReferenceForTodo(scene, input.SaveAsScene,
                    out var targetScenePath, out var targetSceneGuid, out var isClonedSceneReference))
                return;

            TodoListWindow.AddTodoItem(input.Text.Trim(), TodoType.SceneObject, null, targetScenePath, gameObjectPath,
                sceneGuid: targetSceneGuid, isClonedSceneReference: isClonedSceneReference);
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

        #region 输入对话框辅助类

        // 输入弹窗由 Editor.TodoList.TodoInputDialog 统一实现

        #endregion
    }
}
