namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// UI 子视图基础类，统一轻量视图的初始化、显示、隐藏和释放生命周期。
    /// </summary>
    public abstract class UIViewBase
    {
        /// <summary>
        /// 初始化视图依赖，重复调用时由具体视图自行保证幂等。
        /// </summary>
        public virtual void Init()
        {
        }

        /// <summary>
        /// 显示视图并绑定必要事件。
        /// </summary>
        public virtual void Show()
        {
        }

        /// <summary>
        /// 隐藏视图并暂停可见交互。
        /// </summary>
        public virtual void Hide()
        {
        }

        /// <summary>
        /// 释放视图持有的事件订阅和运行时状态。
        /// </summary>
        public virtual void Dispose()
        {
        }
    }
}
