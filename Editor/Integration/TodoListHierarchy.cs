using UnityEditor;
using UnityEngine;

namespace Editor.TodoList
{
    /// <summary>
    /// Hierarchy窗口的TODO按钮
    /// 只在拖拽时显示，拖拽物体/资产到按钮位置添加待办
    /// </summary>
    [InitializeOnLoad]
    public static class TodoListHierarchy
    {
        private static Rect _dragButtonRect;

        static TodoListHierarchy()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            // 检查是否有拖拽内容
            var isDragging = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;

            // 只在拖拽时显示
            if (!isDragging)
                return;

            // 获取Hierarchy窗口
            var hierarchyWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            EditorWindow hierarchyWindow = null;
            foreach (var window in hierarchyWindows)
            {
                if (window != null && window.titleContent.text.Contains("Hierarchy"))
                {
                    hierarchyWindow = window;
                    break;
                }
            }

            if (hierarchyWindow == null)
                return;

            var windowPos = hierarchyWindow.position;
            if (windowPos.width == 0 || windowPos.height == 0)
                return;

            // 将按钮固定在窗口底部（非常靠近底部边缘）
            var buttonWidth = 80f;
            var buttonHeight = 30f;
            var buttonX = windowPos.width - buttonWidth - 10;
            var buttonY = windowPos.height - buttonHeight - 20;
            _dragButtonRect = new Rect(buttonX, buttonY, buttonWidth, buttonHeight);

            // 检测拖拽
            HandleDragAndDrop(_dragButtonRect);

            // 绘制按钮
            var currentEvent = Event.current;
            if (currentEvent != null && currentEvent.type == EventType.Repaint)
            {
                // 绘制高亮背景
                var bgColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                GUI.Box(_dragButtonRect, "");
                GUI.backgroundColor = bgColor;

                // 绘制文字
                var style = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                GUI.Label(_dragButtonRect, "拖拽至此", style);
            }
        }

        private static void HandleDragAndDrop(Rect buttonRect)
        {
            var currentEvent = Event.current;

            if (currentEvent == null)
                return;

            // DragUpdated - 检查是否可以接受拖拽
            if (currentEvent.type == EventType.DragUpdated)
            {
                if (buttonRect.Contains(currentEvent.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    currentEvent.Use();
                }
            }
            // DragPerform - 执行拖拽操作
            else if (currentEvent.type == EventType.DragPerform)
            {
                if (buttonRect.Contains(currentEvent.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    HandleDroppedObjects();
                    currentEvent.Use();
                }
            }
        }

        private static void HandleDroppedObjects()
        {
            var draggedObjects = DragAndDrop.objectReferences;
            if (draggedObjects == null || draggedObjects.Length == 0)
                return;

            foreach (var obj in draggedObjects)
            {
                if (obj == null)
                    continue;

                // 场景中的GameObject（包括Prefab Mode中的子物体）
                if (obj is GameObject gameObject)
                {
                    // 检查是否在Prefab Mode中（预制体编辑模式）
                    var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                    if (prefabStage != null && gameObject.scene != null && gameObject.scene.name == prefabStage.scene.name)
                    {
                        // 这是预制体内部的子物体
                        AddPrefabChildTodo(gameObject, prefabStage);
                    }
                    // 普通场景中的GameObject
                    else if (gameObject.scene != null && gameObject.scene.name != null)
                    {
                        AddSceneObjectTodo(gameObject);
                    }
                }
                // Project中的资产
                else if (obj is UnityEngine.Object)
                {
                    var assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        AddAssetTodo(obj, assetPath);
                    }
                }
            }
        }

        private static void AddPrefabChildTodo(GameObject obj, UnityEditor.SceneManagement.PrefabStage prefabStage)
        {
            var prefabAssetPath = prefabStage.prefabAssetPath;
            var prefabGuid = AssetDatabase.GUIDFromAssetPath(prefabAssetPath).ToString();

            // 获取相对于预制体根节点的路径（排除根节点名称）
            var childPath = GetPrefabChildPath(obj, prefabStage.prefabContentsRoot);

            var prefabName = System.IO.Path.GetFileNameWithoutExtension(prefabAssetPath);
            var defaultDescription = $"[{prefabName}/{obj.name}] - 请输入问题描述";

            var input = TodoInputDialog.ShowWithOptions("添加预制体子物体待办", "请输入描述:", defaultDescription);
            if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Text))
                return;

            TodoListWindow.AddTodoItem(input.Text.Trim(), TodoType.PrefabObject, prefabGuid, null, null, childPath);
        }

        private static string GetPrefabChildPath(GameObject obj, GameObject prefabRoot)
        {
            if (obj == null || prefabRoot == null)
                return "";

            // 如果就是根节点本身
            if (obj == prefabRoot)
                return "";

            var path = obj.name;
            var current = obj.transform.parent;

            // 向上遍历，直到到达预制体根节点的子节点
            while (current != null && current.gameObject != prefabRoot)
            {
                path = current.name + "/" + path;
                current = current.parent;

                // 安全检查：防止无限循环
                if (current != null && current.parent == null && current.gameObject != prefabRoot)
                {
                    // 超出了预制体范围
                    break;
                }
            }

            return path;
        }

        private static void AddSceneObjectTodo(GameObject obj)
        {
            var scene = obj.scene;
            var gameObjectPath = GetGameObjectPath(obj);
            var defaultDescription = $"[{obj.name}] - 请输入问题描述";

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

        private static void AddAssetTodo(UnityEngine.Object asset, string assetPath)
        {
            var guid = AssetDatabase.GUIDFromAssetPath(assetPath).ToString();
            var defaultDescription = $"[{asset.name}] - 请输入问题描述";

            var input = TodoInputDialog.ShowWithOptions("添加待办", "请输入描述:", defaultDescription);
            if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Text))
                return;

            TodoListWindow.AddTodoItem(input.Text.Trim(), TodoType.Asset, guid);
        }

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

        // 输入弹窗由 Editor.TodoList.TodoInputDialog 统一实现
    }
}
