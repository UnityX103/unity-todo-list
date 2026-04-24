# Unity Todo List

一个 Unity 编辑器的待办事项管理窗口，以独立 package 形式分发。每条 TODO 以独立 `ScriptableObject` 存储，避免多人协作时的文件冲突。

- **Package 名**：`com.mida.editor.todo`
- **版本**：1.0.0
- **仓库**：`https://github.com/UnityX103/unity-todo-list`
- **Unity 版本**：2021.3.15f1（其他 2021+ LTS 版本理论上也能用，未实测）

---

## 功能概览

- 三种 TODO 类型：**资源引用**（保存 GUID）、**场景物体**（保存场景路径 + 层级路径）、**纯文本**
- 三种状态：**待修复 / 待验证 / 已修复**，可筛选 + 搜索 + 统计
- 每条 TODO 支持多条评论（作者、时间、删除）
- 右键菜单快捷添加：Project 资源、Hierarchy 物体均可一键入列
- "定位"按钮自动跳转到对应资源或打开对应场景并选中物体
- 独立存储：每条 TODO 是 `Assets/Editor/TodoList/Items/` 下的一个 `.asset` 文件
- 对外暴露 `TodoEditorApi` 静态类，方便其他工具集成

---

## 依赖

- Unity 2021.3+
- [Odin Inspector](https://odininspector.com/)（编辑器 UI 依赖 `Sirenix.OdinInspector.Editor`）

> 如果你的项目没有 Odin Inspector，会编译失败。本包未内置 Odin，请自行购买并安装到宿主项目。

---

## 安装

### 方式 1：Package Manager UI（推荐）

1. 在 Unity 中打开 `Window` → `Package Manager`
2. 左上角 `+` → `Add package from git URL...`
3. 粘贴下面任一地址：
   - 公开访问（HTTPS）：`https://github.com/UnityX103/unity-todo-list.git`
   - SSH：`git@github.com:UnityX103/unity-todo-list.git`
4. 点 `Add`，等待 Unity 导入完成

### 方式 2：手动编辑 manifest.json

编辑项目根目录下的 `Packages/manifest.json`，在 `dependencies` 中添加：

```json
{
  "dependencies": {
    "com.mida.editor.todo": "https://github.com/UnityX103/unity-todo-list.git"
  }
}
```

锁定分支 / tag / commit：

```json
"com.mida.editor.todo": "https://github.com/UnityX103/unity-todo-list.git#main"
"com.mida.editor.todo": "https://github.com/UnityX103/unity-todo-list.git#v1.0.0"
"com.mida.editor.todo": "https://github.com/UnityX103/unity-todo-list.git#9c527fd"
```

### 方式 3：本地文件引用（需要频繁改 package 源码时）

把仓库 clone 到本地后，在 `Packages/manifest.json` 中引用本地路径：

```json
"com.mida.editor.todo": "file:../LocalPackage/com.mida.editor.todo"
```

路径相对于 `Packages/` 目录。

---

## 快速上手

### 打开窗口

菜单栏：`GameConsole` → `TodoList`

### 添加 TODO

- **资源引用**：Project 窗口右键选中资源 → `Add to TodoList` → 选择状态（待修复 / 待验证）
- **场景物体**：Hierarchy 右键选中物体 → `Add to TodoList` → 选择状态
- **纯文本**：菜单 `GameConsole` → `TodoList` → `添加纯文本待办`

### 管理 TODO

- **切换状态**：列表中直接点状态下拉框
- **编辑描述**：点描述文本即可原地编辑，离焦自动保存
- **定位**：点"定位"按钮跳转到资源或场景物体（场景物体会自动打开所在场景）
- **评论**：展开评论区可添加、删除评论
- **删除**：点"删除"按钮会连同 `.asset` 文件一起移除

### 筛选与搜索

窗口顶部工具栏：
- 状态筛选下拉 + "仅显示当前状态"复选框
- 关键词搜索框（匹配描述文本）
- 实时统计各状态数量

---

## 数据存储

导入 package 后首次使用，会在宿主项目中自动创建以下目录：

```
Assets/Editor/TodoList/
├── TodoListConfig.asset     # 引用所有 TODO 的配置文件
└── Items/
    ├── Todo_20260101_120000_xxx.asset
    └── ...
```

这些资产文件应随项目一同提交到 Git（它们就是 TODO 数据本身）。**不要手动编辑 `.asset` 文件**，通过窗口操作即可。

> 路径硬编码在 `TodoProjectPaths.cs` 中，目前不可配置。如果需要自定义，fork 后修改即可。

---

## 程序化 API

其他编辑器工具可以通过 `TodoEditorApi` 静态类集成：

```csharp
using Editor.TodoList;

// 打开 TodoList 窗口
TodoEditorApi.OpenWindow();

// 弹出输入对话框
var result = TodoEditorApi.ShowInputDialog(
    title: "添加 TODO",
    prompt: "描述这个问题",
    defaultText: "");

// 添加一条 TODO
TodoEditorApi.AddTodoItem(
    description: "修复 XXX 的报错",
    type: TodoType.Text);
```

---

## 目录结构

```
com.mida.editor.todo/
├── package.json
├── README.md
├── README.legacy.md              # 旧版文档，保留作参考
└── Editor/
    ├── TodoList.asmdef           # 程序集定义（仅 Editor 平台）
    ├── Core/                     # 数据模型与核心逻辑
    │   ├── TodoItem.cs
    │   ├── TodoListConfig.cs
    │   ├── TodoEditorApi.cs
    │   ├── TodoProjectPaths.cs
    │   ├── TodoPackagePaths.cs
    │   ├── TodoSceneReferenceUtility.cs
    │   └── Extensions/           # 扩展点接口
    ├── Window/                   # 编辑器窗口 UI
    │   ├── TodoListWindow.cs
    │   └── TodoInputDialog.cs
    ├── Integration/              # 与 Unity 编辑器集成
    │   ├── TodoListMenuItems.cs
    │   ├── TodoListHierarchy.cs
    │   └── TodoListBuildProcessor.cs
    └── Resources/                # UXML / USS 资源
        ├── UXML/
        └── USS/
```

---

## 更新与卸载

**更新**：通过 Package Manager 引入的，可在 `Package Manager` 窗口选中该包后点 `Update`；或删除 `Packages/packages-lock.json` 中对应条目后 Unity 会重新拉取。

**卸载**：在 `Package Manager` 中移除，或从 `manifest.json` 中删除对应依赖。

> 卸载 package 不会删除 `Assets/Editor/TodoList/` 下的数据资产文件 —— 这些属于宿主项目。如果不再需要，可以手动删除整个目录。

---

## 许可证

内部工具，暂未指定开源协议。
