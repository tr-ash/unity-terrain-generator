using System;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Jobs.LowLevel.Unsafe;
using Intrinsics = Unity.Burst.Intrinsics.Common;

// All datastructures in this namespace will be allocated contiguously in memory.
namespace TerrainGenerator
{
    //[NativeContainerSupportsDeallocateOnJobCompletion]
    [NativeContainer]
    public unsafe struct NativeDAryHeap<T> : IDisposable where T: unmanaged
    {
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct HeapKvPair
        {
            public readonly float key;
            public readonly T value;

            public HeapKvPair(float key, T value)
            {
                this.key = key;
                this.value = value;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HeapData
        {
            [NativeDisableUnsafePtrRestriction] public HeapKvPair* realBuffer;
            [NativeDisableUnsafePtrRestriction] public HeapKvPair* buffer;
            public int numChildren;
            public int Capacity;
            public int Length;
        }

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule] private DisposeSentinel m_DisposeSentinel;
        private static int s_staticSafetyId;

        [BurstDiscard]
        private static void AssignStaticSafetyId(ref AtomicSafetyHandle safetyHandle)
        {
            // static safety IDs are unique per-type, and should only be initialized the first time an instance of
            // the type is created.
            if (s_staticSafetyId == 0)
            {
                s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeDAryHeap<T>>();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref safetyHandle, s_staticSafetyId);
        }
        #endif

        private HeapKvPair* RealBuffer
        {
            get => data->realBuffer;
            set => data->realBuffer = value;
        }

        private HeapKvPair* Buffer
        {
            get => data->buffer;
            set => data->buffer = value;
        }

        private int NumChildren => data->numChildren;

        private int Capacity
        {
            get => data->Capacity;
            set => data->Capacity = value;
        }

        public int Length
        {
            get
            {
                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                #endif
                return data->Length;
            }
            private set => data->Length = value;
        }

        private Allocator m_AllocatorLabel;
        [NativeDisableUnsafePtrRestriction] private readonly HeapData* data;

        private static int DataAlign()
        {
            return JobsUtility.CacheLineSize;
        }

        // allows us to lineup children with cache lines.
        private static int DataStartOffset()
        {
            return ChildrenInCacheLine() * UnsafeUtility.SizeOf<HeapKvPair>();
        }

        private static int ChildrenInCacheLine()
        {
            return math.max(DataAlign() / UnsafeUtility.SizeOf<HeapKvPair>(), 3);
        }

        private static long CapacityInBytes(int elements)
        {
            return (long)UnsafeUtility.SizeOf<HeapKvPair>() * elements;
        }

        private readonly struct ResizedBuffer
        {
            public static ResizedBuffer CreateInstance(HeapKvPair* rBuf, HeapKvPair* buf)
            {
                return new ResizedBuffer(rBuf, buf);
            }

            private ResizedBuffer(HeapKvPair* rBuf, HeapKvPair* buf)
            {
                RealBuffer = rBuf;
                Buffer = buf;
            }

            public readonly HeapKvPair* RealBuffer;
            public readonly HeapKvPair* Buffer;
        }

        private static ResizedBuffer Allocate(int elements, Allocator alloc, int children)
        {
            var totalSize = CapacityInBytes(elements);

            var tempPtr = (HeapKvPair*)UnsafeUtility.Malloc(totalSize + DataStartOffset(), DataAlign(), alloc);
            var fakePtr = &tempPtr[children - 1];
            
            return ResizedBuffer.CreateInstance(tempPtr, fakePtr);
        }

        private void Deallocate()
        {
            UnsafeUtility.Free(RealBuffer, m_AllocatorLabel);
        }

        public NativeDAryHeap(int initialSize = 1, Allocator alloc = Allocator.None)
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (alloc <= Allocator.None)
            {
                throw new ArgumentException("Allocator must be Temp, TempJob, or Persistent", nameof(m_AllocatorLabel));
            }

            if (initialSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSize), "Size must be > 0");
            }

