using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Editor.TodoList
{
    /// <summary>
    /// 待办事项状态
    /// </summary>
    public enum TodoStatus
    {
        /// <summary>待修复</summary>
        [InspectorName("待修复")]
        Pending = 0,

        /// <summary>待验证</summary>
        [InspectorName("待验证")]
        Verify = 1,

        /// <summary>已修复</summary>
        [InspectorName("已修复")]
        Fixed = 2,

        /// <summary>搁置</summary>
        [InspectorName("搁置")]
        Suspended = 3,

        /// <summary>打包检查</summary>
        [InspectorName("打包检查")]
        BuildCheck = 4
    }

    /// <summary>
    /// 待办事项类型
    /// </summary>
    public enum TodoType
    {
        /// <summary>资源引用</summary>
        [InspectorName("资源")]
        Asset = 0,

        /// <summary>场景物体</summary>
        [InspectorName("场景物体")]
        SceneObject = 1,

        /// <summary>纯文本</summary>
        [InspectorName("纯文本")]
        Text = 2,

        /// <summary>预制体内子物体</summary>
        [InspectorName("预制体子物体")]
        PrefabObject = 3
    }

    /// <summary>
    /// 评论数据
    /// </summary>
    [Serializable]
    public class TodoComment
    {
        /// <summary>评论内容</summary>
        public string Content;

        /// <summary>评论时间</summary>
        public string Time;

        /// <summary>评论作者</summary>
        public string Author;

        public TodoComment() { }

        public TodoComment(string content, string author)
        {
            Content = content;
            Author = author;
            Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    /// <summary>
    /// 单个待办事项数据 - ScriptableObject
    /// 每条TODO独立存储为一个.asset文件
    /// </summary>
    [CreateAssetMenu(fileName = "TodoItem", menuName = "TodoList/待办事项", order = 0)]
    public class TodoItem : ScriptableObject
    {
        /// <summary>待办描述/标题</summary>
        [Tooltip("待办事项的描述")]
        [TextArea(2, 5)]
        public string Description;

        /// <summary>状态</summary>
        public TodoStatus Status;

        /// <summary>类型</summary>
        public TodoType Type;

        /// <summary>创建时间</summary>
        [HideInInspector]
        public string CreateTime;

        /// <summary>更新时间</summary>
        [HideInInspector]
        public string UpdateTime;

        /// <summary>资源GUID（当Type为Asset时使用）</summary>
        [HideInInspector]
        public string AssetGuid;

        /// <summary>场景路径（当Type为SceneObject时使用）</summary>
        [HideInInspector]
        public string ScenePath;

        /// <summary>场景物体路径（层级路径）</summary>
        [HideInInspector]
        public string GameObjectPath;

        /// <summary>预制体内子物体路径（层级路径）</summary>
        [HideInInspector]
        public string PrefabChildPath;

        /// <summary>评论列表</summary>
        [ListDrawerSettings(
            NumberOfItemsPerPage = 10,
            CustomAddFunction = nameof(AddComment),
            CustomRemoveElementFunction = nameof(RemoveComment)
        )]
        [Tooltip("相关评论记录")]
        public List<TodoComment> Comments = new List<TodoComment>();

        // ========== 辅助字段（不序列化）==========

        /// <summary>正在编辑的评论内容</summary>
        [NonSerialized]
        public string EditingComment = "";

        /// <summary>是否展开显示评论</summary>
        [NonSerialized]
        public bool ExpandComments;

        private bool AddComment()
        {
            // 通过窗口添加，这里返回false禁用默认添加按钮
            return false;
        }

        private void RemoveComment(TodoComment comment)
        {
            Comments.Remove(comment);
        }

        /// <summary>
        /// 添加评论
        /// </summary>
        public void AddComment(string content, string author = null)
        {
            if (string.IsNullOrEmpty(content))
                return;

            var comment = new TodoComment(content, author ?? Environment.UserName);
            Comments.Add(comment);
            UpdateUpdateTime();
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// 删除评论
        /// </summary>
        public void RemoveCommentAt(int index)
        {
            if (index >= 0 && index < Comments.Count)
            {
                Comments.RemoveAt(index);
                UpdateUpdateTime();
                EditorUtility.SetDirty(this);
            }
        }

        /// <summary>
        /// 更新修改时间
        /// </summary>
        public void UpdateUpdateTime()
        {
            UpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获取资源
        /// </summary>
        public UnityEngine.Object GetAsset()
        {
            if (string.IsNullOrEmpty(AssetGuid))
                return null;

            var path = AssetDatabase.GUIDToAssetPath(AssetGuid);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        /// <summary>
        /// 定位到待办项对应的资源或物体
        /// </summary>
        public void Locate()
        {
            if (Type == TodoType.Asset)
            {
                var asset = GetAsset();
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                else
                {
                    EditorUtility.DisplayDialog("错误", "资源不存在或已被删除", "确定");
                }
            }
            else if (Type == TodoType.SceneObject)
            {
                LocateSceneObject();
            }
            else if (Type == TodoType.PrefabObject)
            {
                LocatePrefabChild();
            }
        }

        private void LocateSceneObject()
        {
            var currentScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            if (currentScene != ScenePath)
            {
                if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            }

            var obj = GameObject.Find(GameObjectPath);
            if (obj != null)
            {
                Selection.activeGameObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
            else
            {
                EditorUtility.DisplayDialog("错误", $"场景中未找到物体: {GameObjectPath}", "确定");
            }
        }

        private void LocatePrefabChild()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(AssetGuid);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("错误", "预制体不存在或已被删除", "确定");
                return;
            }

            // 在Prefab Mode中打开预制体
#if UNITY_2021_3_OR_NEWER
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(assetPath);
            if (prefabStage != null)
            {
                // 从预制体根节点开始查找
                var child = prefabStage.prefabContentsRoot.transform.Find(PrefabChildPath);
                if (child != null)
                {
                    Selection.activeGameObject = child.gameObject;
                    EditorGUIUtility.PingObject(child.gameObject);
                    return;
                }

                // 如果直接查找失败，尝试按名称查找（用于调试）
                var childName = System.IO.Path.GetFileName(PrefabChildPath);
                var found = false;

                // 递归查找所有同名子物体
                foreach (Transform t in prefabStage.prefabContentsRoot.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == childName && t != prefabStage.prefabContentsRoot.transform)
                    {
                        // 找到了，显示所有匹配项的路径
                        var actualPath = GetGameObjectPath(t.gameObject, prefabStage.prefabContentsRoot);
                        if (actualPath == PrefabChildPath || actualPath.EndsWith(PrefabChildPath))
                        {
                            Selection.activeGameObject = t.gameObject;
                            EditorGUIUtility.PingObject(t.gameObject);
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    Debug.LogWarning($"[TodoList] 未找到预制体子物体: {PrefabChildPath}\n预制体: {assetPath}");
                    EditorUtility.DisplayDialog("警告",
                        $"预制体中未找到子物体\n\n路径: {PrefabChildPath}\n预制体: {System.IO.Path.GetFileName(assetPath)}\n\n可能原因:\n1. 子物体已被删除或重命名\n2. 路径格式不正确",
                        "确定");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "无法打开预制体编辑模式", "确定");
            }
#else
            EditorUtility.DisplayDialog("提示", $"预制体子物体: {PrefabChildPath}\n预制体: {assetPath}", "确定");
#endif
        }

        /// <summary>
        /// 获取GameObject在预制体中的完整路径
        /// </summary>
        private static string GetGameObjectPath(GameObject obj, GameObject root)
        {
            if (obj == null || root == null)
                return "";

            var path = obj.name;
            var current = obj.transform.parent;

            // 向上遍历直到根节点
            while (current != null && current.gameObject != root.transform)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            // 如果到达了根节点，去掉根节点名称（因为查找时从根节点开始）
            if (current != null && current.gameObject == root.transform)
            {
                return path;
            }

            return path;
        }
    }
}
