using UnityEngine;

namespace AlicizaX.EventTests
{
    public sealed class EventOwner : MonoBehaviour
    {
        public bool Payload;
        public bool DisposeOnDisable;
        public bool DisposeOnDestroy;
        public bool SubscribeOnEnable;
        public bool ReadDestroyedObject;
        public bool ReadUi;
        public Transform Ui;
        public int Calls;
        public int Value;
        public EventRuntimeHandle Handle;
        public void Bind() => Handle = Payload ? EventBus.Subscribe<PayloadProbe>(OnPayload) : EventBus.Subscribe<EmptyProbe>(OnEmpty);
        private void OnEnable() { if (SubscribeOnEnable) Bind(); }
        private void OnDisable() { if (DisposeOnDisable) Handle.Dispose(); }
        private void OnDestroy() { if (DisposeOnDestroy) Handle.Dispose(); }
        private void OnEmpty() { Calls++; Read(); }
        private void OnPayload(in PayloadProbe e) { Calls++; Value += e.Value; Read(); }
        private void Read()
        {
            if (ReadUi) Value += (int)Ui.position.x;
            if (ReadDestroyedObject) Value += (int)transform.position.x;
        }
    }
}
