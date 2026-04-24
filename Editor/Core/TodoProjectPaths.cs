using System.IO;
using UnityEditor;

namespace Editor.TodoList
{
    internal static class TodoProjectPaths
    {
        public const string RootFolder = "Assets/Editor/TodoList";
        public const string ConfigAssetPath = RootFolder + "/TodoListConfig.asset";
        public const string ItemsFolder = RootFolder + "/Items";

        public static void EnsureDataFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
            {
                Directory.CreateDirectory(RootFolder);
            }

            if (!AssetDatabase.IsValidFolder(ItemsFolder))
            {
                Directory.CreateDirectory(ItemsFolder);
            }
        }
    }
}
