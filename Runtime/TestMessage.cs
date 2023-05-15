using Unity;
using Unity.Collections;
using Unity.Entities;

namespace NativeMessenger
{
    public struct TestMessage : INativeMessage
    {
        public float kek;
    }

    [UpdateInGroup(typeof(NativeEventSystemGroup))]
    public partial struct TestSystem : ISystem, IEventSystem<TestMessage>
    {
        [Message] private NativeArray<TestMessage> _message;

        public void OnUpdate(ref SystemState state)
        {
            foreach (var testMessage in _message)
            {
                Debug.Log(testMessage.kek);
            }
        }
    }

    public partial class TestClassSystem : EventSystem<TestMessage>
    {
        protected override void OnUpdate()
        {
            foreach (var testMessage in Messages)
            {
                Debug.Log(testMessage.kek);
            }
        }
    }

    public partial struct SendTestSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var messenger = SystemAPI.GetSingleton<Messenger>();
            messenger.Send(new TestMessage { kek = 4.20f });
            messenger.Send(new TestMessage() { kek = 6.9f });
        }
    }
}