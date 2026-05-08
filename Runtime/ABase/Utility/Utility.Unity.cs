using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Internal;
using Object = UnityEngine.Object;

namespace AlicizaX
{
    public static partial class Utility
    {
        /// <summary>
        /// Unity相关的实用函数。
        /// </summary>
        public static partial class Unity
        {
            private static GameObject _entity;
            private static MainBehaviour _behaviour;
            private static UtilityLoopService _loopService;

            #region 控制协程Coroutine

            public static Coroutine StartCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }
                return _behaviour.StartCoroutine(methodName);
            }

            public static Coroutine StartCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return null;
                }
                return _behaviour.StartCoroutine(routine);
            }

            public static Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }
                return _behaviour.StartCoroutine(methodName, value);
            }

            public static void StopCoroutine(string methodName)
            {
                if (string.IsNullOrEmpty(methodName))
                {
                    return;
                }

                if (_entity != null)
                {
                    _behaviour.StopCoroutine(methodName);
                }
            }

            public static void StopCoroutine(IEnumerator routine)
            {
                if (routine == null)
                {
                    return;
                }

                if (_entity != null)
                {
                    _behaviour.StopCoroutine(routine);
                }
            }

            public static void StopCoroutine(Coroutine routine)
            {
                if (routine == null)
                    return;

                if (_entity != null)
                {
                    _behaviour.StopCoroutine(routine);
                    routine = null;
                }
            }

            public static void StopAllCoroutines()
            {
                if (_entity != null)
                {
                    _behaviour.StopAllCoroutines();
                }
            }

            #endregion

            #region 注入UnityUpdate/FixedUpdate/LateUpdate

            /// <summary>
            /// 为给外部提供的 添加帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddUpdateListener(UnityAction fun)
            {
                EnsureLoopService().AddUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddFixedUpdateListener(UnityAction fun)
            {
                EnsureLoopService().AddFixedUpdateListener(fun);
            }

            /// <summary>
            /// 为给外部提供的 添加Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddLateUpdateListener(UnityAction fun)
            {
                EnsureLoopService().AddLateUpdateListener(fun);
            }

            /// <summary>
            /// 移除帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveUpdateListener(UnityAction fun)
            {
                _loopService?.RemoveUpdateListener(fun);
            }

            /// <summary>
            /// 移除物理帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveFixedUpdateListener(UnityAction fun)
            {
                _loopService?.RemoveFixedUpdateListener(fun);
            }

            /// <summary>
            /// 移除Late帧更新事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveLateUpdateListener(UnityAction fun)
            {
                _loopService?.RemoveLateUpdateListener(fun);
            }

            #endregion

            #region Unity Events 注入
            /// <summary>
            /// 为给外部提供的Destroy注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddDestroyListener(UnityAction fun)
            {
                _behaviour.AddDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的Destroy反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveDestroyListener(UnityAction fun)
            {
                _behaviour.RemoveDestroyListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnDrawGizmosListener(UnityAction fun)
            {
                EnsureLoopService().AddOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnDrawGizmos反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnDrawGizmosListener(UnityAction fun)
            {
                _loopService?.RemoveOnDrawGizmosListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnApplicationPauseListener(UnityAction<bool> fun)
            {
                _behaviour.AddOnApplicationPauseListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationPause反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnApplicationPauseListener(UnityAction<bool> fun)
            {
                _behaviour.RemoveOnApplicationPauseListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationQuit注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void AddOnApplicationQuitListener(UnityAction fun)
            {
                _behaviour.AddOnApplicationQuitListener(fun);
            }

            /// <summary>
            /// 为给外部提供的OnApplicationQuit反注册事件。
            /// </summary>
            /// <param name="fun"></param>
            public static void RemoveOnApplicationQuitListener(UnityAction fun)
            {
                _behaviour.RemoveOnApplicationQuitListener(fun);
            }
            #endregion

            public static void MakeEntity(Transform parent)
            {
                if (_entity != null)
                {
                    return;
                }

                _entity = new GameObject("[Unity.Utility]");
                _entity.SetActive(true);
                _entity.transform.SetParent(parent);
                _behaviour = _entity.AddComponent<MainBehaviour>();
                RegisterLoopServiceIfNeeded();
            }

            /// <summary>
            /// 释放Behaviour生命周期。
            /// </summary>
            public static void Shutdown()
            {

                if (_behaviour != null)
                {
                    _behaviour.Dispose();
                    _behaviour.Shutdown();
                }

                if (_loopService != null)
                {
                    _loopService.Clear();
                    _loopService = null;
                }

                if (_entity != null)
                {
                    UnityEngine.Object.Destroy(_entity);
                }


                _entity = null;
            }

            private static void RegisterLoopServiceIfNeeded()
            {
                if (_loopService != null)
                {
                    return;
                }

                if (!AppServices.HasWorld)
                {
                    return;
                }

                if (AppServices.TryGet(out UtilityLoopService service))
                {
                    _loopService = service;
                    return;
                }

                _loopService = AppServices.RegisterAppSelf(new UtilityLoopService());
            }

            private static UtilityLoopService EnsureLoopService()
            {
                RegisterLoopServiceIfNeeded();
                if (_loopService == null)
                {
                    throw new InvalidOperationException("Utility loop service is not available.");
                }

                return _loopService;
            }

            private class MainBehaviour : MonoBehaviour
            {
                private event UnityAction DestroyEvent;
                private event UnityAction<bool> OnApplicationPauseEvent;
                private event UnityAction OnApplicationQuitEvent;

                private void OnDestroy()
                {
                    if (DestroyEvent != null)
                    {
                        DestroyEvent();
                    }
                }

                private void OnApplicationPause(bool pauseStatus)
                {
                    if (OnApplicationPauseEvent != null)
                    {
                        OnApplicationPauseEvent(pauseStatus);
                    }
                }

                private void OnApplicationQuit()
                {
                    if (OnApplicationQuitEvent != null)
                    {
                        OnApplicationQuitEvent();
                    }
                }

                public void AddDestroyListener(UnityAction fun)
                {
                    DestroyEvent += fun;
                }

                public void RemoveDestroyListener(UnityAction fun)
                {
                    DestroyEvent -= fun;
                }

                public void AddOnApplicationPauseListener(UnityAction<bool> fun)
                {
                    OnApplicationPauseEvent += fun;
                }

                public void RemoveOnApplicationPauseListener(UnityAction<bool> fun)
                {
                    OnApplicationPauseEvent -= fun;
                }

                public void AddOnApplicationQuitListener(UnityAction fun)
                {
                    OnApplicationQuitEvent += fun;
                }

                public void RemoveOnApplicationQuitListener(UnityAction fun)
                {
                    OnApplicationQuitEvent -= fun;
                }


                public void Shutdown()
                {
                    DestroyEvent = null;
                    OnApplicationPauseEvent = null;
                    OnApplicationQuitEvent = null;
                }

                public void Dispose()
                {
                    if (OnApplicationQuitEvent != null)
                    {
                        OnApplicationQuitEvent();
                    }
                    if (DestroyEvent != null)
                    {
                        DestroyEvent();
                    }
                }
            }

            private sealed class UtilityLoopService : ServiceBase, IServiceTickable, IServiceLateTickable, IServiceFixedTickable, IServiceGizmoDrawable
            {
                private event UnityAction UpdateEvent;
                private event UnityAction FixedUpdateEvent;
                private event UnityAction LateUpdateEvent;
                private event UnityAction OnDrawGizmosEvent;

                protected override void OnInitialize()
                {
                }

                protected override void OnDestroyService()
                {
                    Clear();
                }

                void IServiceTickable.Tick(float deltaTime)
                {
                    UpdateEvent?.Invoke();
                }

                void IServiceLateTickable.LateTick(float deltaTime)
                {
                    LateUpdateEvent?.Invoke();
                }

                void IServiceFixedTickable.FixedTick(float fixedDeltaTime)
                {
                    FixedUpdateEvent?.Invoke();
                }

                void IServiceGizmoDrawable.DrawGizmos()
                {
                    OnDrawGizmosEvent?.Invoke();
                }

                public void AddUpdateListener(UnityAction fun)
                {
                    UpdateEvent += fun;
                }

                public void RemoveUpdateListener(UnityAction fun)
                {
                    UpdateEvent -= fun;
                }

                public void AddFixedUpdateListener(UnityAction fun)
                {
                    FixedUpdateEvent += fun;
                }

                public void RemoveFixedUpdateListener(UnityAction fun)
                {
                    FixedUpdateEvent -= fun;
                }

                public void AddLateUpdateListener(UnityAction fun)
                {
                    LateUpdateEvent += fun;
                }

                public void RemoveLateUpdateListener(UnityAction fun)
                {
                    LateUpdateEvent -= fun;
                }

                public void AddOnDrawGizmosListener(UnityAction fun)
                {
                    OnDrawGizmosEvent += fun;
                }

                public void RemoveOnDrawGizmosListener(UnityAction fun)
                {
                    OnDrawGizmosEvent -= fun;
                }

                public void Clear()
                {
                    UpdateEvent = null;
                    FixedUpdateEvent = null;
                    LateUpdateEvent = null;
                    OnDrawGizmosEvent = null;
                }
            }
        }
    }
}
