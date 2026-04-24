using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.TodoList
{
    /// <summary>
    /// TodoList编辑器窗口（UI Toolkit 版本）
    /// 菜单路径: GameConsole/工具/TodoList
    /// </summary>
    public class TodoListWindow : EditorWindow
    {
        private static readonly List<string> StatusChoices = new List<string>
            { "待修复", "待验证", "已修复", "搁置", "打包检查" };

        private const float MiniH = 22f;
        private const float PreviewH = 60f;
        private const string WorkNoteKey = "Editor.TodoList.CurrentWorkNote";
        private const string WorkNoteTimeKey = "Editor.TodoList.CurrentWorkNoteTime";
        private const string InitialScanSessionKey = "Editor.TodoList.InitialScanCompleted";

        // ---------- 数据状态 ----------
        private TodoListConfig _config;
        private TodoStatus _filterStatus = TodoStatus.Pending;
        private bool _showOnlyPending;
        private string _searchText = string.Empty;
        private string _currentWorkNote = string.Empty;
        private string _currentWorkNoteTime = string.Empty;
        private bool _workNoteLoaded;
        private bool _uiBuilt;

        // ---------- UI 引用 ----------
        private VisualTreeAsset _itemCardAsset;
        private Label _workTimeLabel;
        private TextField _workNoteField;
        private DropdownField _filterStatusField;
        private Toggle _showOnlyPendingToggle;
        private TextField _searchField;
        private Label _statsLabel;
        private ScrollView _listScrollView;
        private VisualElement _listContainer;

        // ---------- 撤销临时字段 ----------
        private static string _tempAssetPath;
        private static string _tempBackupPath;
        private static string _deletedItemName;
        private static string _deletedItemGuid;
        private static readonly List<DeletedAssetBackup> _deletedAssetBackups = new List<DeletedAssetBackup>();

        public readonly struct WorkNoteState
        {
            public WorkNoteState(string note, string updateTime)
            {
                Note = note ?? string.Empty;
                UpdateTime = updateTime ?? string.Empty;
            }

            public string Note { get; }
            public string UpdateTime { get; }
        }

        // ==========================================================
        // 生命周期
        // ==========================================================

        [MenuItem("GameConsole/工具/TodoList", priority = 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<TodoListWindow>("TodoList");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EnsureWorkNoteLoaded();
            LoadConfig();
        }

        private void OnDisable()
        {
            // 不在此处退订 Undo 回调：若用户删除条目后关闭窗口，静态 _temp* 字段仍有效，
            // 让 OnUndoRedoPerformed 在恢复成功后自行退订，与原 IMGUI 版行为一致。
        }

        public void CreateGUI()
        {
            EnsureWorkNoteLoaded();
            LoadConfig();
            BuildUI();
            _uiBuilt = true;

            if (ShouldRunInitialScan())
            {
                RefreshTodoItems();
            }
            else
            {
                RebuildList();
            }
        }

        // ==========================================================
        // UI 构建
        // ==========================================================

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();

            // 根容器基础内联布局（确保可靠，不依赖 USS 加载时机）
            root.style.flexGrow = 1f;
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            // 将所有 USS 显式加载到根节点，保证样式对全部子元素生效
            LoadUssToRoot(root, TodoPackagePaths.WindowUssPath);
            LoadUssToRoot(root, TodoPackagePaths.WorkNoteUssPath);
            LoadUssToRoot(root, TodoPackagePaths.ToolbarUssPath);
            LoadUssToRoot(root, TodoPackagePaths.FilterBarUssPath);
            LoadUssToRoot(root, TodoPackagePaths.ItemCardUssPath);

            // 加载窗口骨架布局（WorkNoteContainer / ToolbarContainer / FilterBarContainer / ListScrollView）
            var windowTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.WindowUxmlPath);
            if (windowTree == null)
            {
                Debug.LogError($"[TodoList] 无法加载窗口布局: {TodoPackagePaths.WindowUxmlPath}");
                return;
            }
            windowTree.CloneTree(root);

            _listScrollView = root.Q<ScrollView>("ListScrollView");
            _listContainer  = root.Q<VisualElement>("ListContainer");

            // 预加载 ItemCard 模板，RebuildList 中每个 item 直接 Instantiate，避免重复 IO
            _itemCardAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.ItemCardUxmlPath);

            BindWorkNoteSection(root.Q<VisualElement>("WorkNoteContainer"));
            BindToolbarSection(root.Q<VisualElement>("ToolbarContainer"));
            BindFilterBarSection(root.Q<VisualElement>("FilterBarContainer"));
        }

        /// <summary>将指定路径的 USS 样式表加载并附加到目标根节点</summary>
        private static void LoadUssToRoot(VisualElement root, string path)
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (uss != null) root.styleSheets.Add(uss);
            else Debug.LogWarning($"[TodoList] USS 文件未找到: {path}");
        }

        private void BindWorkNoteSection(VisualElement container)
        {
            if (container == null) return;

            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.WorkNoteUxmlPath)?.CloneTree(container);

            // 面板背景/边框由主题决定，运行时写入
            var panel = container.Q<VisualElement>("WorkNotePanel");
            if (panel != null) ApplyPanelChrome(panel);

            _workTimeLabel = container.Q<Label>("WorkNoteTimeLabel");

            _workNoteField = container.Q<TextField>("WorkNoteField");
            if (_workNoteField != null)
            {
                _workNoteField.SetValueWithoutNotify(_currentWorkNote);
                _workNoteField.RegisterValueChangedCallback(evt =>
                {
                    SaveWorkNote(evt.newValue);
                    SyncWorkNoteTimeLabel();
                });
            }

            var clearBtn = container.Q<Button>("WorkNoteClearBtn");
            if (clearBtn != null)
            {
                clearBtn.clicked += () =>
                {
                    SaveWorkNote(string.Empty);
                    _workNoteField?.SetValueWithoutNotify(string.Empty);
                    SyncWorkNoteTimeLabel();
                };
            }

            var addTodoBtn = container.Q<Button>("WorkNoteAddTodoBtn");
            if (addTodoBtn != null)
            {
                addTodoBtn.clicked += AddCurrentWorkNoteToTodo;
            }

            SyncWorkNoteTimeLabel();
        }

        private void BindToolbarSection(VisualElement container)
        {
            if (container == null) return;

            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.ToolbarUxmlPath)?.CloneTree(container);

            var addBtn = container.Q<Button>("ToolbarAddBtn");
            if (addBtn != null) addBtn.clicked += AddNewTodoItem;

            var refreshBtn = container.Q<Button>("ToolbarRefreshBtn");
            if (refreshBtn != null) refreshBtn.clicked += RefreshTodoItems;
        }

        private void BindFilterBarSection(VisualElement container)
        {
            if (container == null) return;

            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TodoPackagePaths.FilterBarUxmlPath)?.CloneTree(container);

            _filterStatusField = container.Q<DropdownField>("FilterStatusField");
            if (_filterStatusField != null)
            {
                _filterStatusField.choices = StatusChoices;
                _filterStatusField.SetValueWithoutNotify(StatusLabel(_filterStatus));
                _filterStatusField.RegisterValueChangedCallback(evt =>
                {
                    _filterStatus = ParseStatus(evt.newValue);
                    RebuildList();
                });
            }

            _showOnlyPendingToggle = container.Q<Toggle>("ShowOnlyPendingToggle");
            if (_showOnlyPendingToggle != null)
            {
                _showOnlyPendingToggle.SetValueWithoutNotify(_showOnlyPending);
                _showOnlyPendingToggle.RegisterValueChangedCallback(evt =>
                {
                    _showOnlyPending = evt.newValue;
                    RebuildList();
                });
            }

            _searchField = container.Q<TextField>("SearchField");
            if (_searchField != null)
            {
                _searchField.SetValueWithoutNotify(_searchText);
                _searchField.RegisterValueChangedCallback(evt =>
                {
                    _searchText = evt.newValue ?? string.Empty;
                    RebuildList();
                });
            }

            _statsLabel = container.Q<Label>("StatsLabel");
        }

        // ==========================================================
        // 列表重建
        // ==========================================================

        private void RebuildList()
        {
            if (_listContainer == null)
                return;

            SyncStatsLabel();

            var savedOffset = _listScrollView?.scrollOffset ?? Vector2.zero;
            _listContainer.Clear();

            var items = GetFilteredItems();
            if (items.Count == 0)
            {
                _listContainer.Add(BuildEmptyState());
            }
            else
            {
                foreach (var item in items)
                    _listContainer.Add(BuildItemElement(item));
            }

            if (_listScrollView != null)
                _listScrollView.scrollOffset = savedOffset;
        }

        private VisualElement BuildEmptyState()
        {
            var panel = new VisualElement();
            ApplyPanelChrome(panel);

            var hasItems = _config?.TodoItems?.Any(x => x != null) == true;
            var label = new Label(hasItems
                ? "当前筛选条件下暂无待办事项"
                : "暂无待办事项\n可通过右键菜单添加:\n- Project窗口: 右键资产 -> Add to TodoList\n- Hierarchy窗口: 右键物体 -> Add to TodoList");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11f;
            panel.Add(label);
            return panel;
        }

        // ==========================================================
        // 单条 TodoItem 构建
        // ==========================================================

        private VisualElement BuildItemElement(TodoItem item)
        {
            item.Comments ??= new List<TodoComment>();

            // 从 UXML 克隆卡片骨架（TemplateContainer 作为外层包装，直接加入列表容器）
            var clone = _itemCardAsset != null
                ? _itemCardAsset.Instantiate()
                : new VisualElement();

            // 卡片主体：应用状态颜色和边框（运行时主题相关，不放 USS）
            var card = clone.Q<VisualElement>("ItemCard") ?? clone;
            card.style.backgroundColor = StatusColor(item.Status);
            ApplyBorder(card, new Color(0f, 0f, 0f, 0.25f));

            BindItemHeader(clone, item);

            var detailsSlot = clone.Q<VisualElement>("ItemDetailsSlot");
            var details = BuildItemDetails(item);
            if (details != null)
            {
                if (detailsSlot != null) detailsSlot.Add(details);
                else card.Add(details);
            }

            var commentsSlot = clone.Q<VisualElement>("ItemCommentsSlot");
            var comments = BuildCommentsSection(item);
            if (commentsSlot != null) commentsSlot.Add(comments);
            else card.Add(comments);

            return clone;
        }

        private void BindItemHeader(VisualElement container, TodoItem item)
        {
            // 状态下拉
            var statusField = container.Q<DropdownField>("ItemStatusField");
            if (statusField != null)
            {
                statusField.choices = StatusChoices;
                statusField.SetValueWithoutNotify(StatusLabel(item.Status));
                statusField.RegisterValueChangedCallback(evt =>
                {
                    var next = ParseStatus(evt.newValue);
                    if (next == item.Status) return;
                    Undo.RecordObject(item, "Change Todo Status");
                    item.Status = next;
                    item.UpdateUpdateTime();
                    EditorUtility.SetDirty(item);
                    RebuildList();
                });
            }

            // 置顶
            var pinToggle = container.Q<Toggle>("ItemPinToggle");
            if (pinToggle != null)
            {
                pinToggle.SetValueWithoutNotify(item.IsPinned);
                pinToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == item.IsPinned) return;
                    Undo.RecordObject(item, "Toggle Todo Pin");
                    item.IsPinned = evt.newValue;
                    item.UpdateUpdateTime();
                    EditorUtility.SetDirty(item);
                    RebuildList();
                });
            }

            // 类型图标（动态插入到占位槽中）
            container.Q<VisualElement>("ItemTypeIconSlot")?.Add(BuildTypeIcon(item.Type));

            // 时间标签（在描述回调中同步更新）
            var timeLabel = container.Q<Label>("ItemTimeLabel");
            if (timeLabel != null)
                timeLabel.text = DisplayTimeLabel(item);

            var setCurrentBtn = container.Q<Button>("ItemSetCurrentBtn");
            if (setCurrentBtn != null)
            {
                setCurrentBtn.clicked += () => SetCurrentWorkFromTodo(item);
            }

            // 描述输入框（isDelayed = true：失焦后触发，避免每帧写盘）
            var descField = container.Q<TextField>("ItemDescField");
            if (descField != null)
            {
                descField.isDelayed = true;
                descField.SetValueWithoutNotify(item.Description ?? string.Empty);
                descField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == item.Description) return;
                    Undo.RecordObject(item, "Edit Todo Description");
                    item.Description = evt.newValue;
                    item.UpdateUpdateTime();
                    EditorUtility.SetDirty(item);
                    if (timeLabel != null) timeLabel.text = DisplayTimeLabel(item);
                    // 描述变更影响排序，无论有无搜索词都需要重建列表
                    RebuildList();
                });
            }

            // 删除按钮
            var delBtn = container.Q<Button>("ItemDeleteBtn");
            if (delBtn != null)
                delBtn.clicked += () => DeleteTodoItemWithUndo(item);
        }

        private VisualElement BuildItemDetails(TodoItem item)
        {
            if (item.Type == TodoType.Text)
                return null;

            var details = new VisualElement();
            ApplyInsetChrome(details);
            details.style.flexDirection = FlexDirection.Row;
            details.style.flexWrap = Wrap.Wrap;
            details.style.alignItems = Align.Center;
            details.style.marginTop = 6f;

            switch (item.Type)
            {
                case TodoType.Asset:       BuildAssetDetails(details, item);       break;
                case TodoType.SceneObject: BuildSceneObjectDetails(details, item); break;
                case TodoType.PrefabObject:BuildPrefabDetails(details, item);      break;
            }

            return details;
        }

        private static void BuildAssetDetails(VisualElement container, TodoItem item)
        {
            var asset = item.GetAsset();
            if (asset == null)
            {
                var lbl = MakeMiniLabel("资源: [已删除]");
                lbl.style.flexGrow = 1f;
                container.Add(lbl);
                return;
            }

            Texture previewTex = null;
            float previewW = 0f;
            string info;

            if (asset is Texture2D tex)
            {
                previewTex = tex;
                previewW = Mathf.Clamp(PreviewH * (tex.height > 0 ? (float)tex.width / tex.height : 1f), PreviewH, 220f);
                info = $"资源: {asset.name} ({tex.width}x{tex.height})";
            }
            else if (asset is Sprite spr)
            {
                previewTex = spr.texture;
                var r = spr.textureRect;
                previewW = Mathf.Clamp(PreviewH * (r.height > 0f ? r.width / r.height : 1f), PreviewH, 220f);
                info = $"资源: {asset.name} ({(int)r.width}x{(int)r.height})";
            }
            else
            {
                info = $"资源: {asset.name}";
            }

            var infoLbl = MakeMiniLabel(info);
            infoLbl.style.flexGrow = 1f;
            infoLbl.style.whiteSpace = WhiteSpace.Normal;
            container.Add(infoLbl);

            if (previewTex != null)
            {
                var img = new Image { image = previewTex, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = previewW;
                img.style.height = PreviewH;
                img.style.marginLeft = 10f;
                img.style.flexShrink = 0f;
                container.Add(img);
            }

            var btn = MakeButton("选中", 44f, () =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            });
            btn.style.marginLeft = 10f;
            container.Add(btn);
        }

        private static void BuildSceneObjectDetails(VisualElement container, TodoItem item)
        {
            var scenePath = item.TryGetResolvedScenePath(out var resolved) ? resolved : item.ScenePath;
            var sceneName = string.IsNullOrEmpty(scenePath)
                ? "[已删除]"
                : Path.GetFileNameWithoutExtension(scenePath);

            var lbl = MakeMiniLabel($"场景: {sceneName} | {item.GameObjectPath}");
            lbl.style.flexGrow = 1f;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            container.Add(lbl);

            var btn = MakeButton("选中", 44f, item.Locate);
            btn.style.marginLeft = 10f;
            container.Add(btn);
        }

        private static void BuildPrefabDetails(VisualElement container, TodoItem item)
        {
            var prefab = item.GetAsset();
            var lbl = MakeMiniLabel($"预制体: {(prefab != null ? prefab.name : "[已删除]")} | {item.PrefabChildPath}");
            lbl.style.flexGrow = 1f;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            container.Add(lbl);

            if (prefab != null)
            {
                var btn = MakeButton("选中", 44f, item.Locate);
                btn.style.marginLeft = 10f;
                container.Add(btn);
            }
        }

        // ==========================================================
        // 评论区
        // ==========================================================

        private VisualElement BuildCommentsSection(TodoItem item)
        {
            item.Comments ??= new List<TodoComment>();
            var section = new VisualElement();
            section.style.flexDirection = FlexDirection.Column;
            section.style.marginTop = 6f;

            var count = item.Comments.Count;

            if (count == 0)
            {
                section.Add(BuildAddCommentRow(item));
                return section;
            }

            if (count <= 3)
            {
                for (int i = count - 1; i >= 0; i--)
                    section.Add(BuildCommentElement(item, item.Comments[i], i));
                section.Add(BuildAddCommentRow(item));
                return section;
            }

            // 超过3条：显示最新一条 + 折叠按钮 + 输入框
            section.Add(BuildCommentElement(item, item.Comments[count - 1], count - 1));

            var expandLabel = item.ExpandComments
                ? $"收起 ({count - 1} 条旧评论)"
                : $"展开 ({count - 1} 条旧评论)";
            var expandBtn = MakeButton(expandLabel, () =>
            {
                item.ExpandComments = !item.ExpandComments;
                RebuildList();
            });
            expandBtn.style.marginTop = 2f;
            expandBtn.style.alignSelf = Align.FlexStart;
            section.Add(expandBtn);

            if (item.ExpandComments)
            {
                for (int i = count - 2; i >= 0; i--)
                    section.Add(BuildCommentElement(item, item.Comments[i], i));
            }

            section.Add(BuildAddCommentRow(item));
            return section;
        }

        private VisualElement BuildAddCommentRow(TodoItem item)
        {
            var row = MakeRow();
            row.style.marginTop = 2f;
            row.Add(MakeMiniLabel("添加评论:", 60f));

            var input = new TextField { value = item.EditingComment ?? string.Empty };
            input.style.flexGrow = 1f;
            input.style.height = 18f;
            input.style.fontSize = 10f;
            input.RegisterValueChangedCallback(evt => { item.EditingComment = evt.newValue; });
            row.Add(input);

            var send = MakeButton("发送", 50f, () =>
            {
                if (string.IsNullOrWhiteSpace(item.EditingComment)) return;
                Undo.RecordObject(item, "Add Todo Comment");
                item.AddComment(item.EditingComment);
                item.EditingComment = string.Empty;
                EditorUtility.SetDirty(item);
                RebuildList();
            }, 20f);
            send.style.marginLeft = 6f;
            row.Add(send);
            return row;
        }

        private VisualElement BuildCommentElement(TodoItem item, TodoComment comment, int index)
        {
            var box = new VisualElement();
            ApplyInsetChrome(box, 6f, 2f);
            box.style.marginTop = 2f;

            var header = MakeRow();
            var headerLbl = MakeMiniLabel($"{comment.Author} - {comment.Time}");
            headerLbl.style.flexGrow = 1f;
            header.Add(headerLbl);
            header.Add(MakeSpacer());
            header.Add(MakeButton("×", 20f, () =>
            {
                Undo.RecordObject(item, "Delete Todo Comment");
                item.RemoveCommentAt(index);
                EditorUtility.SetDirty(item);
                RebuildList();
            }, 18f));
            box.Add(header);

            var content = new Label(comment.Content ?? string.Empty);
            content.style.fontSize = 10f;
            content.style.whiteSpace = WhiteSpace.Normal;
            content.style.marginTop = 2f;
            box.Add(content);

            return box;
        }

        // ==========================================================
        // 数据操作
        // ==========================================================

        private void EnsureWorkNoteLoaded()
        {
            if (_workNoteLoaded) return;
            ApplyWorkNoteState(LoadWorkNoteState(), syncField: false);
            _workNoteLoaded = true;
        }

        private void SaveWorkNote(string note)
        {
            ApplyWorkNoteState(SaveWorkNoteState(note), syncField: false);
        }

        private void AddCurrentWorkNoteToTodo()
        {
            var note = _currentWorkNote?.Trim();
            if (string.IsNullOrWhiteSpace(note))
            {
                return;
            }

            if (AddQuickTextTodoItem(note))
            {
                Debug.Log($"[TodoList] 已从当前事项添加到 TODO: {note}");
            }
        }

        private void SetCurrentWorkFromTodo(TodoItem item)
        {
            var description = item?.Description?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            SaveWorkNote(description);
        }

        private void SyncWorkNoteTimeLabel()
        {
            if (_workTimeLabel == null) return;
            if (string.IsNullOrEmpty(_currentWorkNoteTime))
            {
                _workTimeLabel.style.display = DisplayStyle.None;
            }
            else
            {
                _workTimeLabel.text = $"更新: {_currentWorkNoteTime}";
                _workTimeLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void ApplyWorkNoteState(WorkNoteState state, bool syncField)
        {
            _currentWorkNote = state.Note;
            _currentWorkNoteTime = state.UpdateTime;
            if (syncField)
            {
                _workNoteField?.SetValueWithoutNotify(_currentWorkNote);
            }

            SyncWorkNoteTimeLabel();
        }

        private void SyncStatsLabel()
        {
            if (_statsLabel == null) return;
            var all = _config?.TodoItems;
            int total = all?.Count ?? 0;
            int pending = all?.Count(x => x != null && x.Status == TodoStatus.Pending) ?? 0;
            int verify = all?.Count(x => x != null && x.Status == TodoStatus.Verify) ?? 0;
            int buildCheck = all?.Count(x => x != null && x.Status == TodoStatus.BuildCheck) ?? 0;
            _statsLabel.text = $"总计: {total} | 待修复: {pending} | 待验证: {verify} | 打包检查: {buildCheck}";
        }

        private void LoadConfig()
        {
            TodoProjectPaths.EnsureDataFolders();
            _config = LoadOrCreateConfig();
        }

        /// <summary>
        /// 添加新的文本类型待办事项（工具栏"添加"按钮触发）
        /// </summary>
        private void AddNewTodoItem()
        {
            if (_config == null) LoadConfig();
            var input = TodoInputDialog.ShowWithOptions("添加纯文本待办", "请输入描述:", "新待办事项");
            if (!input.Confirmed || string.IsNullOrWhiteSpace(input.Text)) return;
            CreateTextTodoItem(input.Text.Trim());
        }

        private void CreateTextTodoItem(string description)
        {
            CreateAndSaveTodoItem(_config, CreateTodoItem(description, TodoType.Text), description);
            RebuildList();
        }

        /// <summary>
        /// 刷新待办列表：重新扫描项目中所有 TodoItem 资源
        /// </summary>
        private void RefreshTodoItems()
        {
            if (_config == null) LoadConfig();
            _config.TodoItems ??= new List<TodoItem>();

            TodoProjectPaths.EnsureDataFolders();
            var scannedItems = new List<TodoItem>();
            var guids = AssetDatabase.FindAssets($"t:{nameof(TodoItem)}", new[] { TodoProjectPaths.ItemsFolder });
            foreach (var guid in guids.OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                var item = AssetDatabase.LoadAssetAtPath<TodoItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null)
                    scannedItems.Add(item);
            }

            if (!_config.TodoItems.SequenceEqual(scannedItems))
            {
                _config.TodoItems = scannedItems;
                EditorUtility.SetDirty(_config);
                SaveAssetIfDirty(_config);
            }

            SessionState.SetBool(InitialScanSessionKey, true);
            Debug.Log($"[TodoList] 刷新完成，共找到 {_config.TodoItems.Count} 个待办事项");

            if (_uiBuilt) RebuildList();
        }

        private List<TodoItem> GetFilteredItems()
        {
            if (_config?.TodoItems == null) return new List<TodoItem>();

            return _config.TodoItems
                .Where(x => x != null)
                .Where(x =>
                {
                    if (_showOnlyPending && x.Status != _filterStatus) return false;
                    if (!string.IsNullOrWhiteSpace(_searchText))
                    {
                        if (string.IsNullOrEmpty(x.Description)) return false;
                        if (x.Description.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0) return false;
                    }
                    return true;
                })
                .OrderByDescending(x => x.IsPinned)
                .ThenBy(x => StatusSortOrder(x.Status))
                .ThenByDescending(SortTime)
                .ToList();
        }

        private void DeleteTodoItem(TodoItem item)
        {
            _config?.TodoItems?.Remove(item);
            if (_config != null) EditorUtility.SetDirty(_config);

            var path = AssetDatabase.GetAssetPath(item);
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.DeleteAsset(path);

            SaveAssetIfDirty(_config);
            if (_uiBuilt) RebuildList();
        }

        /// <summary>
        /// 支持撤销的删除操作
        /// </summary>
        private void DeleteTodoItemWithUndo(TodoItem item)
        {
            if (item == null)
            {
                return;
            }

            var deleteSceneAssets = false;
            if (!ConfirmTodoDeletion(item, out deleteSceneAssets))
            {
                return;
            }

            ClearDeletionBackups();

            var path = AssetDatabase.GetAssetPath(item);
            BackupAssetForUndo(path);
            _tempAssetPath = path;
            _tempBackupPath = _deletedAssetBackups.Count > 0 ? _deletedAssetBackups[0].BackupAssetPath : null;
            _deletedItemName = item.Description;
            _deletedItemGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.GUIDFromAssetPath(path).ToString();

            if (deleteSceneAssets && !TryDeleteLinkedSceneAssets(item))
            {
                ClearDeletionBackups();
                return;
            }

            if (_deletedAssetBackups.Count > 0)
            {
                Undo.undoRedoPerformed -= OnUndoRedoPerformed;
                Undo.undoRedoPerformed += OnUndoRedoPerformed;
            }

            DeleteTodoItem(item);
            Debug.Log($"[TodoList] 已删除待办: {item.Description}（可使用 Ctrl+Z 撤销）");
        }

        private void OnUndoRedoPerformed()
        {
            if (string.IsNullOrEmpty(_tempAssetPath) || string.IsNullOrEmpty(_tempBackupPath))
                return;

            if (!File.Exists(_tempAssetPath) && File.Exists(_tempBackupPath))
            {
                RestoreDeletedAssets();
                AssetDatabase.Refresh();

                var restored = AssetDatabase.LoadAssetAtPath<TodoItem>(_tempAssetPath);
                if (restored != null && _config != null)
                {
                    _config.TodoItems ??= new List<TodoItem>();
                    if (!_config.TodoItems.Contains(restored))
                    {
                        _config.TodoItems.Add(restored);
                        EditorUtility.SetDirty(_config);
                        AssetDatabase.SaveAssetIfDirty(_config);
                    }
                }

                Debug.Log($"[TodoList] 已恢复待办: {_deletedItemName}");
                ClearDeletionBackups();
                Undo.undoRedoPerformed -= OnUndoRedoPerformed;

                LoadConfig();
                if (_uiBuilt) RebuildList();
            }
        }

        // ---------- 恢复待办（保留接口，备用）----------

        private void RestoreTodoItem(TodoUndoData data)
        {
            TodoProjectPaths.EnsureDataFolders();

            var item = ScriptableObject.CreateInstance<TodoItem>();
            item.Description = data.Description;
            item.Status = data.Status;
            item.Category = data.Category;
            item.IsPinned = data.IsPinned;
            item.Type = data.Type;
            item.CreateTime = data.CreateTime;
            item.UpdateTime = data.UpdateTime;
            item.AssetGuid = data.AssetGuid;
            item.ScenePath = data.ScenePath;
            item.SceneGuid = data.SceneGuid;
            item.IsClonedSceneReference = data.IsClonedSceneReference;
            item.GameObjectPath = data.GameObjectPath;
            item.PrefabChildPath = data.PrefabChildPath;

            if (data.Comments?.Count > 0)
            {
                item.Comments = new List<TodoComment>();
                foreach (var c in data.Comments)
                    item.Comments.Add(new TodoComment { Content = c.Content, Time = c.Time, Author = c.Author });
            }

            var baseName = Path.GetFileNameWithoutExtension(data.OriginalAssetPath);
            var newPath = $"{TodoProjectPaths.ItemsFolder}/{baseName}.asset";
            if (File.Exists(newPath))
                newPath = $"{TodoProjectPaths.ItemsFolder}/{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.asset";

            AssetDatabase.CreateAsset(item, newPath);
            _config.TodoItems ??= new List<TodoItem>();
            _config.TodoItems.Add(item);
            EditorUtility.SetDirty(_config);
            SaveAssetIfDirty(_config);

            Debug.Log($"[TodoList] 已恢复待办: {item.Description}");
        }

        // ==========================================================
        // 外部静态入口（供右键菜单/拖拽入口调用）
        // ==========================================================

        /// <summary>
        /// 添加待办事项（由右键菜单 / Hierarchy 拖拽入口调用）
        /// </summary>
        public static void AddTodoItem(
            string description, TodoType type,
            string assetGuid = null, string scenePath = null,
            string gameObjectPath = null, string prefabChildPath = null,
            string sceneGuid = null, bool isClonedSceneReference = false)
        {
            var window = GetWindow<TodoListWindow>("TodoList");
            window.Show();
            window.Focus();

            if (window._config == null) window.LoadConfig();

            var item = CreateTodoItem(description, type);

            var resolvedGuid = sceneGuid;
            if (string.IsNullOrEmpty(resolvedGuid) && !string.IsNullOrEmpty(scenePath))
                resolvedGuid = AssetDatabase.AssetPathToGUID(scenePath);

            item.AssetGuid = assetGuid ?? string.Empty;
            item.ScenePath = scenePath ?? string.Empty;
            item.SceneGuid = resolvedGuid ?? string.Empty;
            item.IsClonedSceneReference = isClonedSceneReference;
            item.GameObjectPath = gameObjectPath ?? string.Empty;
            item.PrefabChildPath = prefabChildPath ?? string.Empty;

            CreateAndSaveTodoItem(window._config, item, description);

            if (window._uiBuilt) window.RebuildList();
        }

        public static bool AddQuickTextTodoItem(string description)
        {
            var trimmedDescription = description?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedDescription))
            {
                return false;
            }

            var config = LoadOrCreateConfig();
            CreateAndSaveTodoItem(config, CreateTodoItem(trimmedDescription, TodoType.Text),
                trimmedDescription);
            RefreshOpenWindows(config);
            return true;
        }

        private static bool ShouldRunInitialScan()
        {
            return !SessionState.GetBool(InitialScanSessionKey, false);
        }

        private static void SaveAssetIfDirty(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            AssetDatabase.SaveAssetIfDirty(target);
        }

        private static TodoListConfig LoadOrCreateConfig()
        {
            TodoProjectPaths.EnsureDataFolders();

            var config = AssetDatabase.LoadAssetAtPath<TodoListConfig>(TodoProjectPaths.ConfigAssetPath);
            if (config != null)
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<TodoListConfig>();
            AssetDatabase.CreateAsset(config, TodoProjectPaths.ConfigAssetPath);
            SaveAssetIfDirty(config);
            return config;
        }

        private static TodoItem CreateTodoItem(string description, TodoType type)
        {
            var item = ScriptableObject.CreateInstance<TodoItem>();
            item.Description = description;
            item.Status = TodoStatus.Pending;
            item.Category = TodoCategory.Effect;
            item.IsPinned = false;
            item.Type = type;
            item.CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            item.UpdateTime = string.Empty;
            item.AssetGuid = string.Empty;
            item.ScenePath = string.Empty;
            item.SceneGuid = string.Empty;
            item.IsClonedSceneReference = false;
            item.GameObjectPath = string.Empty;
            item.PrefabChildPath = string.Empty;
            return item;
        }

        private bool ConfirmTodoDeletion(TodoItem item, out bool deleteSceneAssets)
        {
            deleteSceneAssets = false;
            if (item.Type != TodoType.SceneObject || !item.IsClonedSceneReference ||
                !item.TryGetResolvedScenePath(out var resolvedScenePath) || string.IsNullOrEmpty(resolvedScenePath))
            {
                return true;
            }

            var sceneName = Path.GetFileNameWithoutExtension(resolvedScenePath);
            var option = EditorUtility.DisplayDialogComplex(
                "删除待办",
                $"该待办关联了通过“另存为场景”生成的场景 [{sceneName}]。\n\n是否在删除待办的同时，也删除该场景及其关联资源？",
                "删除待办和场景",
                "仅删除待办",
                "取消");

            switch (option)
            {
                case 0:
                    deleteSceneAssets = true;
                    return true;
                case 1:
                    return true;
                default:
                    return false;
            }
        }

        private bool TryDeleteLinkedSceneAssets(TodoItem item)
        {
            if (!item.TryGetResolvedScenePath(out var resolvedScenePath) || string.IsNullOrEmpty(resolvedScenePath))
            {
                return true;
            }

            if (IsSceneLoaded(resolvedScenePath))
            {
                var sceneName = Path.GetFileNameWithoutExtension(resolvedScenePath);
                EditorUtility.DisplayDialog(
                    "无法删除场景",
                    $"场景 [{sceneName}] 当前已打开，请先切换或关闭该场景后再重试删除。",
                    "确定");
                return false;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(resolvedScenePath);
            if (sceneAsset == null)
            {
                return true;
            }

            var extension = TodoExtensionRegistry.SceneDeleteExtension;
            if (extension != null)
            {
                var linkedAssetPaths = extension.GetLinkedAssetPaths(sceneAsset);
                if (linkedAssetPaths != null)
                {
                    foreach (var linkedAssetPath in linkedAssetPaths)
                    {
                        BackupAssetForUndo(linkedAssetPath);
                    }
                }

                if (!extension.TryDeleteLinkedAssets(sceneAsset, out var errorMessage))
                {
                    EditorUtility.DisplayDialog("删除失败", errorMessage, "确定");
                    return false;
                }
            }

            BackupAssetForUndo(resolvedScenePath);

            if (!AssetDatabase.DeleteAsset(resolvedScenePath))
            {
                RestoreDeletedAssets();
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("删除失败", $"删除场景失败：\n{resolvedScenePath}", "确定");
                return false;
            }

            return true;
        }

        private static bool IsSceneLoaded(string scenePath)
        {
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var loadedScene = EditorSceneManager.GetSceneAt(i);
                if (loadedScene.IsValid() && loadedScene.isLoaded &&
                    string.Equals(loadedScene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void BackupAssetForUndo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            {
                return;
            }

            Directory.CreateDirectory("Temp");
            var uniqueId = Guid.NewGuid().ToString("N");
            var extension = Path.GetExtension(assetPath);
            var backupAssetPath = Path.Combine("Temp", $"{uniqueId}{extension}").Replace("\\", "/");
            File.Copy(assetPath, backupAssetPath, true);

            string backupMetaPath = null;
            var metaPath = $"{assetPath}.meta";
            if (File.Exists(metaPath))
            {
                backupMetaPath = $"{backupAssetPath}.meta";
                File.Copy(metaPath, backupMetaPath, true);
            }

            _deletedAssetBackups.Add(new DeletedAssetBackup(assetPath, backupAssetPath, backupMetaPath));
        }

        private static void RestoreDeletedAssets()
        {
            foreach (var backup in _deletedAssetBackups.OrderBy(x => x.AssetPath.Count(c => c == '/')))
            {
                var dir = Path.GetDirectoryName(backup.AssetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (File.Exists(backup.BackupAssetPath))
                {
                    File.Copy(backup.BackupAssetPath, backup.AssetPath, true);
                    File.Delete(backup.BackupAssetPath);
                }

                if (!string.IsNullOrEmpty(backup.BackupMetaPath) && File.Exists(backup.BackupMetaPath))
                {
                    File.Copy(backup.BackupMetaPath, $"{backup.AssetPath}.meta", true);
                    File.Delete(backup.BackupMetaPath);
                }
            }
        }

        private static void ClearDeletionBackups()
        {
            foreach (var backup in _deletedAssetBackups)
            {
                if (File.Exists(backup.BackupAssetPath))
                {
                    File.Delete(backup.BackupAssetPath);
                }

                if (!string.IsNullOrEmpty(backup.BackupMetaPath) && File.Exists(backup.BackupMetaPath))
                {
                    File.Delete(backup.BackupMetaPath);
                }
            }

            _deletedAssetBackups.Clear();
            _tempAssetPath = null;
            _tempBackupPath = null;
            _deletedItemName = null;
            _deletedItemGuid = null;
        }

        private static void CreateAndSaveTodoItem(TodoListConfig config, TodoItem item, string description)
        {
            var assetPath = CreateItemAsset(item, description);
            Debug.Log($"[TodoList] 已添加新的待办事项: {item.Description} -> {assetPath}");

            config.TodoItems ??= new List<TodoItem>();
            config.TodoItems.Add(item);
            EditorUtility.SetDirty(config);
            SaveAssetIfDirty(config);
        }

        private static void RefreshOpenWindows(TodoListConfig config)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TodoListWindow>())
            {
                if (window == null)
                {
                    continue;
                }

                window._config = config;
                if (window._uiBuilt)
                {
                    window.RebuildList();
                }
                else
                {
                    window.Repaint();
                }
            }
        }

        public static WorkNoteState LoadWorkNoteState()
        {
            return new WorkNoteState(
                EditorPrefs.GetString(WorkNoteKey, string.Empty),
                EditorPrefs.GetString(WorkNoteTimeKey, string.Empty));
        }

        public static WorkNoteState SaveWorkNoteState(string note)
        {
            note ??= string.Empty;

            var currentState = LoadWorkNoteState();
            if (currentState.Note == note)
            {
                return currentState;
            }

            var nextState = new WorkNoteState(
                note,
                string.IsNullOrWhiteSpace(note)
                    ? string.Empty
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            EditorPrefs.SetString(WorkNoteKey, nextState.Note);
            EditorPrefs.SetString(WorkNoteTimeKey, nextState.UpdateTime);
            SyncOpenWindowsWorkNote(nextState);
            return nextState;
        }

        private static void SyncOpenWindowsWorkNote(WorkNoteState state)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TodoListWindow>())
            {
                if (window == null)
                {
                    continue;
                }

                window._workNoteLoaded = true;
                window.ApplyWorkNoteState(state, syncField: true);
            }
        }

        // ==========================================================
        // 辅助：文件生成
        // ==========================================================

        private static string CreateItemAsset(TodoItem item, string description)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var safe = string.Join("_", description.Split(Path.GetInvalidFileNameChars()));
            if (safe.Length > 20) safe = safe.Substring(0, 20);
            if (string.IsNullOrWhiteSpace(safe)) safe = "New";

            TodoProjectPaths.EnsureDataFolders();

            var path = AssetDatabase.GenerateUniqueAssetPath($"{TodoProjectPaths.ItemsFolder}/Todo_{timestamp}_{safe}.asset");
            AssetDatabase.CreateAsset(item, path);
            return path;
        }

        // ==========================================================
        // 辅助：排序 / 标签转换
        // ==========================================================

        private static string SortTime(TodoItem x) =>
            !string.IsNullOrEmpty(x.UpdateTime) ? x.UpdateTime : x.CreateTime;

        private static int StatusSortOrder(TodoStatus s) => s switch
        {
            TodoStatus.Verify    => 0,
            TodoStatus.Pending   => 1,
            TodoStatus.Fixed     => 2,
            TodoStatus.BuildCheck=> 3,
            TodoStatus.Suspended => 4,
            _ => int.MaxValue
        };

        private static string StatusLabel(TodoStatus s) => s switch
        {
            TodoStatus.Pending    => "待修复",
            TodoStatus.Verify     => "待验证",
            TodoStatus.Fixed      => "已修复",
            TodoStatus.Suspended  => "搁置",
            TodoStatus.BuildCheck => "打包检查",
            _ => s.ToString()
        };

        private static TodoStatus ParseStatus(string label) => label switch
        {
            "待验证"   => TodoStatus.Verify,
            "已修复"   => TodoStatus.Fixed,
            "搁置"     => TodoStatus.Suspended,
            "打包检查" => TodoStatus.BuildCheck,
            _ => TodoStatus.Pending
        };

        private static string DisplayTimeLabel(TodoItem item)
        {
            var t = !string.IsNullOrEmpty(item.UpdateTime) ? item.UpdateTime : item.CreateTime;
            return !string.IsNullOrEmpty(item.UpdateTime) ? $"更新: {t}" : t;
        }

        private static Color StatusColor(TodoStatus s)
        {
            // 深色主题（Unity Pro Skin）：在深灰背景 ~(0.22, 0.22, 0.22) 上叠加低饱和色调
            // 浅色主题（Personal Skin）：在浅灰背景上使用高亮淡色
            return EditorGUIUtility.isProSkin
                ? s switch
                {
                    TodoStatus.Pending    => new Color(0.26f, 0.26f, 0.26f), // 暗橙 — 待修复
                    TodoStatus.Verify     => new Color(0.22f, 0.36f, 0.24f), // 暗绿 — 待验证
                    TodoStatus.Fixed      => new Color(0.22f, 0.30f, 0.24f), // 极暗绿 — 已修复
                    TodoStatus.Suspended  => new Color(0.24f, 0.24f, 0.24f), // 深灰 — 搁置
                    TodoStatus.BuildCheck => new Color(0.44f, 0.19f, 0.19f), // 暗红 — 打包检查
                    _                    => new Color(0.24f, 0.24f, 0.24f),
                }
                : s switch
                {
                    TodoStatus.Pending    => new Color(1.00f, 0.90f, 0.76f), // 浅橙 — 待修复
                    TodoStatus.Verify     => new Color(0.84f, 1.00f, 0.80f), // 浅绿 — 待验证
                    TodoStatus.Fixed      => new Color(0.88f, 0.96f, 0.88f), // 极浅绿 — 已修复
                    TodoStatus.Suspended  => new Color(0.86f, 0.86f, 0.86f), // 浅灰 — 搁置
                    TodoStatus.BuildCheck => new Color(1.00f, 0.74f, 0.74f), // 浅红 — 打包检查
                    _                    => Color.white,
                };
        }

        // ==========================================================
        // 辅助：UI 工厂方法
        // ==========================================================

        private static VisualElement MakeRow()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        private static VisualElement MakeSpacer()
        {
            var e = new VisualElement();
            e.style.flexGrow = 1f;
            return e;
        }

        private static Label MakeBoldLabel(string text, int fontSize)
        {
            var l = new Label(text);
            l.style.fontSize = fontSize;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static Label MakeMiniLabel(string text, float fixedWidth = -1f)
        {
            var l = new Label(text);
            l.style.fontSize = 10f;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (fixedWidth > 0f)
            {
                l.style.width = fixedWidth;
                l.style.minWidth = fixedWidth;
                l.style.flexShrink = 0f;
            }
            return l;
        }

        private static DropdownField MakePopup(List<string> choices, string current, float width)
        {
            var idx = Mathf.Max(0, choices.IndexOf(current));
            var f = new DropdownField(choices, idx);
            f.style.width = width;
            f.style.minWidth = width;
            f.style.height = MiniH;
            f.style.flexShrink = 0f;
            return f;
        }

        private static Button MakeButton(string text, Action onClick, float height = MiniH)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = height;
            b.style.minHeight = height;
            b.style.flexShrink = 0f;
            return b;
        }

        private static Button MakeButton(string text, float width, Action onClick, float height = MiniH)
        {
            var b = MakeButton(text, onClick, height);
            b.style.width = width;
            b.style.minWidth = width;
            return b;
        }

        private static VisualElement BuildTypeIcon(TodoType type)
        {
            var iconName = type switch
            {
                TodoType.Asset        => "Prefab Icon",
                TodoType.SceneObject  => "SceneAsset Icon",
                TodoType.Text         => "d_TextAsset Icon",
                TodoType.PrefabObject => "Prefab Icon",
                _ => "d_Invalid"
            };
            var typeLabel = type switch
            {
                TodoType.Asset        => "资源",
                TodoType.SceneObject  => "场景物体",
                TodoType.Text         => "纯文本",
                TodoType.PrefabObject => "预制体子物体",
                _ => type.ToString()
            };
            var tex = EditorGUIUtility.IconContent(iconName).image as Texture2D;
            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, tooltip = typeLabel };
                img.style.width = 18f;
                img.style.height = 18f;
                img.style.flexShrink = 0f;
                return img;
            }
            var fallback = MakeMiniLabel(typeLabel, 72f);
            return fallback;
        }

        private static void ApplyBorder(VisualElement e, Color color, float width = 1f)
        {
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
        }

        private static void ApplyPanelChrome(VisualElement e)
        {
            var bg = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(0f, 0f, 0f, 0.035f);
            var border = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.14f);
            e.style.backgroundColor = bg;
            ApplyBorder(e, border);
            e.style.paddingLeft = 8f;
            e.style.paddingRight = 8f;
            e.style.paddingTop = 6f;
            e.style.paddingBottom = 6f;
        }

        private static void ApplyInsetChrome(VisualElement e, float hPad = 6f, float vPad = 4f)
        {
            var bg = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.05f)
                : new Color(0f, 0f, 0f, 0.05f);
            var border = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.14f);
            e.style.backgroundColor = bg;
            ApplyBorder(e, border);
            e.style.paddingLeft = hPad;
            e.style.paddingRight = hPad;
            e.style.paddingTop = vPad;
            e.style.paddingBottom = vPad;
        }

        // ==========================================================
        // 私有数据类（用于撤销恢复，备用）
        // ==========================================================

        [Serializable]
        private class TodoUndoData
        {
            public string Description;
            public TodoStatus Status;
            public TodoCategory Category;
            public bool IsPinned;
            public TodoType Type;
            public string CreateTime;
            public string UpdateTime;
            public string AssetGuid;
            public string ScenePath;
            public string SceneGuid;
            public bool IsClonedSceneReference;
            public string GameObjectPath;
            public string PrefabChildPath;
            public string OriginalAssetPath;
            public string OriginalGuid;
            public List<TodoCommentData> Comments;
        }

        private readonly struct DeletedAssetBackup
        {
            public DeletedAssetBackup(string assetPath, string backupAssetPath, string backupMetaPath)
            {
                AssetPath = assetPath;
                BackupAssetPath = backupAssetPath;
                BackupMetaPath = backupMetaPath;
            }

            public string AssetPath { get; }
            public string BackupAssetPath { get; }
            public string BackupMetaPath { get; }
        }

        [Serializable]
        private class TodoCommentData
        {
            public string Content;
            public string Time;
            public string Author;
        }
    }
}
