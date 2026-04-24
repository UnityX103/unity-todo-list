using System.Collections.Generic;
using UnityEditor;

namespace Editor.TodoList
{
    public interface ITodoSceneDeleteExtension
    {
        IReadOnlyList<string> GetLinkedAssetPaths(SceneAsset sceneAsset);
        bool TryDeleteLinkedAssets(SceneAsset sceneAsset, out string errorMessage);
    }
}
