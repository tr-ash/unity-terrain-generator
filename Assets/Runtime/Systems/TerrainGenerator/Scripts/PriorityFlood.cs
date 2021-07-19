using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGenerator
{
    public static class PriorityFlood
    {
        public static void FloodHeightmap(int sideLength, NativeArray<float> heightmap, int indexOfMin)
        {
            var guessSize = (int) math.max((sideLength * sideLength * 0.3), 1);

            var processed = new NativeArray<bool>(heightmap.Length, Allocator.Persistent);
            var pq = new NativeDAryHeap<int>(guessSize, Allocator.Persistent);
            var sq = new NativeQueue<SpillNode>(Allocator.Persistent);
            var psq = new NativeQueue<SpillNode>(Allocator.Persistent);
            var potential = new NativeQueue<SpillNode>(Allocator.Persistent);

            var job = new PriorityFloodJob
            {
                HeightMap = heightmap,
                Processed = processed,
                Pq = pq,
                Sq = sq,
                Psq = psq,
                Potential = potential,
                SeedCell = indexOfMin,
                SideLength = sideLength
            };

            job.Run();

            // The heightmap will be updated now such that all depressions have been filled in.

            processed.Dispose();
            pq.Dispose();
            sq.Dispose();
            psq.Dispose();
            potential.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SpillNode
        {
            public static SpillNode CreateInstance(int id, float spillHeight)
            {
                return new SpillNode(id, spillHeight);
            }

            // [MarshalAs(UnmanagedType.I4)]
            public readonly int id;

            // [MarshalAs(UnmanagedType.R4)]
            public readonly float spillHeight;

            private SpillNode(int id, float spillHeight)
            {
                this.id = id;
                this.spillHeight = spillHeight;
            }
        }

        #region Jobs
        [BurstCompile(CompileSynchronously = true)]
        private struct PriorityFloodJob : IJob
        {
            public NativeArray<float> HeightMap;
            public NativeArray<bool> Processed;

            // Priority queue containing seed cells and potential spill cells.
            public NativeDAryHeap<int> Pq;

            // Standard queue for tracing depression cells.
            public NativeQueue<SpillNode> Sq;

            // Standard queue for tracing slope cells.
            public NativeQueue<SpillNode> Psq;

            // Queue for trying to discard potential spill cells.
            public NativeQueue<SpillNode> Potential;

            public int SideLength;
            public int SeedCell;

            // The value of each index indicates whether the node it corresponds to has a spill path that is lower than c.
            private SMatrix mat;

            public void Execute()
            {
                // Initialization:
                mat = new SMatrix();

                for (var i = 0; i < Processed.Length; i++)
                {
                    Processed[i] = false;
                }

                Processed[SeedCell] = true;
                Pq.Insert(HeightMap[SeedCell], SeedCell);



                while (!Pq.IsEmpty())
                {
                    var cell = Pq.PeekMin();
                    var cellSpill = Pq.MinKey();
                    // Debug.Log(String.Format("PQ Cell: {0}, {1}", Pq.MinKey(), Pq.PeekMin()));

                    Pq.DeleteMin();

                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            // Avoid accidentally doing more work on the focal node.
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell + j + verticalOffset;

                            if (IsProcessed(n))
                                continue;

                            var nSpillEl = HeightMap[n];

                            if (nSpillEl <= cellSpill)
                            {
                                AddDepressionCell(n, cellSpill);
                                FillDepression();
                            }
                            else
                            {
                                AddSlopeCell(n, nSpillEl);
                            }
                            TraceSlope();
                        }
                    }
                }
            }

            ///<summary> Processes a depression by performing a region growing operation on it with the depression cells that are currently in the queue. </summary>
            private void FillDepression()
            {
                while (!Sq.IsEmpty())
                {
                    var cell = Sq.Dequeue();

                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell.id + j + verticalOffset;

                            if (IsProcessed(n))
                                continue;

                            var neighbourSpill = HeightMap[n];

                            if (neighbourSpill > cell.spillHeight)
                            {
                                AddSlopeCell(n, neighbourSpill);
                            }
                            else
                            {
                                AddDepressionCell(n, cell.spillHeight);
                            }

                        }
                    }
                }
            }

            ///<summary> Processes a slope by tracing the slope cells that are currently in the queue. </summary>
            private void TraceSlope()
            {
                while (!Psq.IsEmpty())
                {
                    var cell = Psq.Dequeue();

                    // Clear S matrix.
                    mat.MakeFalse();

                    var shouldExit = false;
                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell.id + j + verticalOffset;

                            if (IsProcessed(n))
                                continue;

                            var neighbourSpill = HeightMap[n];

                            if (neighbourSpill > cell.spillHeight)
                            {
                                AddSlopeCell(n, neighbourSpill);
                            }
                            else
                            {
                                if (CanSpill(cell, i, j)) continue;
                                AddPotentialSpillCell((i + 1) * 3 + j + 1, cell);
                                shouldExit = true;
                                break;
                            }
                        }
                        if (shouldExit)
                            break;
                    }
                }

                TryToDiscardSpillCells();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void AddSlopeCell(int id, float spillHeight)
            {
                Processed[id] = true;
                Psq.Enqueue(SpillNode.CreateInstance(id, spillHeight));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void AddDepressionCell(int id, float spillHeight)
            {
                Processed[id] = true;
                HeightMap[id] = spillHeight;
                Sq.Enqueue(SpillNode.CreateInstance(id, spillHeight));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void AddPotentialSpillCell(int iteration, SpillNode cell)
            {
                const int cutoff = 2;

                if (iteration <= cutoff)
                    Potential.Enqueue(cell);
                else
                    Pq.Insert(cell.spillHeight, cell.id);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void TryToDiscardSpillCells()
            {
                while (!Potential.IsEmpty())
                {
                    var cell = Potential.Dequeue();

                    var shouldExit = false;
                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell.id + j + verticalOffset;

                            if (IsProcessed(n))
                                continue;

                            Pq.Insert(cell.spillHeight, cell.id);
                            shouldExit = true;
                            break;
                        }

                        if (shouldExit)
                            break;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IndexOutOfBounds(int n) => n >= HeightMap.Length || n < 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsProcessed(int n) => IndexOutOfBounds(n) || Processed[n];

            ///<summary>
            /// This method determines whether the neighbour of focus (j + i * SideLength) has a spill path or a spill outlet if it is a depression cell.
            /// <para>
            /// If all neighbours of the focus have a spill path or spill outlet, then there is no reason to move the focal element to the priority queue.
            /// </para>
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool CanSpill(SpillNode focus, int i, int j)
            {
                for (var u = -1; u <= 1; u++)
                {
                    var y = i + u;
                    var verticalOffset = y * SideLength;

                    for (var v = -1; v <= 1; v++)
                    {
                        var x = j + v;
                        var n = focus.id + x + verticalOffset;

                        // Skip the neighbour cell we're branching out of and ensure we don't go out of bounds.
                        if (u == 0 && v == 0 || IndexOutOfBounds(n))
                            continue;

                        if (mat[y + 2][x + 2] || IsProcessed(n) && HeightMap[n] < focus.spillHeight)
                        {
                            mat[y + 2][x + 2] = true;
                            return true;
                        }
                    }
                }

                return false;
            }
        }
        #endregion

        #region Matrix
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        private struct SRow
        {
            [MarshalAs(UnmanagedType.U1)] public bool a;
            [MarshalAs(UnmanagedType.U1)] public bool b;
            [MarshalAs(UnmanagedType.U1)] public bool c;
            [MarshalAs(UnmanagedType.U1)] public bool d;
            [MarshalAs(UnmanagedType.U1)] public bool e;

            public unsafe bool this[int index]
            {
                get
                {
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if ((uint)index >= 5)
                        throw new System.ArgumentException("index must be between[0...4]");
                    #endif
                    fixed (SRow* array = &this) { return ((bool*)array)[index]; }
                }
                set
                {
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if ((uint)index >= 5)
                        throw new System.ArgumentException("index must be between[0...4]");
                    #endif
                    fixed (bool* array = &a) { array[index] = value; }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void MakeFalse() => a = b = c = d = e = false;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        private struct SMatrix
        {
            public SRow c0;
            public SRow c1;
            public SRow c2;
            public SRow c3;
            public SRow c4;

            public unsafe ref SRow this[int index]
            {
                get
                {
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if ((uint)index >= 5)
                        throw new System.ArgumentException("index must be between[0...4]");
                    #endif
                    fixed (SMatrix* array = &this) { return ref ((SRow*)array)[index]; }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void MakeFalse()
            {
                c0.MakeFalse();
                c1.MakeFalse();
                c2.MakeFalse();
                c3.MakeFalse();
                c4.MakeFalse();
            }
        }
        #endregion
    }
}