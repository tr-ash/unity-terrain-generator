using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace TerrainGenerator
{
    internal static class PriorityFlood
    {
        private static void FloodHeightmap(int sideLength, NativeArray<float> heightmap, int indexOfMin)
        {
            var guessSize = (int) math.max((sideLength * sideLength * 0.3), 1);

            var processed = new NativeArray<bool>(heightmap.Length, Allocator.Persistent);
            var spillElevations = new NativeArray<float>(heightmap.Length, Allocator.Persistent);
            var pq = new NativeDAryHeap<int>(guessSize, Allocator.Persistent);
            var sq = new NativeQueue<int>(Allocator.Persistent);
            var psq = new NativeQueue<int>(Allocator.Persistent);

            var job = new PriorityFloodJob
            {
                HeightMap = heightmap,
                Processed = processed,
                SpillElevations = spillElevations,
                Pq = pq,
                Sq = sq,
                Psq = psq,
                SeedCell = indexOfMin,
                SideLength = sideLength
            };

            job.Run();

            // The heightmap will be updated now such that all depressions have been filled in.

            processed.Dispose();
            spillElevations.Dispose();
            pq.Dispose();
            sq.Dispose();
            psq.Dispose();
        }

        #region Jobs
        [BurstCompile(CompileSynchronously = true)]
        private struct PriorityFloodJob : IJob
        {
            public NativeArray<float> HeightMap;
            public NativeArray<bool> Processed;
            public NativeArray<float> SpillElevations;
            public NativeDAryHeap<int> Pq; // Priority queue.
            public NativeQueue<int> Sq; // Standard queue.
            public NativeQueue<int> Psq; // Spill cell standard queue.

            public int SideLength;
            public int SeedCell;

            // The value of each index indicates whether the node it corresponds to has a spill path that is lower than c.
            //
            private SMatrix mat;

            public void Execute()
            {
                // Initialization:
                mat = new SMatrix();

                for (var i = 0; i < Processed.Length; i++)
                {
                    Processed[i] = false;
                }
                InitializeQueue();
                Processed[SeedCell] = true;
                SpillElevations[SeedCell] = HeightMap[SeedCell];

                while (!Pq.IsEmpty())
                {
                    var cell = Pq.PeekMin();
                    Pq.DeleteMin();
                    var cellSpill = SpillEl(cell);

                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell + j + verticalOffset;

                            if (IsProcessed(n) || IndexOutOfBounds(n))
                                continue;

                            var neighbourSpill = HeightMap[n];

                            if (neighbourSpill <= cellSpill)
                            {
                                HeightMap[n] = cellSpill;
                                Sq.Enqueue(n);
                                ProcessSq();
                            }
                            else
                            {
                                Processed[n] = true;
                                SpillElevations[n] = neighbourSpill;
                                Psq.Enqueue(n);
                            }
                            ProcessPsq();
                        }
                    }
                }
            }

            private void ProcessSq()
            {
                while (!Sq.IsEmpty())
                {
                    var cell = Sq.Dequeue();
                    var cellSpill = SpillEl(cell);

                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell + j + verticalOffset;

                            if (IsProcessed(n) || IndexOutOfBounds(n))
                                continue;

                            var neighbourSpill = HeightMap[n];

                            if (cellSpill < neighbourSpill)
                            {
                                Processed[n] = true;
                                SpillElevations[n] = neighbourSpill;
                                Psq.Enqueue(n);
                            }
                            else
                            {
                                Processed[n] = true;
                                HeightMap[n] = cellSpill;
                                Sq.Enqueue(n);
                            }

                        }
                    }
                }
            }

            private void ProcessPsq()
            {
                while (!Psq.IsEmpty())
                {
                    var cell = Psq.Dequeue();
                    var cellSpill = SpillEl(cell);

                    // Clear S matrix.
                    for (var i = 0; i < 5; i++)
                    for (var j = 0; j < 5; j++)
                    {
                        mat[i][j] = false;
                    }

                    var shouldExit = false;
                    for (var i = -1; i <= 1; i++)
                    {
                        var verticalOffset = i * SideLength;

                        for (var j = -1; j <= 1; j++)
                        {
                            if (i == 0 && j == 0)
                                continue;

                            var n = cell + j + verticalOffset;

                            if (IsProcessed(n) || IndexOutOfBounds(n))
                                continue;

                            var neighbourSpill = HeightMap[n];

                            if (neighbourSpill > cellSpill)
                            {
                                SpillElevations[n] = neighbourSpill;
                                Processed[n] = true;
                                Psq.Enqueue(n);
                            }
                            else
                            {
                                if (CanSpill(cell, i, j)) continue;
                                Pq.Insert(SpillEl(cell), cell);
                                shouldExit = true;
                                break;
                            }
                        }
                        if (shouldExit)
                            break;
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IndexOutOfBounds(int n) => n >= HeightMap.Length || n < 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private float SpillEl(int n) => SpillElevations[n];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsProcessed(int n) => Processed[n];


            ///<summary>
            /// This method determines whether the neighbour of focus (j + i * SideLength) has a spill path or a spill outlet if it is a depression cell.
            /// <para>
            /// If all neighbours of the focus have a spill path or spill outlet, then there is no reason to move focus to the priority queue.
            /// </para>
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool CanSpill(int focus, int i, int j)
            {
                for (var u = -1; u <= 1; u++)
                {
                    var y = i + u;
                    var verticalOffset = y * SideLength;

                    for (var v = -1; v <= 1; v++)
                    {
                        var x = j + v;
                        var n = focus + x + verticalOffset;

                        // Skip the neighbour cell we're branching out of and ensure we don't go out of bounds.
                        if (u == 0 && v == 0 || IndexOutOfBounds(n))
                            continue;

                        if (mat[y][x] || IsProcessed(n) && HeightMap[n] < SpillEl(focus))
                        {
                            mat[y][x] = true;
                            return true;
                        }
                    }
                }

                return false;
            }

            ///<summary> Initialize Queue with Seed Cells.
            /// <para>
            /// Seed cells won't have their heights altered by Priority-Flood.
            /// If we set the global minimum as a seed cell, all slopes will flow towards it.
            /// </para>
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void InitializeQueue()
            {
                Sq.Enqueue(SeedCell);
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
        }
        #endregion
    }
}