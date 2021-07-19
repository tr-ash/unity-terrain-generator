using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Unity.Collections;
using TerrainGenerator;
using Unity.PerformanceTesting;
using UnityEngine;

public class PriorityFloodTests
{
    [Test, Performance]
    public void FloodAHeightMap()
    {
        const int sideLength = 1024;
        var heightMap = new NativeArray<float>(sideLength * sideLength, Allocator.Persistent);

        var fractal = new PerlinFractal {octaves = 8, maxDisplacement = 1};
        fractal.Setup();

        var minIndex = 0;
        for (var i = 0; i < sideLength * sideLength; i++)
        {
            heightMap[i] = fractal.Sample(i % sideLength / (float) sideLength, i / sideLength / (float) sideLength);

            if (heightMap[i] < heightMap[minIndex])
                minIndex = i;
        }

        Debug.Log(String.Format("{0}, {1}", heightMap[minIndex], minIndex));

        var testMap = new NativeArray<float>(sideLength * sideLength, Allocator.Persistent);
        // heightMap.CopyTo(testMap);
        // PriorityFlood.FloodHeightmap(sideLength, testMap, minIndex);
        testMap.Dispose();

        Measure.Method(() => { PriorityFlood.FloodHeightmap(sideLength, testMap, minIndex); })
            .WarmupCount(16)
            .MeasurementCount(32)
            .IterationsPerMeasurement(1)
            .GC()
            .SetUp(() =>
            {
                testMap = new NativeArray<float>(sideLength * sideLength, Allocator.Persistent);
                heightMap.CopyTo(testMap);
            }).CleanUp(() => { testMap.Dispose(); })
            .Run();

        heightMap.Dispose();
    }
}