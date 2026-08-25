using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Battle.Ability;
using Game.Battle.Combat.Feedback;
using GameMain2.Framework.Audio;
using GameMain2.Framework.Manager;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    public sealed class UIManager : MonoBehaviour
    {
        private const float BossBattleBgmFadeOutSeconds = 1f;

        // UIManager 独立静态实例，不走项目现有 SingletonManager 生命周期。
        private static UIManager s_instance;

        // 已实例化的面板缓存；关闭面板只隐藏，不立即销毁。
        private readonly Dictionary<UIType, UIPanelBase> m_panelInstances = new Dictionary<UIType, UIPanelBase>();

        // Addressables 实例化句柄缓存，用于 UIManager 销毁时释放实例。
        private readonly Dictionary<UIType, AsyncOperationHandle<GameObject>> m_panelHandles =
            new Dictionary<UIType, AsyncOperationHandle<GameObject>>();

        // 面板注册表，由 UIPanelAttribute 扫描生成，保存层级、地址和面板类型。
        private readonly UIPanelRegistry m_panelRegistry = new UIPanelRegistry();

        // UI 分层根节点缓存，面板打开时会挂到对应层级下。
        private readonly Dictionary<UILayer, RectTransform> m_layerRoots = new Dictionary<UILayer, RectTransform>();

        // 拾取提示在面板异步加载前先进入这里，避免同一帧批量提交时丢提示。
        private readonly Queue<PickupTipData> m_pendingPickupTips = new Queue<PickupTipData>();

        // 正在异步加载的面板集合，防止同一面板被重复加载。
        private readonly HashSet<UIType> m_loadingPanels = new HashSet<UIType>();

        // 加载完成前已经收到关闭请求的面板集合，用于处理异步打开/关闭竞态。
        private readonly HashSet<UIType> m_pendingClosePanels = new HashSet<UIType>();

        // 外部系统临时阻断玩法输入；暂停快捷键不走该入口，因此仍可响应。
        private int m_externalGameplayInputBlockCount;

        // 场景加载等全局流程阻断所有玩家输入，包含玩法输入和 UI 快捷键。
        private int m_fullInputBlockCount;

        // 全局 UI Canvas，运行时自动创建并常驻。
        private Canvas m_canvas;

        // 全局 EventSystem 引用，用于复用或清理重复 EventSystem。
        private EventSystem m_eventSystem;

        // 场景流程管理器引用，用于订阅场景加载事件。
        private SceneFlowManager m_sceneFlowManager;

        // 首次场景 UI 是否已经应用，避免启动阶段重复切换。
        private bool m_hasAppliedInitialSceneUI;

        // 本次运行是否已经在 MainScene 显示过按键提示。
        private bool m_hasShownPlayerControlsThisRun;

        // 拾取提示队列刷新状态，确保同一时间只有一个异步刷新流程。
        private bool m_isFlushingPickupTips;

        // UI 运行态代次；返回主菜单会递增，用于丢弃重置前发起的异步面板加载结果。
        private int m_runtimeStateVersion;

        /// <summary>
        /// 获取或创建全局 UI 管理器实例。
        /// </summary>
        public static UIManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindObjectOfType<UIManager>();
                }

                if (s_instance == null)
                {
                    GameObject go = new GameObject("[UIManager]");
                    s_instance = go.AddComponent<UIManager>();
                }

                return s_instance;
            }
        }

        /// <summary>
        /// 在首个场景加载前提前创建 UI 管理器实例。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
        }

        /// <summary>
        /// 初始化单例引用、UI 根节点和场景流程事件。
        /// </summary>
        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeRoot();
            BindSceneFlowEvents();
        }

        /// <summary>
        /// 延迟一帧补做首个场景 UI 初始化，防止场景事件在极端时序下错过。
        /// </summary>
        private IEnumerator Start()
        {
            yield return null;
            if (!m_hasAppliedInitialSceneUI)
            {
                ApplySceneUI(SceneManager.GetActiveScene().name);
                m_hasAppliedInitialSceneUI = true;
            }
        }

        /// <summary>
        /// 持续处理当前场景下的快捷键输入。
        /// </summary>
        private void Update()
        {
            HandleShortcuts(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// 解绑场景流程事件并释放 UI 面板实例缓存。
        /// </summary>
        private void OnDestroy()
        {
            if (s_instance != this)
            {
                return;
            }

            UnbindSceneFlowEvents();
            m_runtimeStateVersion++;
            m_loadingPanels.Clear();
            m_pendingClosePanels.Clear();
            foreach (AsyncOperationHandle<GameObject> handle in m_panelHandles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.ReleaseInstance(handle);
                }
            }

            m_panelHandles.Clear();
            m_panelInstances.Clear();
            s_instance = null;
        }

        /// <summary>
        /// 订阅场景流程管理器事件，并同步当前场景的 UI 状态。
        /// </summary>
        private void BindSceneFlowEvents()
        {
            SceneFlowManager flowManager = SceneFlowManager.Instance;
            if (m_sceneFlowManager == flowManager)
            {
                return;
            }

            UnbindSceneFlowEvents();
            m_sceneFlowManager = flowManager;
            m_sceneFlowManager.SceneLoadStarted += OnSceneLoadStarted;
            m_sceneFlowManager.SceneLoaded += OnSceneLoaded;
            m_sceneFlowManager.SceneLoadFailed += OnSceneLoadFailed;
        }

        /// <summary>
        /// 取消订阅场景流程管理器事件。
        /// </summary>
        private void UnbindSceneFlowEvents()
        {
            if (m_sceneFlowManager == null)
            {
                return;
            }

            m_sceneFlowManager.SceneLoadStarted -= OnSceneLoadStarted;
            m_sceneFlowManager.SceneLoaded -= OnSceneLoaded;
            m_sceneFlowManager.SceneLoadFailed -= OnSceneLoadFailed;
            m_sceneFlowManager = null;
        }

        /// <summary>
        /// 异步打开指定 UI 面板，调用方不需要等待返回值时可直接使用。
        /// </summary>
        public void OpenPanel(UIType type, object userData = null)
        {
            _ = OpenPanelAsync(type, userData);
        }

        /// <summary>
        /// 异步打开指定 UI 面板，并返回已打开的面板实例。
        /// </summary>
        public async Task<UIPanelBase> OpenPanelAsync(UIType type, object userData = null)
        {
            InitializeRoot();
            int runtimeStateVersion = m_runtimeStateVersion;

            if (m_panelInstances.TryGetValue(type, out UIPanelBase panel))
            {
                m_pendingClosePanels.Remove(type);
                ActivatePanel(panel, userData);
                return panel;
            }

            if (m_loadingPanels.Contains(type))
            {
                return null;
            }

            m_loadingPanels.Add(type);
            panel = await InstantiatePanelAsync(type, runtimeStateVersion);
            if (!IsRuntimeStateVersionCurrent(runtimeStateVersion))
            {
                return null;
            }

            m_loadingPanels.Remove(type);

            if (panel == null)
            {
                Debug.LogError($"打开 UI 面板失败：{type}");
                return null;
            }

            panel.Bind(type, this);
            panel.gameObject.SetActive(false);
            m_panelInstances[type] = panel;

            if (m_pendingClosePanels.Remove(type))
            {
                panel.OnClose();
                return panel;
            }

            ActivatePanel(panel, userData);
            return panel;
        }

        /// <summary>
        /// 关闭指定 UI 面板，若面板尚在加载中则记录关闭请求。
        /// </summary>
        public void ClosePanel(UIType type)
        {
            if (!m_panelInstances.TryGetValue(type, out UIPanelBase panel))
            {
                if (m_loadingPanels.Contains(type))
                {
                    m_pendingClosePanels.Add(type);
                }

                return;
            }
            panel.OnClose();
            RefreshBattleCursorState();
        }

        /// <summary>
        /// 判断指定 UI 面板当前是否处于激活状态。
        /// </summary>
        public bool IsPanelOpen(UIType type)
        {
            return m_panelInstances.TryGetValue(type, out UIPanelBase panel) && panel.gameObject.activeSelf;
        }

        /// <summary>玩家装备快照恢复后，刷新已存在战斗 HUD 的武器和技能图标。</summary>
        public void RefreshBattleHudEquipmentSlots()
        {
            if (!m_panelInstances.TryGetValue(UIType.BattleHud, out UIPanelBase panel))
            {
                return;
            }

            BattleHudPanel battleHudPanel = panel as BattleHudPanel;
            if (battleHudPanel == null)
            {
                return;
            }

            battleHudPanel.RefreshEquipmentSlots();
        }

        /// <summary>返回主菜单时释放全部已实例化 UI 面板，保留 UIManager、Canvas 和 EventSystem 基础壳。</summary>
        public void ResetRuntimeStateForMainMenu()
        {
            m_runtimeStateVersion++;
            m_externalGameplayInputBlockCount = 0;
            m_pendingPickupTips.Clear();
            m_loadingPanels.Clear();
            m_pendingClosePanels.Clear();
            m_isFlushingPickupTips = false;
            ReleaseCachedPanels();
            UnlockCursor();
        }

        /// <summary>增加一次外部玩法输入阻断，处决等强控流程用它屏蔽移动、攻击、锁定和视角输入。</summary>
        public void PushGameplayInputBlock()
        {
            m_externalGameplayInputBlockCount++;
        }

        /// <summary>释放一次外部玩法输入阻断，调用次数必须和 PushGameplayInputBlock 成对。</summary>
        public void PopGameplayInputBlock()
        {
            m_externalGameplayInputBlockCount--;
            if (m_externalGameplayInputBlockCount < 0)
            {
                Debug.LogError("玩法输入阻断释放次数超过获取次数。", this);
                m_externalGameplayInputBlockCount = 0;
            }
        }

        /// <summary>增加一次全局输入阻断，场景加载黑屏阶段用它屏蔽玩法输入和暂停快捷键。</summary>
        public void PushFullInputBlock()
        {
            m_fullInputBlockCount++;
        }

        /// <summary>释放一次全局输入阻断，调用次数必须和 PushFullInputBlock 成对。</summary>
        public void PopFullInputBlock()
        {
            m_fullInputBlockCount--;
            if (m_fullInputBlockCount < 0)
            {
                Debug.LogError("全局输入阻断释放次数超过获取次数。", this);
                m_fullInputBlockCount = 0;
            }
        }

        /// <summary>判断当前是否阻断玩法输入，包含全局输入锁、外部玩法锁和阻断型 UI 面板。</summary>
        public bool IsGameplayInputBlocked()
        {
            if (m_fullInputBlockCount > 0)
            {
                return true;
            }

            if (m_externalGameplayInputBlockCount > 0)
            {
                return true;
            }

            foreach (KeyValuePair<UIType, UIPanelBase> pair in m_panelInstances)
            {
                if (pair.Value == null || !pair.Value.gameObject.activeSelf)
                {
                    continue;
                }

                if (GetPanelDefinition(pair.Key).BlockGameplayInput)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 打开加载遮罩并显示加载文案。
        /// </summary>
        public void ShowLoading(string message = "加载中...")
        {
            OpenPanel(UIType.Loading, message);
        }

        /// <summary>
        /// 关闭加载遮罩。
        /// </summary>
        public void HideLoading()
        {
            ClosePanel(UIType.Loading);
        }

        /// <summary>
        /// 弹出一条顶部 Toast 提示。
        /// </summary>
        public void ShowToast(string message, float duration = 2f)
        {
            OpenPanel(UIType.Toast, new ToastData(message, duration));
        }

        /// <summary>在战斗 HUD 可见且没有阻塞面板时显示拾取成功提示。</summary>
        public void ShowPickupTip(PickupTipData data)
        {
            if (!CanShowPickupTip())
            {
                return;
            }

            m_pendingPickupTips.Enqueue(data);
            if (!m_isFlushingPickupTips)
            {
                _ = FlushPickupTipsAsync();
            }
        }

        /// <summary>按顺序把待显示拾取提示提交给拾取提示面板。</summary>
        private async Task FlushPickupTipsAsync()
        {
            m_isFlushingPickupTips = true;
            while (m_pendingPickupTips.Count > 0 && CanShowPickupTip())
            {
                PickupTipData data = m_pendingPickupTips.Dequeue();
                await OpenPanelAsync(UIType.PickupTip, data);
            }

            if (!CanShowPickupTip())
            {
                m_pendingPickupTips.Clear();
            }

            m_isFlushingPickupTips = false;
        }

        /// <summary>
        /// 打开确认弹窗并绑定确认、取消回调。
        /// </summary>
        public void ShowConfirm(
            string title,
            string message,
            System.Action onConfirm,
            System.Action onCancel = null,
            string confirmText = "确定",
            string cancelText = "取消")
        {
            OpenPanel(
                UIType.ConfirmDialog,
                new UIConfirmData(title, message, onConfirm, onCancel, confirmText, cancelText));
        }

        /// <summary>显示玩家死亡面板，并解锁鼠标供按钮交互。</summary>
        public void ShowDeathPanel()
        {
            FadeOutBossBattleBgm();
            ClosePanel(UIType.BossHealth);
            UnlockCursor();
            OpenPanel(UIType.Death);
        }

        /// <summary>显示 Boss 击杀胜利面板，并解锁鼠标供按钮交互。</summary>
        public void ShowVictoryPanel()
        {
            FadeOutBossBattleBgm();
            ClosePanel(UIType.BossHealth);
            UnlockCursor();
            OpenPanel(UIType.Victory);
        }

        /// <summary>Boss 战结束或玩家死亡时淡出 Boss 战背景音乐，避免菜单或结算界面继续播放战斗音乐。</summary>
        private static void FadeOutBossBattleBgm()
        {
            if (SoundManager.TryGetInstance(out SoundManager soundManager))
            {
                soundManager.StopAll(SoundId.BossBattleBgm, BossBattleBgmFadeOutSeconds);
            }
        }

        /// <summary>显示 Boss 血条面板，并把 Boss 属性源传给面板刷新生命值。</summary>
        public void ShowBossHealth(ICombatAttributes bossAttributes, string bossName)
        {
            OpenPanel(UIType.BossHealth, new BossHealthPanelData(bossAttributes, bossName));
        }

        /// <summary>关闭 Boss 血条面板，用于切场景、Boss 死亡或战斗重置。</summary>
        public void HideBossHealth()
        {
            ClosePanel(UIType.BossHealth);
        }

        /// <summary>
        /// 打开设置面板。
        /// </summary>
        public void ShowSettings()
        {
            OpenPanel(UIType.Settings);
        }

        /// <summary>
        /// 接收场景开始加载通知并打开加载遮罩。
        /// </summary>
        private void OnSceneLoadStarted(string sceneName)
        {
            Time.timeScale = 1f;
            ShowLoading("加载中...");
        }

        /// <summary>
        /// 接收场景加载失败通知并关闭加载遮罩后提示错误。
        /// </summary>
        private void OnSceneLoadFailed(string sceneName)
        {
            HideLoading();
            ShowToast($"无法加载场景：{sceneName}");
        }

        /// <summary>
        /// 接收场景完成通知并应用对应场景的默认 UI。
        /// </summary>
        private void OnSceneLoaded(string sceneName)
        {
            EnsureEventSystem();
            ApplySceneUI(sceneName);
            HideLoading();
            m_hasAppliedInitialSceneUI = true;
        }

        /// <summary>
        /// 按快捷键定义处理当前场景下的面板切换。
        /// </summary>
        private void HandleShortcuts(string sceneName)
        {
            if (IsShortcutInputBlocked())
            {
                return;
            }

            IReadOnlyList<UIShortcutDefinition> shortcutDefinitions = m_panelRegistry.ShortcutDefinitions;
            for (int i = 0; i < shortcutDefinitions.Count; i++)
            {
                UIShortcutDefinition definition = shortcutDefinitions[i];
                if (!definition.MatchesScene(sceneName))
                {
                    continue;
                }

                // UI 只读取输入意图，底层输入实现仍收口在 InputManager。
                if (!InputManager.Instance.IsKeyPressed(definition.Key))
                {
                    continue;
                }

                TogglePanelShortcut(definition);
                break;
            }
        }

        /// <summary>判断当前是否禁止 UI 快捷键，场景加载期间暂停键也不能响应。</summary>
        private bool IsShortcutInputBlocked()
        {
            return m_fullInputBlockCount > 0;
        }

        /// <summary>
        /// 根据快捷键定义切换对应面板。
        /// </summary>
        private void TogglePanelShortcut(UIShortcutDefinition definition)
        {
            if (definition.PauseGame)
            {
                if (IsPanelOpen(definition.Type))
                {
                    ResumeGame();
                    return;
                }

                PauseGame();
                return;
            }

            if (definition.Toggle && IsPanelOpen(definition.Type))
            {
                ClosePanel(definition.Type);

                return;
            }

            if (definition.UnlockCursor)
            {
                UnlockCursor();
            }

            OpenPanel(definition.Type);
        }

        /// <summary>
        /// 暂停游戏并打开暂停面板。
        /// </summary>
        public void PauseGame()
        {
            CombatHitStopController.CancelActiveStopForExternalPause();
            Time.timeScale = 0f;
            UnlockCursor();
            OpenPanel(UIType.Pause);
        }

        /// <summary>
        /// 恢复游戏并关闭暂停相关面板。
        /// </summary>
        public void ResumeGame()
        {
            Time.timeScale = 1f;
            ClosePanel(UIType.Settings);
            ClosePanel(UIType.ConfirmDialog);
            ClosePanel(UIType.Pause);
            RefreshBattleCursorState();
        }

        /// <summary>
        /// 退出游戏，编辑器中则直接停止播放。
        /// </summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 获取指定 UI 层级的根节点。
        /// </summary>
        internal RectTransform GetLayerRoot(UILayer layer)
        {
            InitializeRoot();
            return m_layerRoots[layer];
        }

        /// <summary>
        /// 按面板定义创建面板实例，优先走 Addressables，失败后按约定兜底。
        /// </summary>
        private async Task<UIPanelBase> InstantiatePanelAsync(UIType type, int runtimeStateVersion)
        {
            UIPanelDefinition definition = GetPanelDefinition(type);
            UILayer layer = definition.Layer;
            RectTransform parent = GetLayerRoot(layer);
            string address = definition.Address;

            if (!string.IsNullOrEmpty(address))
            {
                AsyncOperationHandle<GameObject> handle = default;
                try
                {
                    handle = Addressables.InstantiateAsync(address, parent, false);
                    await handle.Task;
                    if (!IsRuntimeStateVersionCurrent(runtimeStateVersion))
                    {
                        if (handle.IsValid())
                        {
                            Addressables.ReleaseInstance(handle);
                        }

                        return null;
                    }

                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                    {
                        UIPanelBase panel = handle.Result.GetComponent<UIPanelBase>();
                        if (panel != null)
                        {
                            UIElementFactory.Stretch(handle.Result.GetComponent<RectTransform>());
                            m_panelHandles[type] = handle;
                            return panel;
                        }

                        Debug.LogWarning($"UI 预制体未挂载 UIPanelBase：{address}");
                        Addressables.ReleaseInstance(handle);
                    }
                    else
                    {
                        string errorMessage = handle.IsValid() && handle.OperationException != null
                            ? handle.OperationException.Message
                            : "未知原因";
                        Debug.LogWarning($"Addressables 加载 UI 面板失败：{address}\n{errorMessage}");
                        if (handle.IsValid())
                        {
                            Addressables.ReleaseInstance(handle);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    if (handle.IsValid())
                    {
                        Addressables.ReleaseInstance(handle);
                    }

                    Debug.LogWarning($"Addressables 加载 UI 面板抛出异常：{address}\n{ex.Message}");
                }

                if (RequiresPrefabInstance(type))
                {
                    Debug.LogError($"UI 面板必须通过完整预制体加载，禁止使用运行时兜底：{type}, address={address}");
                    return null;
                }
            }

            if (!IsRuntimeStateVersionCurrent(runtimeStateVersion))
            {
                return null;
            }

            return CreateFallbackPanel(type, parent);
        }

        /// <summary>判断异步面板加载是否仍属于当前 UI 运行态。</summary>
        private bool IsRuntimeStateVersionCurrent(int runtimeStateVersion)
        {
            return runtimeStateVersion == m_runtimeStateVersion;
        }

        /// <summary>释放所有缓存面板实例和 Addressables 句柄，下一场景按需重新加载 UI。</summary>
        private void ReleaseCachedPanels()
        {
            HashSet<UIType> addressablePanelTypes = new HashSet<UIType>();
            foreach (KeyValuePair<UIType, AsyncOperationHandle<GameObject>> pair in m_panelHandles)
            {
                if (pair.Value.IsValid())
                {
                    Addressables.ReleaseInstance(pair.Value);
                    addressablePanelTypes.Add(pair.Key);
                }
            }

            foreach (KeyValuePair<UIType, UIPanelBase> pair in m_panelInstances)
            {
                if (addressablePanelTypes.Contains(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                Destroy(pair.Value.gameObject);
            }

            m_panelHandles.Clear();
            m_panelInstances.Clear();
        }

        /// <summary>判断指定面板是否必须由完整预制体实例化，避免运行时空壳面板产生误导错误。</summary>
        private static bool RequiresPrefabInstance(UIType type)
        {
            return type == UIType.Bag
                || type == UIType.PickupTip
                || type == UIType.Death
                || type == UIType.Victory
                || type == UIType.BossHealth
                || type == UIType.PlayerControls;
        }

        /// <summary>
        /// 在没有可用预制体时按约定创建运行时兜底面板。
        /// </summary>
        private UIPanelBase CreateFallbackPanel(UIType type, Transform parent)
        {
            UIPanelDefinition definition = GetPanelDefinition(type);
            if (definition.PanelType == null)
            {
                Debug.LogError($"未找到 UI 面板类型定义：{type}");
                return null;
            }

            GameObject go = new GameObject($"{type}RuntimePanel", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            UIElementFactory.Stretch(rect);

            return go.AddComponent(definition.PanelType) as UIPanelBase;
        }

        /// <summary>
        /// 将面板挂到目标层级并触发打开回调。
        /// </summary>
        private void ActivatePanel(UIPanelBase panel, object userData)
        {
            RectTransform targetLayer = GetLayerRoot(GetPanelDefinition(panel.Type).Layer);
            if (panel.transform.parent != targetLayer)
            {
                panel.transform.SetParent(targetLayer, false);
            }

            panel.transform.SetAsLastSibling();
            ClosePickupTipForBlockingPanel(panel.Type);
            panel.OnOpen(userData);
            RefreshBattleCursorState();
        }

        /// <summary>打开阻塞玩法输入的面板时关闭拾取提示，避免菜单上层继续显示。</summary>
        private void ClosePickupTipForBlockingPanel(UIType type)
        {
            if (type == UIType.PickupTip)
            {
                return;
            }

            UIPanelDefinition definition = GetPanelDefinition(type);
            if (definition.BlockGameplayInput)
            {
                m_pendingPickupTips.Clear();
                ClosePanel(UIType.PickupTip);
            }
        }

        /// <summary>
        /// 获取指定面板类型的注册信息，缺失时按约定兜底生成。
        /// </summary>
        private UIPanelDefinition GetPanelDefinition(UIType type)
        {
            return m_panelRegistry.GetDefinition(type);
        }

        /// <summary>
        /// 创建全局 UI 根节点、Canvas 和层级容器。
        /// </summary>
        private void InitializeRoot()
        {
            if (m_canvas != null)
            {
                return;
            }

            GameObject canvasGo = new GameObject("GlobalUICanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            m_canvas = canvasGo.AddComponent<Canvas>();
            m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
            UIElementFactory.Stretch(canvasGo.GetComponent<RectTransform>());

            CreateLayer(UILayer.Background);
            CreateLayer(UILayer.Normal);
            CreateLayer(UILayer.Popup);
            CreateLayer(UILayer.Overlay);
            CreateLayer(UILayer.Toast);
            EnsureEventSystem();
        }

        /// <summary>
        /// 创建单个 UI 层级根节点。
        /// </summary>
        private void CreateLayer(UILayer layer)
        {
            RectTransform rect = UIElementFactory.CreateRect(layer.ToString(), m_canvas.transform);
            UIElementFactory.Stretch(rect);
            m_layerRoots[layer] = rect;
        }

        /// <summary>
        /// 确保场景里只有一个可用的 EventSystem。
        /// </summary>
        private void EnsureEventSystem()
        {
            EventSystem[] eventSystems = FindObjectsOfType<EventSystem>();
            if (m_eventSystem == null)
            {
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i].gameObject.name == "GlobalEventSystem")
                    {
                        m_eventSystem = eventSystems[i];
                        break;
                    }
                }
            }

            if (m_eventSystem == null && eventSystems.Length > 0)
            {
                m_eventSystem = eventSystems[0];
                m_eventSystem.gameObject.name = "GlobalEventSystem";
                DontDestroyOnLoad(m_eventSystem.gameObject);
            }

            if (m_eventSystem == null)
            {
                GameObject eventSystemGo = new GameObject("GlobalEventSystem");
                m_eventSystem = eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(eventSystemGo);
                return;
            }

            eventSystems = FindObjectsOfType<EventSystem>();
            for (int i = 0; i < eventSystems.Length; i++)
            {
                if (eventSystems[i] == m_eventSystem)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(eventSystems[i].gameObject);
                }
                else
                {
                    DestroyImmediate(eventSystems[i].gameObject);
                }
            }
        }

        /// <summary>
        /// 根据当前场景名切换默认 UI。
        /// </summary>
        private void ApplySceneUI(string sceneName)
        {
            Time.timeScale = 1f;
            CloseShortcutPanels();
            m_pendingPickupTips.Clear();
            ClosePanel(UIType.PickupTip);
            ClosePanel(UIType.BossHealth);
            ClosePanel(UIType.Victory);

            if (sceneName == SceneNames.MenuScene)
            {
                ClosePanel(UIType.BattleHud);
                ClosePanel(UIType.Pause);
                ClosePanel(UIType.Settings);
                ClosePanel(UIType.ConfirmDialog);
                ClosePanel(UIType.Death);
                ClosePanel(UIType.PlayerControls);
                UnlockCursor();
                OpenPanel(UIType.MainMenu);
                return;
            }

            if (IsBattleGameplayScene(sceneName))
            {
                ClosePanel(UIType.MainMenu);
                ClosePanel(UIType.Pause);
                ClosePanel(UIType.Settings);
                ClosePanel(UIType.ConfirmDialog);
                ClosePanel(UIType.Death);
                ClosePanel(UIType.PlayerControls);
                LockCursorForBattle();
                OpenPanel(UIType.BattleHud);

                if (sceneName == SceneNames.BattleScene && !m_hasShownPlayerControlsThisRun)
                {
                    m_hasShownPlayerControlsThisRun = true;
                    OpenPanel(UIType.PlayerControls);
                }
            }
        }

        /// <summary>解锁鼠标并显示光标，供菜单和弹窗交互。</summary>
        private static void UnlockCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        /// <summary>关闭所有快捷键面板，用于场景切换时清理 UI 状态。</summary>
        private void CloseShortcutPanels()
        {
            IReadOnlyList<UIShortcutDefinition> shortcutDefinitions = m_panelRegistry.ShortcutDefinitions;
            for (int i = 0; i < shortcutDefinitions.Count; i++)
            {
                ClosePanel(shortcutDefinitions[i].Type);
            }
        }

        /// <summary>根据当前战斗场景的可交互面板状态刷新鼠标锁定。</summary>
        private void RefreshBattleCursorState()
        {
            if (!IsBattleGameplayScene(SceneManager.GetActiveScene().name))
            {
                return;
            }

            bool hasInteractivePanel =
                HasOpenCursorUnlockShortcutPanel()
                || IsPanelOpen(UIType.Pause)
                || IsPanelOpen(UIType.Settings)
                || IsPanelOpen(UIType.ConfirmDialog)
                || IsPanelOpen(UIType.Death)
                || IsPanelOpen(UIType.Victory)
                || IsPanelOpen(UIType.PlayerControls);

            if (hasInteractivePanel)
            {
                UnlockCursor();
                return;
            }

            LockCursorForBattle();
        }

        /// <summary>判断当前 UI 状态是否允许播放拾取提示。</summary>
        private bool CanShowPickupTip()
        {
            if (!IsBattleGameplayScene(SceneManager.GetActiveScene().name))
            {
                return false;
            }

            return IsPanelOpen(UIType.BattleHud)
                && !IsPanelOpen(UIType.Bag)
                && !IsPanelOpen(UIType.Pause)
                && !IsPanelOpen(UIType.Settings)
                && !IsPanelOpen(UIType.ConfirmDialog)
                && !IsPanelOpen(UIType.Death)
                && !IsPanelOpen(UIType.Victory);
        }

        /// <summary>判断当前是否存在声明为解锁鼠标的快捷键面板。</summary>
        private bool HasOpenCursorUnlockShortcutPanel()
        {
            IReadOnlyList<UIShortcutDefinition> shortcutDefinitions = m_panelRegistry.ShortcutDefinitions;
            for (int i = 0; i < shortcutDefinitions.Count; i++)
            {
                UIShortcutDefinition definition = shortcutDefinitions[i];
                if (definition.UnlockCursor && IsPanelOpen(definition.Type))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将鼠标锁定到战斗输入状态。
        /// </summary>
        private static void LockCursorForBattle()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>判断当前场景是否使用战斗 HUD、暂停和背包快捷键逻辑。</summary>
        private static bool IsBattleGameplayScene(string sceneName)
        {
            return sceneName == SceneNames.BattleScene || sceneName == SceneNames.BossScene;
        }
    }

}
