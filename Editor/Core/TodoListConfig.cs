using System.Collections.Generic;
using UnityEngine;

namespace Editor.TodoList
{
    /// <summary>
    /// TodoList配置存储
    /// 存储路径: Assets/Editor/TodoList/TodoListConfig.asset
    /// </summary>
    [CreateAssetMenu(fileName = "TodoListConfig", menuName = "TodoList/配置", order = 1)]
    public class TodoListConfig : ScriptableObject
    {
        /// <summary>所有待办事项的引用列表</summary>
        [Tooltip("所有待办事项")]
        public List<TodoItem> TodoItems = new List<TodoItem>();
    }
}

