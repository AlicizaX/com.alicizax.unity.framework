using UnityEngine;

namespace AlicizaX
{
    /// <summary>
    /// 标记物体对象为不可销毁
    /// </summary>
    public sealed class ObjectDontDestroyOnLoad : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(this);
        }
    }
}
