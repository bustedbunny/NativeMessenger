using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace NativeMessenger
{
    public interface IEventSystem<T> : IEventSystem where T : unmanaged, INativeMessage { }

    public interface IEventSystem { }

    [UpdateInGroup(typeof(NativeEventSystemGroup))]
    public abstract partial class EventSystem<T> : EventSystemBase, IEventSystem<T>
        where T : unmanaged, INativeMessage
    {
        protected unsafe T Message => UnsafeUtility.AsRef<T>(dataBuffer.GetUnsafePtr());

        protected unsafe NativeArray<T> Messages
        {
            get
            {
                var buf = dataBuffer.AsArray().GetSubArray(0, dataBuffer.Length / sizeof(T));
                return UnsafeUtility.As<NativeArray<byte>, NativeArray<T>>(ref buf);
            }
        }
    }

    public abstract partial class EventSystemBase : SystemBase
    {
        internal NativeList<byte> dataBuffer;
    }
}