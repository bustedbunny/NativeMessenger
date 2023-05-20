using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace NativeMessenger
{
    public struct Messenger : IComponentData
    {
        internal NativeList<byte>.ParallelWriter data;

        public unsafe void Send<T>(T message) where T : unmanaged, INativeMessage
        {
            var size = sizeof(T);
            var headerSize = sizeof(MessageHeader);
            var header = new MessageHeader { hash = BurstRuntime.GetHashCode32<T>() };

            var length = headerSize + size;
            var idx = Interlocked.Add(ref data.ListData->m_length, length) - length;
            var ptr = (byte*)data.Ptr + idx;
            UnsafeUtility.MemCpy(ptr, &header, headerSize);
            UnsafeUtility.MemCpy(ptr + headerSize, &message, size);
        }

        public unsafe void SendRange<T>(NativeArray<T> messages) where T : unmanaged, INativeMessage
        {
            SendRange((T*)messages.GetUnsafePtr(), messages.Length);
        }

        public unsafe void SendRange<T>(NativeList<T> messages) where T : unmanaged, INativeMessage
        {
            SendRange(messages.GetUnsafePtr(), messages.Length);
        }

        public unsafe void SendRange<T>(T* messagePtr, int count) where T : unmanaged, INativeMessage
        {
            var size = sizeof(T) * count;
            var headerSize = sizeof(MessageHeader);
            var header = new MessageHeader
            {
                flags = (byte)MessageFlags.Multi,
                hash = BurstRuntime.GetHashCode32<T>()
            };

            const int countSize = sizeof(int);

            var totalLength = headerSize + countSize + size;
            var idx = Interlocked.Add(ref data.ListData->m_length, totalLength) - totalLength;
            var ptr = (byte*)data.Ptr + idx;
            UnsafeUtility.MemCpy(ptr, &header, headerSize);
            UnsafeUtility.MemCpy(ptr + headerSize, &count, headerSize);
            UnsafeUtility.MemCpy(ptr + headerSize + countSize, messagePtr, size);
        }
    }
}