using System;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace NativeMessenger
{
    #if !NATIVEMESSENGER_MANUAL_ORDER
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    #endif
    public unsafe partial class NativeEventSystemGroup : ComponentSystemGroup
    {
        private struct EventSystemHandle
        {
            public bool isManaged;
            public int messageHash;
            public bool isSingle;
            public TypeDataHandle typeDataHandle;
            public void* lastPtr;

            public SystemHandle handle;

            // Unmanaged
            public int fieldOffset;
        }

        private struct TypeDataHandle
        {
            public int size;
            public NativeList<byte> dataBuffer;
        }

        private NativeList<EventSystemHandle> _systemHandles;
        private NativeHashMap<int, TypeDataHandle> _dataMap;


        private NativeList<byte> _messenger;

        protected override void OnCreate()
        {
            base.OnCreate();
            _messenger = new(1024, Allocator.Persistent);
            _dataMap = new(10, Allocator.Persistent);

            var messenger = EntityManager.CreateSingleton<Messenger>();
            SystemAPI.SetComponent(messenger, new Messenger
            {
                data = _messenger.AsParallelWriter()
            });
        }

        private TypeDataHandle ReserveDataSlot(int messageHash, Type messageType)
        {
            if (!_dataMap.TryGetValue(messageHash, out var dataBuf))
            {
                dataBuf = new()
                {
                    size = UnsafeUtility.SizeOf(messageType),
                    dataBuffer = new(1, Allocator.Persistent)
                };
                _dataMap[messageHash] = dataBuf;
            }

            return dataBuf;
        }

        protected override void OnDestroy()
        {
            foreach (var pair in _dataMap)
            {
                pair.Value.dataBuffer.Dispose();
            }

            _dataMap.Dispose();
            _systemHandles.Dispose();
            base.OnDestroy();
        }

        private bool _init;

        protected override void OnStartRunning()
        {
            if (_init)
            {
                return;
            }

            _init = true;

            SortSystems();

            var world = World.Unmanaged;
            var updateListLength = m_MasterUpdateList.Length;

            _systemHandles = new(updateListLength, Allocator.Persistent);

            for (var i = 0; i < updateListLength; ++i)
            {
                var index = m_MasterUpdateList[i];

                var handle = !index.IsManaged
                    ? m_UnmanagedSystemsToUpdate[index.Index]
                    : m_managedSystemsToUpdate[index.Index].SystemHandle;


                var systemType = world.GetTypeOfSystem(handle);
                if (typeof(IEventSystem).IsAssignableFrom(systemType))
                {
                    var messageType = GetMessageType(systemType);
                    var messageHash = BurstRuntime.GetHashCode32(messageType);
                    var typeDataHandle = ReserveDataSlot(messageHash, messageType);

                    var fieldOffset = -1;
                    var isSingle = true;

                    if (!index.IsManaged)
                    {
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                        foreach (var field in systemType.GetFields(flags))
                        {
                            var attribute = field.GetCustomAttribute(typeof(MessageAttribute), true);
                            if (attribute is not null)
                            {
                                fieldOffset = UnsafeUtility.GetFieldOffset(field);
                                if (field.FieldType != messageType)
                                {
                                    isSingle = false;
                                }

                                break;
                            }
                        }
                    }
                    else
                    {
                        ((EventSystemBase)m_managedSystemsToUpdate[index.Index]).dataBuffer = typeDataHandle.dataBuffer;
                    }

                    _systemHandles.Add(new()
                    {
                        isManaged = index.IsManaged,
                        messageHash = messageHash,
                        handle = handle,
                        fieldOffset = fieldOffset,
                        isSingle = isSingle,
                        typeDataHandle = typeDataHandle,
                    });
                }
            }
        }

        protected override void OnUpdate()
        {
            var data = _messenger;
            if (data.Length == 0)
            {
                return;
            }

            var ptr = data.GetUnsafeReadOnlyPtr();
            var iterator = 0;
            while (iterator < data.Length)
            {
                var src = ptr + iterator;

                var header = UnsafeUtility.AsRef<MessageHeader>(src);
                var headerSize = sizeof(MessageHeader);

                const int zero = 0;
                const int intSize = sizeof(int);

                var headerFlags = (MessageFlags)header.flags;
                var multiCountSize = headerFlags is MessageFlags.Multi ? intSize : zero;

                var count = headerFlags is MessageFlags.Multi
                    ? UnsafeUtility.AsRef<int>(src + headerSize)
                    : 1;

                var dataPtr = src + headerSize + multiCountSize;

                var handle = _dataMap[header.hash];

                handle.dataBuffer.AddRange(dataPtr, handle.size * count);

                iterator += headerSize + multiCountSize + handle.size * count;
            }

            var world = World.Unmanaged;
            ref var impl = ref world.GetImpl();
            for (var ind = 0; ind < _systemHandles.Length; ind++)
            {
                ref var systemHandle = ref _systemHandles.ElementAt(ind);
                ref var typeHandle = ref systemHandle.typeDataHandle;
                ref var dataBuffer = ref typeHandle.dataBuffer;
                if (dataBuffer.Length == 0)
                {
                    continue;
                }

                var dataPtr = dataBuffer.GetUnsafePtr();
                if (systemHandle.isManaged)
                {
                    ref var state = ref world.ResolveSystemStateRef(systemHandle.handle);
                    var system = state.ManagedSystem;

                    if (systemHandle.lastPtr != dataPtr)
                    {
                        systemHandle.lastPtr = dataPtr;

                        ((EventSystemBase)system).dataBuffer = dataBuffer;
                    }

                    system.Update();
                }
                else
                {
                    if (systemHandle.fieldOffset != -1)
                    {
                        if (systemHandle.isSingle)
                        {
                            ref var state = ref world.ResolveSystemStateRef(systemHandle.handle);
                            var dstPtr = (int*)state.m_SystemPtr + systemHandle.fieldOffset;

                            UnsafeUtility.MemCpy(dstPtr, dataBuffer.GetUnsafeReadOnlyPtr(), typeHandle.size);
                        }
                        else
                        {
                            #if !ENABLE_UNITY_COLLECTIONS_CHECKS
                            if (systemHandle.lastPtr != dataPtr)
                            #endif
                            {
                                ref var state = ref world.ResolveSystemStateRef(systemHandle.handle);
                                var dstPtr = (int*)state.m_SystemPtr + systemHandle.fieldOffset;
                                systemHandle.lastPtr = dataPtr;

                                var roArray = dataBuffer.AsArray()
                                    .GetSubArray(0, dataBuffer.Length / typeHandle.size);
                                *(NativeArray<byte>*)dstPtr = roArray;
                            }
                        }
                    }

                    impl.UpdateSystem(systemHandle.handle);
                }
            }

            data.Clear();
            foreach (var pair in _dataMap)
            {
                pair.Value.dataBuffer.Clear();
            }
        }

        private static Type GetMessageType(Type systemType)
        {
            foreach (var iType in systemType.GetInterfaces())
            {
                if (iType == typeof(IEventSystem))
                {
                    continue;
                }

                if (typeof(IEventSystem).IsAssignableFrom(iType))
                {
                    return iType.GenericTypeArguments[0];
                }
            }

            throw new($"{systemType.Name} must implement IEventSystem<INativeMessage>");
        }
    }
}