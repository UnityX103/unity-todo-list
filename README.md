# MIDA Editor Todo

Todo 编辑器窗口独立包。代码、UXML、USS 位于本包；可写数据固定在宿主项目 `Assets/Editor/TodoList/`。

## 要求

- Unity 2021.3.15f1
- Sirenix Odin Inspector Editor 程序集可用

## 集成

在 `Packages/manifest.json` 中加入：

`"com.mida.editor.todo": "file:../LocalPackage/com.mida.editor.todo"`

## 数据位置

- `Assets/Editor/TodoList/TodoListConfig.asset`
- `Assets/Editor/TodoList/Items/*.asset`
