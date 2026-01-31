using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Editor.TodoList
{
    /// <summary>
    /// TodoList编辑器窗口
    /// 菜单路径: GameConsole/TodoList
    /// </summary>
    public class TodoListWindow : OdinEditorWindow
    {
        [MenuItem("GameConsole/TodoList/Window", priority = 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<TodoListWindow>("TodoList");
            window.Focus();
        }

        private TodoListConfig _config;
        private TodoStatus _filterStatus = TodoStatus.Pending;
        private bool _showOnlyPending = true;
        private Vector2 _scrollPosition;
        private string _searchText = "";

        protected override void OnImGUI()
        {
            if (_config == null)
            {
                LoadConfig();
            }

            EditorGUILayout.Space(10);
            DrawToolbar();
            EditorGUILayout.Space(5);
            DrawFilterBar();
            EditorGUILayout.Space(10);
            DrawTodoList();
        }

        private void LoadConfig()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(TodoListConfig)}");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _config = AssetDatabase.LoadAssetAtPath<TodoListConfig>(path);
            }
            else
            {
                _config = ScriptableObject.CreateInstance<TodoListConfig>();
                var folderPath = "Assets/Editor/TodoList";
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                AssetDatabase.CreateAsset(_config, $"{folderPath}/TodoListConfig.asset");
                AssetDatabase.SaveAssets();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("TodoList 待办事项管理", EditorStyles.boldLabel, GUILayout.Height(25));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("添加", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    AddNewTodoItem();
                }

                if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    RefreshTodoItems();
                }
            }
        }

        /// <summary>
        /// 添加新的文本类型待办事项
        /// </summary>
        private void AddNewTodoItem()
        {
            if (_config == null)
            {
                LoadConfig();
            }

            // 创建独立的ScriptableObject文件
            var item = ScriptableObject.CreateInstance<TodoItem>();
            item.Description = "新待办事项";
            item.Status = TodoStatus.Pending;
            item.Type = TodoType.Text;
            item.CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            item.UpdateTime = "";
            item.AssetGuid = "";
            item.ScenePath = "";
            item.GameObjectPath = "";
            item.PrefabChildPath = "";

            // 生成唯一文件名
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"Todo_{timestamp}_New";

            // 保存到独立文件
            var folderPath = "Assets/Editor/TodoList/Items";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var assetPath = $"{folderPath}/{fileName}.asset";
            AssetDatabase.CreateAsset(item, assetPath);

            // 添加到配置列表
            _config.TodoItems.Add(item);
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TodoList] 已添加新的待办事项: {item.Description}");
            Repaint();
        }

        /// <summary>
        /// 刷新TodoItem列表，重新扫描项目中所有的TodoItem资源
        /// </summary>
        private void RefreshTodoItems()
        {
            if (_config == null)
            {
                LoadConfig();
            }

            // 清空现有列表
            _config.TodoItems.Clear();

            // 查找项目中所有的TodoItem资源
            var guids = AssetDatabase.FindAssets($"t:{nameof(TodoItem)}");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<TodoItem>(path);

                if (item != null && !_config.TodoItems.Contains(item))
                {
                    _config.TodoItems.Add(item);
                }
            }

            // 保存配置
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TodoList] 刷新完成，共找到 {_config.TodoItems.Count} 个待办事项");
            Repaint();
        }

        private void DrawFilterBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("筛选:", GUILayout.Width(40));

                _filterStatus = (TodoStatus)EditorGUILayout.EnumPopup(_filterStatus, GUILayout.Width(80));

                _showOnlyPending = EditorGUILayout.ToggleLeft("仅显示当前状态", _showOnlyPending, GUILayout.Width(110));

                GUILayout.Label("搜索:", GUILayout.Width(40));
                _searchText = EditorGUILayout.TextField(_searchText, GUILayout.Width(200));

                GUILayout.FlexibleSpace();

                var totalCount = _config?.TodoItems.Count ?? 0;
                var pendingCount = _config?.TodoItems.Count(x => x != null && x.Status == TodoStatus.Pending) ?? 0;
                var verifyCount = _config?.TodoItems.Count(x => x != null && x.Status == TodoStatus.Verify) ?? 0;
                var fixedCount = _config?.TodoItems.Count(x => x != null && x.Status == TodoStatus.Fixed) ?? 0;
                var suspendedCount = _config?.TodoItems.Count(x => x != null && x.Status == TodoStatus.Suspended) ?? 0;
                var buildCheckCount = _config?.TodoItems.Count(x => x != null && x.Status == TodoStatus.BuildCheck) ?? 0;

                EditorGUILayout.LabelField(
                    $"总计: {totalCount} | 待修复: {pendingCount} | 待验证: {verifyCount}  | 打包检查: {buildCheckCount}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawTodoList()
        {
            if (_config == null || _config.TodoItems == null || _config.TodoItems.Count == 0 ||
                _config.TodoItems.All(x => x == null))
            {
                EditorGUILayout.HelpBox(
                    "暂无待办事项\n可通过右键菜单添加:\n- Project窗口: 右键资产 -> Add to TodoList\n- Hierarchy窗口: 右键物体 -> Add to TodoList",
                    MessageType.Info);
                return;
            }

            var filteredItems = _config.TodoItems.Where(x => x != null).Where(x =>
            {
                if (_showOnlyPending && x.Status != _filterStatus)
                    return false;
                if (!string.IsNullOrEmpty(_searchText) &&
                    !string.IsNullOrEmpty(x.Description) &&
                    !x.Description.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            }).OrderBy(x => x.Status).ThenByDescending(x => GetSortTime(x)).ToList();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            {
                for (int i = 0; i < filteredItems.Count; i++)
                {
                    DrawTodoItem(filteredItems[i]);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static string GetSortTime(TodoItem item)
        {
            // 优先使用更新时间，如果为空则使用创建时间
            return !string.IsNullOrEmpty(item.UpdateTime) ? item.UpdateTime : item.CreateTime;
        }

        private void DrawTodoItem(TodoItem item)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = GetStatusColor(item.Status);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                // 标题行
                DrawTodoItemHeader(item);

                // 详细信息
                DrawTodoItemDetails(item);

                // 评论区域
                DrawCommentsSection(item);
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawTodoItemHeader(TodoItem item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 状态切换
                var newStatus = (TodoStatus)EditorGUILayout.EnumPopup(item.Status, GUILayout.Width(80));
                if (newStatus != item.Status)
                {
                    Undo.RecordObject(item, "Change Todo Status");
                    item.Status = newStatus;
                    item.UpdateUpdateTime();
                    EditorUtility.SetDirty(item);
                }

                // 类型图标
                string typeIcon = item.Type switch
                {
                    TodoType.Asset => "Prefab Icon",
                    TodoType.SceneObject => "SceneAsset Icon",
                    TodoType.Text => "d_TextAsset Icon",
                    TodoType.PrefabObject => "Prefab Icon",
                    _ => "d_Invalid"
                };
                GUILayout.Label(EditorGUIUtility.IconContent(typeIcon), GUILayout.Width(20), GUILayout.Height(20));

                // 可编辑的描述
                EditorGUI.BeginChangeCheck();
                var newDescription = EditorGUILayout.TextField(item.Description ?? "", EditorStyles.boldLabel, GUILayout.MinWidth(200), GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(item, "Edit Todo Description");
                    item.Description = newDescription;
                    item.UpdateUpdateTime();
                    EditorUtility.SetDirty(item);
                }

                // 时间（优先显示更新时间）
                var displayTime = !string.IsNullOrEmpty(item.UpdateTime) ? item.UpdateTime : item.CreateTime;
                var timeLabel = !string.IsNullOrEmpty(item.UpdateTime) ? $"更新: {displayTime}" : displayTime;
                GUILayout.Label(timeLabel, EditorStyles.miniLabel, GUILayout.Width(140));

                // 删除按钮
                if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    DeleteTodoItemWithUndo(item);
                    GUI.backgroundColor = Color.white;
                    return;
                }
            }
        }

        private void DrawTodoItemDetails(TodoItem item)
        {
            if (item.Type == TodoType.Text)
                return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (item.Type == TodoType.Asset)
                {
                    var asset = item.GetAsset();
                    if (asset != null)
                    {
                        // 如果是Texture类型的资源，显示图片预览
                        if (asset is Texture2D texture)
                        {
                            var previewHeight = 60f;
                            var aspect = (float)texture.width / texture.height;
                            var previewWidth = previewHeight * aspect;
                            GUILayout.Label($"资源: {asset.name} ({texture.width}x{texture.height})", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            GUILayout.Box(texture, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                            GUILayout.Space(10);
                        }
                        else if (asset is Sprite sprite)
                        {
                            var previewHeight = 60f;
                            var rect = sprite.textureRect;
                            var aspect = rect.width / rect.height;
                            var previewWidth = previewHeight * aspect;
                            GUILayout.Label($"资源: {asset.name} ({sprite.texture.width}x{sprite.texture.height})", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            GUILayout.Box(sprite.texture, GUILayout.Width(previewWidth), GUILayout.Height(previewHeight));
                            GUILayout.Space(10);
                        }
                        else
                        {
                            GUILayout.Label($"资源: {asset.name}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                        }

                        if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(40)))
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                    else
                    {
                        GUILayout.Label($"资源: [已删除]", EditorStyles.miniLabel);
                    }
                }
                else if (item.Type == TodoType.SceneObject)
                {
                    GUILayout.Label($"场景: {Path.GetFileNameWithoutExtension(item.ScenePath)}", EditorStyles.miniLabel);
                    GUILayout.Label($" | {item.GameObjectPath}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        item.Locate();
                    }
                }
                else if (item.Type == TodoType.PrefabObject)
                {
                    var prefabAsset = item.GetAsset();
                    if (prefabAsset != null)
                    {
                        GUILayout.Label($"预制体: {prefabAsset.name}", EditorStyles.miniLabel);
                        GUILayout.Label($" | {item.PrefabChildPath}", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(40)))
                        {
                            item.Locate();
                        }
                    }
                    else
                    {
                        GUILayout.Label($"预制体: [已删除]", EditorStyles.miniLabel);
                        GUILayout.Label($" | {item.PrefabChildPath}", EditorStyles.miniLabel);
                    }
                }
            }
        }

        private void DrawCommentsSection(TodoItem item)
        {
            var commentCount = item.Comments?.Count ?? 0;

            // 没有评论的情况 - 直接显示添加评论输入框
            if (commentCount == 0)
            {
                DrawAddCommentInput(item);
                return;
            }

            // 有一条评论 - 直接显示该评论和添加评论输入框
            if (commentCount == 1)
            {
                DrawCommentItem(item, item.Comments[0], 0);
                DrawAddCommentInput(item);
                return;
            }

            // 多于一条评论 - 显示最新一条和折叠按钮
            // 显示最新一条评论
            DrawCommentItem(item, item.Comments[commentCount - 1], commentCount - 1);

            // 折叠/展开按钮
            var expandText = item.ExpandComments ? $"收起 ({commentCount - 1} 条旧评论)" : $"展开 ({commentCount - 1} 条旧评论)";

            if (GUILayout.Button(expandText, EditorStyles.miniButton))
            {
                item.ExpandComments = !item.ExpandComments;
            }

            if (item.ExpandComments)
            {
                // 显示旧评论（从倒数第二条开始倒序显示）
                for (int i = commentCount - 2; i >= 0; i--)
                {
                    DrawCommentItem(item, item.Comments[i], i);
                }
            }

            // 添加评论输入框（总是在折叠内部）
            DrawAddCommentInput(item);
        }

        private void DrawAddCommentInput(TodoItem item)
        {
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("添加评论:", GUILayout.Width(60));
                item.EditingComment = EditorGUILayout.TextField(item.EditingComment);

                if (GUILayout.Button("发送", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    if (!string.IsNullOrWhiteSpace(item.EditingComment))
                    {
                        Undo.RecordObject(item, "Add Todo Comment");
                        item.AddComment(item.EditingComment);
                        item.EditingComment = "";
                        EditorUtility.SetDirty(item);
                    }
                }
            }
        }

        private void DrawCommentItem(TodoItem item, TodoComment comment, int index)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    // 评论头部：作者 + 时间
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"{comment.Author} - {comment.Time}", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();

                        // 删除评论按钮
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20)))
                        {
                            Undo.RecordObject(item, "Delete Todo Comment");
                            item.RemoveCommentAt(index);
                            EditorUtility.SetDirty(item);
                            return;
                        }
                    }

                    // 评论内容
                    EditorGUILayout.LabelField(comment.Content, EditorStyles.wordWrappedLabel);
                }
            }
        }

        private Color GetStatusColor(TodoStatus status)
        {
            return status switch
            {
                TodoStatus.Pending => new Color(1f, 0.9f, 0.8f),
                TodoStatus.Verify => new Color(0.9f, 1f, 0.8f),
                TodoStatus.Fixed => new Color(0.9f, 0.95f, 0.9f),
                TodoStatus.Suspended => new Color(0.85f, 0.85f, 0.85f),
                TodoStatus.BuildCheck => new Color(1f, 0.7f, 0.7f),
                _ => Color.white
            };
        }

        private void DeleteTodoItem(TodoItem item)
        {
            // 从配置列表中移除
            _config.TodoItems.Remove(item);
            EditorUtility.SetDirty(_config);

            // 删除资源文件
            var assetPath = AssetDatabase.GetAssetPath(item);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.SaveAssets();
            LoadConfig();
        }

        /// <summary>
        /// 支持撤销的删除操作
        /// </summary>
        private void DeleteTodoItemWithUndo(TodoItem item)
        {
            // 记录资产文件，以便撤销时恢复
            var assetPath = AssetDatabase.GetAssetPath(item);
            if (!string.IsNullOrEmpty(assetPath))
            {
                // 将资产移到临时位置，撤销时可以恢复
                var tempPath = $"Temp/{item.name}_{System.Guid.NewGuid()}.asset";
                Directory.CreateDirectory("Temp");
                File.Copy(assetPath, tempPath, true);

                // 保存临时路径供回调使用
                _tempAssetPath = assetPath;
                _tempBackupPath = tempPath;
                _deletedItemName = item.Description;
                _deletedItemGuid = AssetDatabase.GUIDFromAssetPath(assetPath).ToString();

                // 注册撤销回调
                Undo.undoRedoPerformed += OnUndoRedoPerformed;
            }

            // 执行删除
            DeleteTodoItem(item);

            Debug.Log($"[TodoList] 已删除待办: {item.Description} (可使用 Ctrl+Z 撤销)");
        }

        // 临时路径字段（用于撤销）
        private static string _tempAssetPath;
        private static string _tempBackupPath;
        private static string _deletedItemName;
        private static string _deletedItemGuid;

        private void OnUndoRedoPerformed()
        {
            // 检查是否是我们需要恢复的删除操作
            if (!string.IsNullOrEmpty(_tempAssetPath) && !string.IsNullOrEmpty(_tempBackupPath))
            {
                // 检查资产是否存在，不存在则从临时位置恢复
                if (!File.Exists(_tempAssetPath) && File.Exists(_tempBackupPath))
                {
                    // 恢复资产文件
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_tempAssetPath));
                    File.Copy(_tempBackupPath, _tempAssetPath, true);
                    File.Delete(_tempBackupPath);
                    AssetDatabase.Refresh();

                    // 重新加载恢复的资产
                    var restoredItem = AssetDatabase.LoadAssetAtPath<TodoItem>(_tempAssetPath);

                    // 将恢复的资产添加回配置列表
                    if (restoredItem != null && _config != null)
                    {
                        // 检查是否已存在（避免重复添加）
                        if (!_config.TodoItems.Contains(restoredItem))
                        {
                            _config.TodoItems.Add(restoredItem);
                            EditorUtility.SetDirty(_config);
                            AssetDatabase.SaveAssetIfDirty(_config);
                        }
                    }

                    // 清理临时路径
                    _tempAssetPath = null;
                    _tempBackupPath = null;
                    _deletedItemGuid = null;

                    // 重新加载配置
                    LoadConfig();

                    Debug.Log($"[TodoList] 已恢复待办: {_deletedItemName}");
                    _deletedItemName = null;
                }
            }
        }

        /// <summary>
        /// 恢复已删除的TodoItem
        /// </summary>
        private void RestoreTodoItem(TodoUndoData data)
        {
            var folderPath = "Assets/Editor/TodoList/Items";
            Directory.CreateDirectory(folderPath);

            // 创建新的TodoItem
            var item = ScriptableObject.CreateInstance<TodoItem>();
            item.Description = data.Description;
            item.Status = data.Status;
            item.Type = data.Type;
            item.CreateTime = data.CreateTime;
            item.UpdateTime = data.UpdateTime;
            item.AssetGuid = data.AssetGuid;
            item.ScenePath = data.ScenePath;
            item.GameObjectPath = data.GameObjectPath;

            // 恢复评论
            if (data.Comments != null && data.Comments.Count > 0)
            {
                item.Comments = new System.Collections.Generic.List<TodoComment>();
                foreach (var commentData in data.Comments)
                {
                    item.Comments.Add(new TodoComment
                    {
                        Content = commentData.Content,
                        Time = commentData.Time,
                        Author = commentData.Author
                    });
                }
            }

            // 保存资产文件
            var fileName = $"{System.IO.Path.GetFileNameWithoutExtension(data.OriginalAssetPath)}";
            var newAssetPath = $"{folderPath}/{fileName}.asset";

            // 如果文件已存在，添加时间戳避免冲突
            if (File.Exists(newAssetPath))
            {
                var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                newAssetPath = $"{folderPath}/{fileName}_{timestamp}.asset";
            }

            AssetDatabase.CreateAsset(item, newAssetPath);
            _config.TodoItems.Add(item);
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TodoList] 已恢复待办: {item.Description}");
        }

        /// <summary>
        /// TodoItem撤销数据
        /// </summary>
        [System.Serializable]
        private class TodoUndoData
        {
            public string Description;
            public TodoStatus Status;
            public TodoType Type;
            public string CreateTime;
            public string UpdateTime;
            public string AssetGuid;
            public string ScenePath;
            public string GameObjectPath;
            public string OriginalAssetPath;
            public string OriginalGuid;
            public System.Collections.Generic.List<TodoCommentData> Comments;
        }

        /// <summary>
        /// 评论撤销数据
        /// </summary>
        [System.Serializable]
        private class TodoCommentData
        {
            public string Content;
            public string Time;
            public string Author;
        }

        /// <summary>
        /// 添加待办事项（由菜单调用）
        /// </summary>
        public static void AddTodoItem(string description, TodoType type, string assetGuid = null,
            string scenePath = null, string gameObjectPath = null, string prefabChildPath = null)
        {
            var window = GetWindow<TodoListWindow>("TodoList");
            window.Show();
            window.Focus();

            if (window._config == null)
            {
                window.LoadConfig();
            }

            // 创建独立的ScriptableObject文件
            var item = ScriptableObject.CreateInstance<TodoItem>();
            item.Description = description;
            item.Status = TodoStatus.Pending;
            item.Type = type;
            item.CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            item.UpdateTime = "";  // 初始为空，表示未更新过
            item.AssetGuid = assetGuid ?? "";
            item.ScenePath = scenePath ?? "";
            item.GameObjectPath = gameObjectPath ?? "";
            item.PrefabChildPath = prefabChildPath ?? "";

            // 生成唯一文件名
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeName = string.Join("_", description.Split(Path.GetInvalidFileNameChars()));
            if (safeName.Length > 20) safeName = safeName.Substring(0, 20);
            var fileName = $"Todo_{timestamp}_{safeName}";

            // 保存到独立文件
            var folderPath = "Assets/Editor/TodoList/Items";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            var assetPath = $"{folderPath}/{fileName}.asset";
            AssetDatabase.CreateAsset(item, assetPath);

            // 添加到配置列表
            window._config.TodoItems.Add(item);
            EditorUtility.SetDirty(window._config);
            AssetDatabase.SaveAssets();

            window.Repaint();
        }
    }
}