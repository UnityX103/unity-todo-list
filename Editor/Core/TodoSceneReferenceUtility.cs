using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Editor.TodoList
{
    public readonly struct TodoSceneClonedEventArgs
    {
        public TodoSceneClonedEventArgs(string sourceScenePath, string sourceSceneGuid, string targetScenePath,
            string targetSceneGuid)
        {
            SourceScenePath = sourceScenePath ?? string.Empty;
            SourceSceneGuid = sourceSceneGuid ?? string.Empty;
            TargetScenePath = targetScenePath ?? string.Empty;
            TargetSceneGuid = targetSceneGuid ?? string.Empty;
        }

        public string SourceScenePath { get; }
        public string SourceSceneGuid { get; }
        public string TargetScenePath { get; }
        public string TargetSceneGuid { get; }
    }

    public static class TodoSceneCloneEvents
    {
        internal static void RaiseSceneCloned(string sourceScenePath, string sourceSceneGuid, string targetScenePath,
            string targetSceneGuid)
        {
            var args = new TodoSceneClonedEventArgs(sourceScenePath, sourceSceneGuid, targetScenePath, targetSceneGuid);

            try
            {
                TodoExtensionRegistry.SceneCloneExtension?.OnSceneCloned(args);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    /// <summary>
    /// 场景引用工具：负责"另存为场景"的克隆逻辑
    /// 供拖拽入口和右键菜单入口共同复用
    /// </summary>
    internal static class TodoSceneReferenceUtility
    {
        /// <summary>
        /// 根据用户是否选择"另存为场景"，决定 TodoItem 最终引用的场景路径和 GUID。
        /// </summary>
        /// <param name="sourceScene">待办关联的源场景</param>
        /// <param name="saveAsScene">是否执行另存为场景操作</param>
        /// <param name="targetScenePath">输出：最终引用的场景路径</param>
        /// <param name="targetSceneGuid">输出：最终引用的场景 GUID</param>
        /// <param name="isClonedSceneReference">输出：最终引用是否由“另存为场景”创建</param>
        /// <returns>true 表示成功（可继续创建 TodoItem）；false 表示用户取消或发生错误</returns>
        public static bool TryGetSceneReferenceForTodo(
            Scene sourceScene, bool saveAsScene,
            out string targetScenePath, out string targetSceneGuid, out bool isClonedSceneReference)
        {
            var originalPath = sourceScene.path ?? string.Empty;
            targetScenePath = originalPath;
            targetSceneGuid = string.IsNullOrEmpty(originalPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(originalPath);
            isClonedSceneReference = false;

            if (!saveAsScene)
                return true;

            // 用户勾选了"另存为场景"，必须先将场景存盘才能克隆
            // 显示自定义确认弹窗并调用 SaveScene，避免"Don't Save"语义导致克隆旧版本
            if (!SaveSceneBeforeClone(sourceScene))
                return false;

            // 保存后重新读取路径（处理之前未保存到磁盘的新场景在此路径更新的情况）
            var savedPath = sourceScene.path ?? string.Empty;
            if (string.IsNullOrEmpty(savedPath))
            {
                EditorUtility.DisplayDialog("另存为场景失败",
                    "当前场景尚未保存到磁盘，无法创建场景副本。请先手动另存场景后再试。", "确定");
                return false;
            }

            var cloneScenePath = GenerateUniqueCloneScenePath(savedPath, DateTime.Now);
            if (!AssetDatabase.CopyAsset(savedPath, cloneScenePath))
            {
                EditorUtility.DisplayDialog("另存为场景失败",
                    $"复制场景失败：\n{savedPath}\n->\n{cloneScenePath}", "确定");
                return false;
            }

            AssetDatabase.Refresh();
            targetScenePath = cloneScenePath;
            targetSceneGuid = AssetDatabase.AssetPathToGUID(cloneScenePath);
            isClonedSceneReference = true;

            if (string.IsNullOrEmpty(targetSceneGuid))
            {
                EditorUtility.DisplayDialog("另存为场景失败",
                    $"场景已复制但无法获取其 GUID，请手动检查：\n{cloneScenePath}", "确定");
                return false;
            }

            var sourceSceneGuid = AssetDatabase.AssetPathToGUID(savedPath);
            TodoSceneCloneEvents.RaiseSceneCloned(savedPath, sourceSceneGuid, targetScenePath, targetSceneGuid);

            return true;
        }

        /// <summary>
        /// 提示用户确认保存，然后强制保存当前打开的场景。
        /// 返回 false 表示用户取消或保存失败。
        /// </summary>
        private static bool SaveSceneBeforeClone(Scene sourceScene)
        {
            if (!sourceScene.isDirty)
                return true;

            var sceneName = string.IsNullOrEmpty(sourceScene.name) ? "当前场景" : sourceScene.name;
            var confirmed = EditorUtility.DisplayDialog(
                "另存为场景",
                $"需要先保存 [{sceneName}] 才能创建场景副本，是否立即保存？",
                "保存",
                "取消");

            if (!confirmed)
                return false;

            var saved = EditorSceneManager.SaveScene(sourceScene);
            if (!saved)
            {
                EditorUtility.DisplayDialog("另存为场景失败",
                    $"场景 [{sceneName}] 保存失败，无法创建副本。", "确定");
            }

            return saved;
        }

        /// <summary>
        /// 生成不冲突的克隆场景路径，格式为 原场景名_yyyy-MM-dd_HH-mm.unity，
        /// 同一分钟内多次克隆时追加 _01、_02 后缀。
        /// 使用 File.Exists 检查磁盘，确保检测结果准确。
        /// </summary>
        private static string GenerateUniqueCloneScenePath(string sourceScenePath, DateTime now)
        {
            var directory = (Path.GetDirectoryName(sourceScenePath) ?? "Assets").Replace("\\", "/");
            var extension = Path.GetExtension(sourceScenePath);
            var sceneName = Path.GetFileNameWithoutExtension(sourceScenePath);
            var baseName = $"{sceneName}_{now:yyyy-MM-dd_HH-mm}";

            var candidatePath = $"{directory}/{baseName}{extension}";
            if (!SceneFileExists(candidatePath))
                return candidatePath;

            for (var index = 1; index <= 99; index++)
            {
                candidatePath = $"{directory}/{baseName}_{index:00}{extension}";
                if (!SceneFileExists(candidatePath))
                    return candidatePath;
            }

            // 极端情况：同一分钟超过 99 个同名场景，回退到秒级时间戳
            return $"{directory}/{sceneName}_{now:yyyy-MM-dd_HH-mm-ss}{extension}";
        }

        /// <summary>
        /// 通过磁盘文件检查候选路径是否已存在，比 AssetDatabase 更准确
        /// </summary>
        private static bool SceneFileExists(string assetPath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), assetPath));
            return File.Exists(fullPath);
        }
    }
}
