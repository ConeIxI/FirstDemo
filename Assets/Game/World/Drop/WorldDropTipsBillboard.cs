using UnityEngine;

namespace Game.World.Drop
{
    public sealed class WorldDropTipsBillboard : MonoBehaviour
    {
        private Camera mainCamera;
        private Transform cachedTransform;

        /// <summary>缓存自身 Transform 和初始主摄像机引用，减少每帧重复查找。</summary>
        private void Awake()
        {
            cachedTransform = transform;
            ResolveMainCamera();
        }

        /// <summary>每帧在相机更新后对齐朝向，让拾取提示 UI 始终面向主摄像机。</summary>
        private void LateUpdate()
        {
            if (mainCamera == null)
            {
                ResolveMainCamera();
            }

            if (mainCamera == null)
            {
                return;
            }

            Transform cameraTransform = mainCamera.transform;
            cachedTransform.LookAt(
                cachedTransform.position + cameraTransform.rotation * Vector3.forward,
                cameraTransform.rotation * Vector3.up);
        }

        /// <summary>重新读取场景主摄像机，支持运行期相机重建后继续对齐。</summary>
        private void ResolveMainCamera()
        {
            mainCamera = Camera.main;
        }
    }
}
