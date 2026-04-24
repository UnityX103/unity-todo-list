namespace Editor.TodoList
{
    public static class TodoEditorApi
    {
        public static void OpenWindow()
        {
            TodoListWindow.OpenWindow();
        }

        public static TodoInputDialog.TodoInputResult ShowInputDialog(
            string title,
            string prompt,
            string defaultText = "",
            bool showSaveAsScene = false,
            bool defaultSaveAsScene = false)
        {
            return TodoInputDialog.ShowWithOptions(title, prompt, defaultText, showSaveAsScene, defaultSaveAsScene);
        }

        public static void AddTodoItem(
            string description,
            TodoType type = TodoType.Text,
            string assetGuid = null,
            string scenePath = null,
            string gameObjectPath = null,
            string prefabChildPath = null,
            bool isClonedSceneReference = false,
            string sceneGuid = null)
        {
            TodoListWindow.AddTodoItem(
                description: description,
                type: type,
                assetGuid: assetGuid,
                scenePath: scenePath,
                gameObjectPath: gameObjectPath,
                prefabChildPath: prefabChildPath,
                isClonedSceneReference: isClonedSceneReference,
                sceneGuid: sceneGuid);
        }

        public static TodoListWindow.WorkNoteState GetWorkNoteState()
        {
            return TodoListWindow.LoadWorkNoteState();
        }

        public static TodoListWindow.WorkNoteState SaveWorkNoteState(string note)
        {
            return TodoListWindow.SaveWorkNoteState(note);
        }
    }
}
