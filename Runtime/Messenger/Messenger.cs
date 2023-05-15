using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine.Scripting;

namespace NativeMessenger
{
    public struct Messenger : IComponentData
    {
        internal NativeList<byte>.ParallelWriter data;

        [Preserve]
        public unsafe void Send<T>(T message) where T : unmanaged, INativeMessage
        {
            var size = sizeof(T);
            var hash = BurstRuntime.GetHashCode32<T>();
            const int hashSize = sizeof(int);

            var length = hashSize + size;
            var idx = Interlocked.Add(ref data.ListData->m_length, length) - length;
            var ptr = (byte*)data.Ptr + idx;
            UnsafeUtility.MemCpy(ptr, UnsafeUtility.AddressOf(ref hash), hashSize);
            UnsafeUtility.MemCpy(ptr + hashSize, UnsafeUtility.AddressOf(ref message), size);
        }
    }
}