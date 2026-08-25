using System;
using System.Collections;
using System.Collections.Generic;
using GameMain2.Framework.Audio;
using GameMain2.Framework.Core;
using Game.Character.Equipment;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 统一驱动场景切换流程，并向 UI 层转发加载状态和场景完成事件。
    /// </summary>
    public sealed class SceneFlowManager : MonoBehaviour
    {
        private const float SceneFadeInSeconds = 0.45f;
        private const float SceneRevealDelaySeconds = 0.3f;
        private const float SceneFadeOutSeconds = 0.85f;
        private const int SceneFadeSortingOrder = 2000;
        private const string SceneFadeFontAddress = "Fonts/SIMHEI SDF.asset";
        private const string GobalPrefabAddress = "Character/Gobal.prefab";

        private static SceneFlowManager s_instance;

        private Coroutine m_sceneLoadCoroutine;
        private AsyncOperationHandle<TMP_FontAsset> m_fadeFontHandle;
        private AsyncOperationHandle<GameObject> m_gobalHandle;
        private TMP_FontAsset m_fadeFontAsset;
        private GameObject m_gobalInstance;
        private Image m_fadeImage;
        private TextMeshProUGUI m_fadeMessageText;
        private bool m_isManagedSceneLoading;
        private bool m_sceneInputBlocked;
        private bool m_recreateGobalForNextGameplayLoad;
        private int m_sceneLoadGeneration;
        private PlayerRestartSnapshot m_pendingRestartSnapshot;

        public event Action<string> SceneLoadStarted;

        public event Action<string> SceneLoaded;

        public event Action<string> SceneLoadFailed;

        /// <summary>
        /// 获取或创建场景流程管理器实例。
        /// </summary>
        public static SceneFlowManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindObjectOfType<SceneFlowManager>();
                }

                if (s_instance == null)
                {
                    GameObject go = new GameObject("[SceneFlowManager]");
                    s_instance = go.AddComponent<SceneFlowManager>();
                }

                return s_instance;
            }
        }

        /// <summary>
        /// 在首个场景加载前预先创建场景流程管理器。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
        }

        /// <summary>
        /// 初始化单例实例并订阅 Unity 场景加载回调。
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
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
        }

        /// <summary>
        /// 清理 Unity 场景回调和单例引用。
        /// </summary>
        private void OnDestroy()
        {
            if (s_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            ReleaseSceneInputBlock();
            ReleaseGobalInstance();
            ReleaseFadeFont();
            s_instance = null;
        }

        /// <summary>
        /// 发起一次场景加载，并在需要时终止上一轮尚未结束的加载协程。
        /// </summary>
        public void LoadScene(string sceneName)
        {
            m_sceneLoadGeneration++;
            if (m_sceneLoadCoroutine != null)
            {
                StopCoroutine(m_sceneLoadCoroutine);
                m_sceneLoadCoroutine = null;
                ReleaseSceneInputBlock();
            }

            m_sceneLoadCoroutine = StartCoroutine(LoadSceneRoutine(sceneName, m_sceneLoadGeneration));
        }

        /// <summary>
        /// 直接返回主菜单场景，供 UI 面板复用。
        /// </summary>
        public void ReturnToMainMenu()
        {
            LoadScene(SceneNames.MenuScene);
        }

        /// <summary>重新加载当前场景，并在黑屏后重建 Gobal 和玩家运行时对象。</summary>
        public void RestartCurrentScene()
        {
            CacheCurrentPlayerRestartSnapshot();
            m_recreateGobalForNextGameplayLoad = true;
            LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>缓存当前玩家死亡重开时允许继承的装备和药水数据。</summary>
        public void CacheCurrentPlayerRestartSnapshot()
        {
            int activeWeaponIndex = FindCurrentPlayerActiveWeaponIndex();
            m_pendingRestartSnapshot = BagInventoryManager.Instance.CreateRestartSnapshot(activeWeaponIndex);
        }

        /// <summary>
        /// 包装场景加载协程，确保异常和中断路径都会释放输入阻断与黑场状态。
        /// </summary>
        private IEnumerator LoadSceneRoutine(string sceneName, int loadGeneration)
        {
            Exception loadException = null;
            Stack<IEnumerator> routines = new Stack<IEnumerator>();
            routines.Push(RunSceneLoadRoutine(sceneName));

            try
            {
                while (routines.Count > 0)
                {
                    object current = null;
                    bool hasNext = false;
                    try
                    {
                        IEnumerator routine = routines.Peek();
                        hasNext = routine.MoveNext();
                        if (hasNext)
                        {
                            current = routines.Peek().Current;
                        }
                    }
                    catch (Exception ex)
                    {
                        loadException = ex;
                        break;
                    }

                    if (!hasNext)
                    {
                        DisposeSceneLoadEnumerator(routines.Pop());
                        continue;
                    }

                    if (current is IEnumerator nestedRoutine)
                    {
                        routines.Push(nestedRoutine);
                        continue;
                    }

                    yield return current;
                }

                if (loadException != null)
                {
                    Debug.LogError($"场景加载流程异常：{sceneName}", this);
                    Debug.LogException(loadException, this);
                    SceneLoadFailed?.Invoke(sceneName);
                    if (m_fadeImage != null)
                    {
                        yield return FadeOverlay(0f, SceneFadeInSeconds);
                    }
                }
            }
            finally
            {
                DisposeSceneLoadEnumerators(routines);
                m_isManagedSceneLoading = false;
                if (m_fadeMessageText != null)
                {
                    SetFadeMessageVisible(false);
                }

                ReleaseSceneInputBlock();
                if (loadGeneration == m_sceneLoadGeneration)
                {
                    m_sceneLoadCoroutine = null;
                }
            }
        }

        /// <summary>释放尚未自然结束的场景加载子协程，确保其中的 finally 逻辑有机会执行。</summary>
        private static void DisposeSceneLoadEnumerators(Stack<IEnumerator> routines)
        {
            while (routines.Count > 0)
            {
                DisposeSceneLoadEnumerator(routines.Pop());
            }
        }

        /// <summary>释放单个场景加载枚举器，主要用于中断和异常路径。</summary>
        private static void DisposeSceneLoadEnumerator(IEnumerator routine)
        {
            if (routine is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// 真正执行 Unity 的异步场景加载，并把开始和成功状态转发出去。
        /// </summary>
        private IEnumerator RunSceneLoadRoutine(string sceneName)
        {
            Time.timeScale = 1f;
            BlockSceneInput();
            EnsureFadeOverlay();
            SetFadeMessageVisible(false);
            BeginFadeFontLoad();

            yield return FadeOverlay(1f, SceneFadeInSeconds);
            yield return EnsureFadeFontLoaded();

            if (sceneName == SceneNames.MenuScene)
            {
                ResetRuntimeStateForMainMenu();
                yield return null;
            }

            SetFadeMessageVisible(true);
            SceneLoadStarted?.Invoke(sceneName);
            bool releasedGobal = ReleaseGobalForSceneLoad(sceneName);
            if (releasedGobal)
            {
                yield return null;
            }

            yield return EnsureGobalLoadedForGameplayScene(sceneName);

            m_isManagedSceneLoading = true;
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null)
            {
                throw new InvalidOperationException($"Unity 场景异步加载失败：{sceneName}");
            }

            while (!operation.isDone)
            {
                yield return null;
            }

            SceneLoaded?.Invoke(sceneName);
            ApplyPendingRestartSnapshot(sceneName);
            m_isManagedSceneLoading = false;
            yield return null;
            SetFadeMessageVisible(false);
            yield return new WaitForSecondsRealtime(SceneRevealDelaySeconds);
            yield return FadeOverlay(0f, SceneFadeOutSeconds);
        }

        /// <summary>场景切换开始时阻断所有玩家输入，避免黑屏加载阶段改变玩家和相机状态。</summary>
        private void BlockSceneInput()
        {
            if (m_sceneInputBlocked)
            {
                return;
            }

            UIManager.Instance.PushFullInputBlock();
            m_sceneInputBlocked = true;
        }

        /// <summary>场景黑屏淡出结束或流程中断时释放全局输入阻断。</summary>
        private void ReleaseSceneInputBlock()
        {
            if (!m_sceneInputBlocked)
            {
                return;
            }

            UIManager.Instance.PopFullInputBlock();
            m_sceneInputBlocked = false;
        }

        /// <summary>
        /// 进入战斗类场景前实例化 Gobal 预制体，让玩家和全局对象先进入跨场景生命周期。
        /// </summary>
        private IEnumerator EnsureGobalLoadedForGameplayScene(string sceneName)
        {
            if (!IsGameplayScene(sceneName) || m_gobalInstance != null)
            {
                yield break;
            }

            if (m_gobalHandle.IsValid())
            {
                Addressables.ReleaseInstance(m_gobalHandle);
                m_gobalHandle = default;
            }

            m_gobalHandle = Addressables.InstantiateAsync(GobalPrefabAddress);
            yield return m_gobalHandle;

            if (m_gobalHandle.Status != AsyncOperationStatus.Succeeded || m_gobalHandle.Result == null)
            {
                throw new InvalidOperationException($"加载全局对象失败：{GobalPrefabAddress}");
            }

            m_gobalInstance = m_gobalHandle.Result;
        }

        /// <summary>返回主菜单黑屏阶段重置运行态，只保留场景、UI、事件、音频等基础管理器外壳。</summary>
        private void ResetRuntimeStateForMainMenu()
        {
            m_recreateGobalForNextGameplayLoad = false;
            m_pendingRestartSnapshot = null;
            ReleaseGobalInstance();
            if (EventCenter.TryGetInstance(out EventCenter eventCenter))
            {
                eventCenter.ResetRuntimeStateForMainMenu();
            }

            BagInventoryManager.Instance.ResetRuntimeStateForMainMenu();
            if (SoundManager.TryGetInstance(out SoundManager soundManager))
            {
                soundManager.ResetRuntimeStateForMainMenu();
            }

            UIManager.Instance.ResetRuntimeStateForMainMenu();
        }

        /// <summary>按场景加载目的释放旧 Gobal；返回主菜单或重开战斗场景都必须销毁旧玩家。</summary>
        private bool ReleaseGobalForSceneLoad(string sceneName)
        {
            bool shouldReleaseGobal = sceneName == SceneNames.MenuScene
                || (m_recreateGobalForNextGameplayLoad && IsGameplayScene(sceneName));
            m_recreateGobalForNextGameplayLoad = false;

            if (sceneName == SceneNames.MenuScene)
            {
                m_pendingRestartSnapshot = null;
            }

            if (!shouldReleaseGobal)
            {
                return false;
            }

            ReleaseGobalInstance();
            return true;
        }

        /// <summary>
        /// 释放由场景流程管理器创建的 Gobal Addressables 实例。
        /// </summary>
        private void ReleaseGobalInstance()
        {
            if (!m_gobalHandle.IsValid())
            {
                m_gobalInstance = null;
                return;
            }

            Addressables.ReleaseInstance(m_gobalHandle);
            m_gobalHandle = default;
            m_gobalInstance = null;
        }

        /// <summary>
        /// 判断目标场景是否需要 Gobal 中的玩家和战斗全局对象。
        /// </summary>
        private static bool IsGameplayScene(string sceneName)
        {
            return sceneName == SceneNames.BattleScene || sceneName == SceneNames.BossScene;
        }

        /// <summary>把等待中的死亡重开快照应用到新 Gobal 下的玩家。</summary>
        private void ApplyPendingRestartSnapshot(string sceneName)
        {
            if (!IsGameplayScene(sceneName) || m_pendingRestartSnapshot == null)
            {
                return;
            }

            PersistentSceneRoot persistentRoot = m_gobalInstance == null
                ? null
                : m_gobalInstance.GetComponent<PersistentSceneRoot>();
            if (persistentRoot == null)
            {
                throw new InvalidOperationException("死亡重开后缺少 PersistentSceneRoot，无法恢复玩家装备和药水快照。");
            }

            persistentRoot.ApplyPlayerRestartSnapshot(m_pendingRestartSnapshot);
            m_pendingRestartSnapshot = null;
        }

        /// <summary>读取当前玩家激活武器槽，死亡重开后用于恢复当前手持武器。</summary>
        private static int FindCurrentPlayerActiveWeaponIndex()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            for (int i = 0; i < players.Length; i++)
            {
                EquipmentManager equipmentManager = players[i].GetComponent<EquipmentManager>();
                if (equipmentManager != null)
                {
                    return equipmentManager.ActiveWeaponIndex;
                }
            }

            return -1;
        }

        /// <summary>
        /// 确保场景切换黑场遮罩存在，并保持在全局 UI 最上层。
        /// </summary>
        private void EnsureFadeOverlay()
        {
            if (m_fadeImage != null)
            {
                return;
            }

            GameObject canvasGo = new GameObject("SceneFadeCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            Canvas fadeCanvas = canvasGo.AddComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.sortingOrder = SceneFadeSortingOrder;

            GameObject imageGo = new GameObject("FadeImage", typeof(RectTransform));
            imageGo.transform.SetParent(canvasGo.transform, false);

            RectTransform imageRect = imageGo.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            m_fadeImage = imageGo.AddComponent<Image>();
            m_fadeImage.raycastTarget = false;
            SetFadeAlpha(0f);

            GameObject textGo = new GameObject("FadeMessage", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);

            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(500f, 80f);

            m_fadeMessageText = textGo.AddComponent<TextMeshProUGUI>();
            m_fadeMessageText.text = "加载中...";
            m_fadeMessageText.fontSize = 34f;
            m_fadeMessageText.alignment = TextAlignmentOptions.Center;
            m_fadeMessageText.color = Color.white;
            SetFadeMessageVisible(false);
        }

        /// <summary>
        /// 开始异步加载黑场中文字体，加载过程与淡黑动画并行。
        /// </summary>
        private void BeginFadeFontLoad()
        {
            if (m_fadeFontAsset != null || m_fadeFontHandle.IsValid())
            {
                return;
            }

            m_fadeFontHandle = Addressables.LoadAssetAsync<TMP_FontAsset>(SceneFadeFontAddress);
        }

        /// <summary>
        /// 等待黑场中文字体加载完成，并绑定到 TMP 文本。
        /// </summary>
        private IEnumerator EnsureFadeFontLoaded()
        {
            if (m_fadeFontAsset != null)
            {
                yield break;
            }

            yield return m_fadeFontHandle;

            if (m_fadeFontHandle.Status != AsyncOperationStatus.Succeeded || m_fadeFontHandle.Result == null)
            {
                throw new InvalidOperationException($"加载场景过渡字体失败：{SceneFadeFontAddress}");
            }

            m_fadeFontAsset = m_fadeFontHandle.Result;
            m_fadeMessageText.font = m_fadeFontAsset;
        }

        /// <summary>
        /// 释放黑场字体 Addressables 句柄，避免场景流程管理器销毁时泄漏资源。
        /// </summary>
        private void ReleaseFadeFont()
        {
            if (!m_fadeFontHandle.IsValid())
            {
                return;
            }

            Addressables.Release(m_fadeFontHandle);
            m_fadeFontAsset = null;
        }

        /// <summary>
        /// 按真实时间平滑调整黑场透明度，避免暂停时间缩放影响镜头过渡。
        /// </summary>
        private IEnumerator FadeOverlay(float targetAlpha, float duration)
        {
            Color color = m_fadeImage.color;
            float startAlpha = color.a;
            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                float progress = (Time.realtimeSinceStartup - startTime) / duration;
                SetFadeAlpha(Mathf.Lerp(startAlpha, targetAlpha, progress));
                if (progress >= 1f)
                {
                    break;
                }

                yield return null;
            }

            SetFadeAlpha(targetAlpha);
        }

        /// <summary>
        /// 写入黑场遮罩透明度，黑色保持统一由这里维护。
        /// </summary>
        private void SetFadeAlpha(float alpha)
        {
            m_fadeImage.color = new Color(0f, 0f, 0f, alpha);
        }

        /// <summary>
        /// 控制黑场加载文案显隐，确保文字只在屏幕全黑后出现。
        /// </summary>
        private void SetFadeMessageVisible(bool visible)
        {
            m_fadeMessageText.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 接收 Unity 的场景加载完成回调，并把结果转发给订阅方。
        /// </summary>
        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_isManagedSceneLoading)
            {
                return;
            }

            SceneLoaded?.Invoke(scene.name);
        }
    }
}
