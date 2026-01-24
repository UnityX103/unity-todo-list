using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Editor.TodoList
{
    /// <summary>
    /// 构建处理器 - 在打包前检查是否有"打包检查"状态的TODO
    /// </summary>
    public class TodoListBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(TodoListConfig)}");
            if (guids.Length == 0)
                return;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = AssetDatabase.LoadAssetAtPath<TodoListConfig>(path);

            if (config == null || config.TodoItems == null)
                return;

            // 查找所有"打包检查"状态的TODO
            var buildCheckItems = config.TodoItems.Where(x => x != null && x.Status == TodoStatus.BuildCheck).ToList();

            if (buildCheckItems.Count > 0)
            {
                var errorMsg = "=== 打包检查失败 ===\n\n";
                errorMsg += $"发现 {buildCheckItems.Count} 个待处理的打包检查项:\n\n";

                for (int i = 0; i < buildCheckItems.Count; i++)
                {
                    var item = buildCheckItems[i];
                    errorMsg += $"{i + 1}. [{item.Description}]\n";

                    if (item.Type == TodoType.Asset && !string.IsNullOrEmpty(item.AssetGuid))
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(item.AssetGuid);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                            if (asset != null)
                                errorMsg += $"   资源: {asset.name} ({assetPath})\n";
                        }
                    }
                    else if (item.Type == TodoType.SceneObject && !string.IsNullOrEmpty(item.ScenePath))
                    {
                        errorMsg += $"   场景: {item.ScenePath}\n";
                        errorMsg += $"   物体: {item.GameObjectPath}\n";
                    }

                    if (item.Comments != null && item.Comments.Count > 0)
                    {
                        var latestComment = item.Comments[item.Comments.Count - 1];
                        errorMsg += $"   最新评论: {latestComment.Content}\n";
                    }

                    errorMsg += "\n";
                }

                errorMsg += "请处理完所有打包检查项后再进行打包。";

                // 抛出异常阻止打包
                throw new BuildFailedException(errorMsg);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            // 构建完成后的处理（如果需要）
        }
    }
}