            // The following is sort of dangerous as it allows allocation of this datastructure within jobs, this could lead to memory leaks if dispose is not called, and we have no way of checking that.
            if (!JobsUtility.IsExecutingJob)
            {
                DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, alloc);
                AssignStaticSafetyId(ref m_Safety);
            }
            else
            {
                m_DisposeSentinel = null;
                m_Safety = AtomicSafetyHandle.Create();
            }
            
            #endif

            this.data = (HeapData*)UnsafeUtility.Malloc(sizeof(HeapData), UnsafeUtility.AlignOf<HeapData>(), Allocator.Persistent);

            data->Capacity = initialSize;
            data->Length = 0;
            data->buffer = (HeapKvPair*)IntPtr.Zero;
            data->realBuffer = (HeapKvPair*)IntPtr.Zero;
            data->numChildren = ChildrenInCacheLine();
            this.m_AllocatorLabel = alloc;

            var buffers = Allocate(initialSize, m_AllocatorLabel, NumChildren);

            RealBuffer = buffers.RealBuffer;
            Buffer = buffers.Buffer;
        }

        [WriteAccessRequired]
        public void Dispose()
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!JobsUtility.IsExecutingJob)
            {
                DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
            }
            else
            {
                AtomicSafetyHandle.Release(m_Safety);
            }
            #endif

            Deallocate();
            UnsafeUtility.Free(this.data, Allocator.Persistent);
            Buffer = (HeapKvPair*)IntPtr.Zero;
            Length = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ChildIndex(int elem, [AssumeRange(0, JobsUtility.CacheLineSize)] int child)
        {
            return elem * (NumChildren) + child + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasChild(int elem)
        {
            return elem * (NumChildren) + 1 < Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasParent(int elem)
        {
            return elem > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ParentIndex(int elem)
        {
            return (elem - 1) / NumChildren;
        }

        [SkipLocalsInit]
        private void DownHeapify(int elem)
        {
            var didWork = true;

            while (Hint.Likely(HasChild(elem)) && Hint.Likely(didWork))
            {
                var firstChild = ChildIndex(elem, 0);
                var lastChild = math.min(firstChild + NumChildren, Length);

                var minIndex = firstChild;
                var minValue = Buffer[firstChild].key;

                for (var i = firstChild; i < lastChild; i++)
                {
                    var childKey = Buffer[i].key;

                    if (childKey < minValue)
                    {
                        minIndex = i;
                        minValue = childKey;
                    }
                }

                Hint.Assume(minIndex != elem);

                if (Hint.Likely(HasChild(minIndex)))
                    Intrinsics.Prefetch(Buffer + ChildIndex(minIndex, 0), Intrinsics.ReadWrite.Read);
                
                
                var current = Buffer[elem];
                if (Hint.Likely(didWork = current.key > minValue))
                {
                    Buffer[elem] = Buffer[minIndex];
                    Buffer[minIndex] = current;
                    elem = minIndex;
                }
            }
        }

        [SkipLocalsInit]
        private void UpHeapify(int elem)
        {
            var swap = true;
            while (Hint.Likely(HasParent(elem)) && swap)
            {
                var element = Buffer[elem];
                var parentIdx = ParentIndex(elem);
                var parent = Buffer[parentIdx];

                swap = parent.key > element.key;
                if (Hint.Unlikely(swap))
                {
                    Buffer[parentIdx] = element;
                    Buffer[elem] = parent;
                    elem = parentIdx;
                }
            }
        }

        private void Resize(int size)
        {
            var buffers = Allocate(size, m_AllocatorLabel, NumChildren);
            UnsafeUtility.MemCpy(buffers.Buffer, Buffer, CapacityInBytes(Capacity));

            Deallocate();

            RealBuffer = buffers.RealBuffer;
            Buffer = buffers.Buffer;
            Capacity = size;
        }

        public T PeekMin()
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            if (IsEmpty())
                throw new InvalidOperationException("Can't get minimum value when heap is empty");
            #endif
            return Buffer[0].value;
        }

        public bool IsEmpty()
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            #endif
            return Length == 0;
        }

        public float MinKey()
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            if (IsEmpty())
                throw new InvalidOperationException("Can't get minimum key when heap is empty");
            #endif

            return Buffer[0].key;
        }

        [WriteAccessRequired]
        public void Insert(float key, T value)
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            #endif
            if (Length == Capacity)
                Resize(Capacity * 2);

            Buffer[Length] = new HeapKvPair(key, value);
            UpHeapify(Length);

            Length++;
        }

        [WriteAccessRequired]
        public void DeleteMin()
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (IsEmpty())
                throw new InvalidOperationException("Can't remove minimum element when heap is empty");
            #endif
            Buffer[0] = Buffer[Length - 1];
            Length--;
            DownHeapify(0);
        }
    }
}