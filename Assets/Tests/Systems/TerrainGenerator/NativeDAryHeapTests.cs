using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Jobs;

using TerrainGenerator;
using Unity.Collections;
using UnityEngine.PlayerLoop;

public class NativeDAryHeapTests
{
    private static object[] _insertElements =
    {
        new int[] {1, 2, 3, 4},
        new int[] {4, 3, 2, 1},
        new int[] {100, 256, 91, 38, 26, 512, 69, 82},
        new int[] {1, 1, 1, 2, 2, 2, 3, 3, 4, 5, 5, 5, 5, 6, 7, 8, 9, 9},
        new int[] {23, 23, 23, 23, 23, 23, 4, 4, 4, 4, 4, 1, 1, 1},
        new int[] {2, 3, 4, 99, 99, 99, 99, 0, 1, -1, -23}
    };

    private static object[] _interleavedPad =
    {
        new object[] {new int[] {1, 2, 3, 4}, new int[] {4, 3, 2, 1}},
        new object[] {new int[] {7, 33, 82, 1, 9, 0, -100, 53}, new int[] {4, 3, 2, 1}},
        new object[] {new int[] {0, 0, 0, 0, 0, 0, 0}, new int[] {99, 99, 99, 99}},
        new object[] {new int[] {57, 89, 1, 1, 1, 4, 4, 4, 7, 8, 9, 9, 8, 7}, new int[] {1, 0, 643, 111}},
        new object[] {new int[] {3}, new int[] {2}}
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

        var isSorted = true;
        var count = 0;
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