using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;

namespace TerrainGenerator
{
    namespace Jobs
    {
        
        [BurstCompile(CompileSynchronously = true)]
        internal struct PriorityFloodJob : IJob
        {
            public void Execute()
            {
            }
        }
    }

    internal static class PriorityFlood
    {
        private static void FloodHeightmap(int sideLength, float[] heightmap)
        {
           
        }
    }
}
