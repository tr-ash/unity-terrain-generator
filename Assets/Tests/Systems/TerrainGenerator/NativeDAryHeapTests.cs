using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using TerrainGenerator;
using Unity.Collections;
using Unity.PerformanceTesting;
using Unity.Burst;
using Unity.Jobs;
using Unity.Profiling;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Tests.NativeDAryHeap
{
    public class NativeDAryHeapTests
    {
        private static object[] _insertElements =
        {
            new[] {1, 2, 3, 4},
            new[] {4, 3, 2, 1},
            new[] {100, 256, 91, 38, 26, 512, 69, 82},
            new[] {1, 1, 1, 2, 2, 2, 3, 3, 4, 5, 5, 5, 5, 6, 7, 8, 9, 9},
            new[] {23, 23, 23, 23, 23, 23, 4, 4, 4, 4, 4, 1, 1, 1},
            new[] {2, 3, 4, 99, 99, 99, 99, 0, 1, -1, -23}
        };

        private static object[] _interleavedPad =
        {
            new object[] {new[] {1, 2, 3, 4}, new[] {4, 3, 2, 1}},
            new object[] {new[] {7, 33, 82, 1, 9, 0, -100, 53}, new[] {4, 3, 2, 1}},
            new object[] {new[] {0, 0, 0, 0, 0, 0, 0}, new[] {99, 99, 99, 99}},
            new object[] {new[] {57, 89, 1, 1, 1, 4, 4, 4, 7, 8, 9, 9, 8, 7}, new[] {1, 0, 643, 111}},
            new object[] {new[] {3}, new[] {2}}
        };

        private NativeDAryHeap<int> heap;

        [SetUp]
        public void Init()
        {
            heap = new NativeDAryHeap<int>(1, Allocator.Persistent);
        }

        [TearDown]
        public void Cleanup()
        {
            heap.Dispose();
        }

        private static bool DoesHeapSortCorrectly(int[] elements, NativeDAryHeap<int> toCheck)
        {
            var sorted = new int[elements.Length];
            Array.Copy(elements, sorted, elements.Length);
            Array.Sort(sorted);

            var count = 0;
            bool isSorted = true;
            while (!toCheck.IsEmpty())
            {
                isSorted = isSorted && sorted[count] == toCheck.PeekMin();
                toCheck.DeleteMin();
                count++;
            }

            isSorted = isSorted && elements.Length == count;

            return isSorted;
        }


        #region InsertDelete

        [Test]
        [TestCaseSource(nameof(_insertElements))]
        public void InsertElementsThenDeleteMinUntilEmpty(int[] elements)
        {
            foreach (var t in elements)
            {
                heap.Insert(t, t);
            }

            Assert.IsTrue(DoesHeapSortCorrectly(elements, heap));
        }

        [Test]
        [TestCaseSource(nameof(_insertElements))]
        public void InsertThenDeleteInterleaved(int[] elements)
        {
            foreach (var t in elements)
            {
                heap.Insert(t, t);
                Assert.AreEqual(t, heap.PeekMin());
                heap.DeleteMin();
            }
        }

        [Test]
        [TestCaseSource(nameof(_interleavedPad))]
        public void InsertThenDeleteInterleavedWithPadding(int[] padding, int[] elements)
        {
            var both = new List<int>();

            foreach (var t in padding)
            {
                heap.Insert(t, t);
                both.Add(t);
            }

            foreach (var t in elements)
            {
                heap.Insert(t, t);
                both.Add(t);
                Assert.AreEqual(both.Min(), heap.PeekMin());
                both.Remove(both.Min());
                heap.DeleteMin();
            }

            for (var i = 0; i < padding.Length; i++)
            {
                Assert.AreEqual(both.Min(), heap.PeekMin());
                both.Remove(both.Min());
                heap.DeleteMin();
            }

            Assert.IsTrue(heap.IsEmpty());
        }

        #endregion

        #region EmptyHeap

        #if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void RemoveFromEmptyHeap()
        {
            Assert.Throws<InvalidOperationException>(() => heap.DeleteMin());
        }

        [Test]
        public void PeekMinOnEmptyHeap()
        {
            Assert.Throws<InvalidOperationException>(() => heap.PeekMin());
        }

        [Test]
        public void MinKeyOnEmptyHeap()
        {
            Assert.Throws<InvalidOperationException>(() => heap.MinKey());
        }
        #endif

        #endregion


    }
}

namespace Benchmarks.NativeDAryHeap
{
    public class NativeDAryHeapPerformance
    {
        #region JobStructs

        [BurstCompile(CompileSynchronously = true)]
        private struct RemoveElementsJob : IJob
        {
            public NativeDAryHeap<int> Heap;

            public void Execute()
            {
                while (!Heap.IsEmpty())
                {
                    Heap.DeleteMin();
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct AddElementsJob : IJob
        {
            public NativeDAryHeap<int> Heap;
            public int Count;

            public void Execute()
            {
                for (var i = 0; i < Count; i++)
                    Heap.Insert(i, i);
            }
        }

        #endregion

        [Test, Performance]
        [TestCase(1024 * 1024)]
        public void BenchmarkRemoveMin(int count)
        {
            var heap = new NativeDAryHeap<int>(1, Allocator.Persistent);
            var job = new RemoveElementsJob {Heap = heap};
            heap.Dispose();

            Measure.Method(() => { job.Run(); })
                .WarmupCount(16)
                .MeasurementCount(32)
                .IterationsPerMeasurement(1)
                .GC()
                .SetUp(() =>
                {
                    heap = new NativeDAryHeap<int>(count, Allocator.Persistent);
                    for (var i = 0; i < count; i++)
                        heap.Insert(i, i);
                    job = new RemoveElementsJob {Heap = heap};
                }).CleanUp(() => { heap.Dispose(); })
                .Run();
        }

        [Test, Performance]
        [TestCase(1024 * 1024)]
        public void BenchmarkInsertion(int count)
        {
            var heap = new NativeDAryHeap<int>(1, Allocator.Persistent);
            heap.Dispose();
            var job = new AddElementsJob {Heap = heap, Count = count};

            Measure.Method(() => { job.Run(); })
                .WarmupCount(16)
                .MeasurementCount(32)
                .IterationsPerMeasurement(1)
                .GC()
                .SetUp(() =>
                {
                    heap = new NativeDAryHeap<int>(count, Allocator.Persistent);
                    job = new AddElementsJob {Heap = heap, Count = count};
                }).CleanUp(() => { heap.Dispose(); })
                .Run();
        }
    }
}