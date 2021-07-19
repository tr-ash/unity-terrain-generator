using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace TerrainGenerator
{
    class TerrainGen : MonoBehaviour
    {

        public class HeightfieldView
        {
            public int squareResolution {get; private set;}
            private int maxResolution;
            public int squareCount {get; private set;}
            private int maxSize;
            private float[,] heights;
            private float perlinSeed;

            private float crossoverScale;

            // squareResolution = squareSize;
            // squareCount = squares;
            public HeightfieldView(int currentResolution, int maxResolution, float[,] heights, float perlinSeed, float crossoverScale)
            {
                this.squareResolution = currentResolution;
                this.maxResolution = maxResolution;
                this.maxSize = (maxResolution - 1);
                this.squareCount = maxSize / currentResolution;

                this.heights = heights;
                this.perlinSeed = perlinSeed;
                this.crossoverScale = crossoverScale;
            }

            public float SampleHeight(int x, int y)
            {
                return heights[ConvertIndex(y), ConvertIndex(x)];
            }

            public float DirectSample(int x, int y)
            {
                return heights[y * squareResolution, x * squareResolution];
            }

            public void SetHeight(int x, int y, float height)
            {
                heights[ConvertIndex(y), ConvertIndex(x)] = height;
            }

            public void DirectSet(int x, int y, float height)
            {
                heights[y * squareResolution, x * squareResolution] = height;
            }

            private int ConvertIndex(int n)
            {
                n *= squareResolution;

                if (n < 0)
                {
                    n = -n;
                }
                
                if (n >= maxResolution)
                {
                    int dir = (n / maxSize) % 2;

                    if (n % maxSize == 0)
                    {
                        return maxSize * dir;
                    }
                    else
                    {
                        if (dir == 0) // up
                        {
                            n = n % maxSize;
                        }
                        else // down
                        {
                            n = (maxSize - (n % maxSize)) % maxSize;
                        }
                    }
                }

                return n;
            }

            // Sample height using 1 dimensional bicubic interpolation.
            public float SampleHeightVEdge(int x, int y, int scale = 1)
            {
                return Interpolation.BicubicMidpoint1D(new Vector4(
                    SampleHeight(x * scale, (y - 1) * scale),
                    SampleHeight(x * scale, y * scale),
                    SampleHeight(x * scale, (y + 1) * scale),
                    SampleHeight(x * scale, (y + 2) * scale)
                ));
            }

            public float SampleHeightHEdge(int x, int y, int scale = 1)
            {
                return Interpolation.BicubicMidpoint1D(new Vector4(
                    SampleHeight((x - 1) * scale, y * scale),
                    SampleHeight(x * scale, y * scale),
                    SampleHeight((x + 1) * scale, y * scale),
                    SampleHeight((x + 2) * scale, y * scale)
                ));
            }

            // Sample height using 2 dimensional bicubic interpolation.
            public float SampleHeightMidpoint(int x, int y, int scale = 1)
            {
                Matrix4x4 m = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                {
                    int columnX = (x + i - 1) * scale;

                    m.SetColumn(i, new Vector4(
                        SampleHeight(columnX, (y - 1) * scale),
                        SampleHeight(columnX, (y) * scale),
                        SampleHeight(columnX, (y + 1) * scale),
                        SampleHeight(columnX, (y + 2) * scale)
                    ));
                }

                return Interpolation.BicubicMidpoint2D(m);
            }

            public Vector2 SampleDirectionalGradient(int x, int y, int dx, int dy, Vector2 samplePos, Vector2 dir)
            {
                Matrix4x4 m = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                {
                    int columnX = (x + i - 1) + dx;

                    m.SetColumn(i, new Vector4(
                        SampleHeight(columnX, y - 1 + dy),
                        SampleHeight(columnX, y + dy),
                        SampleHeight(columnX, y + 1 + dy),
                        SampleHeight(columnX, y + 2 + dy)
                    ));
                }

                return Interpolation.BicubicDirectionalGrad(m, dir, samplePos.x, samplePos.y);
            }

            public void Subdivide()
            {
                squareResolution /= 2;
                squareCount = maxSize / squareResolution; // doubles
            }

            public float perlin(float x, float y)
            {   
                
                return (Mathf.PerlinNoise(perlinSeed + x, perlinSeed + y) +
                    1.0f/2.0f * Mathf.PerlinNoise(11506f * perlinSeed + 2f * x, 65131f * perlinSeed + 2f * y) +
                    1.0f/4.0f * Mathf.PerlinNoise(841391f * perlinSeed + 4f * x, 546667f * perlinSeed + 4f * y) +
                    1.0f/8.0f * Mathf.PerlinNoise(97102f * perlinSeed + 8f * x, 31731f * perlinSeed + 8f * y) +
                    1.0f/16.0f * Mathf.PerlinNoise(41231f * perlinSeed + 16f * x, 855539f * perlinSeed + 16f * y) +
                    1.0f/32.0f * Mathf.PerlinNoise(678f * perlinSeed + 32f * x, 916727f * perlinSeed + 32f * y)) / (63.0f/32.0f);
            }

            public bool CrossoverScale(int x, int y)
            {
                float perlinX = ConvertIndex(x) / (float)maxSize;
                float perlinY = ConvertIndex(y) / (float)maxSize;

                perlinX *= this.crossoverScale;
                perlinY *= this.crossoverScale;

                if (squareCount < 2) return false;
                if(squareCount < 4) return perlin(perlinX, perlinY) > 0.65;
                else if(squareCount < 8) return perlin(perlinX, perlinY) > 0.60;
                else if (squareCount < 16) return perlin(perlinX, perlinY) > 0.55;
                else if (squareCount < 32) return perlin(perlinX, perlinY) > 0.50;
                else if (squareCount < 64) return perlin(perlinX, perlinY) > 0.3;
                else if (squareCount < 128) return perlin(perlinX, perlinY) > 0.2;
                else if (squareCount < 256) return perlin(perlinX, perlinY) > 0.1;

                return true;
            }
        }

        [Header("Fractal Settings")]
        [Range(1.0f, 3.0f), Tooltip("Controls the fractal dimension, lower values are closer to noise.")]
        public float beta = 2.0f;

        [Range(0.1f, 2.0f), Tooltip("Controls the roughness of the fractal, higher values are smoother.")]
        public float roughness = 1.0f;

        [Range(0.1f, 3.0f), Tooltip("Controls the crossover scale (scale where fractal behaviour starts).")]
        public float crossoverScale = 1.0f;
        
        [Header("Erosion Settings")]
        public ThermalErosion thermalErosion = new ThermalErosion();

        [Header("Detail Settings")]
        public PerlinFractal detailFractal = new PerlinFractal();

        private float[,] heights;
        private int res;
        private System.Random rand;
        private System.Random deterministicRand;

        private HeightfieldView view;

        private TerrainData terrain;
        public void Start()
        {
            // rand = new System.Random(GetInstanceID() + 2048 * (int)System.DateTimeOffset.Now.ToUnixTimeSeconds());
            rand = new System.Random(256);
            terrain = gameObject.GetComponent<Terrain>().terrainData;

            res = terrain.heightmapResolution;
            
            heights = new float[res, res];

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    heights[y,x] = -1.0f;
                }
            }
            
            view = new HeightfieldView(res - 1, res, heights, (float)rand.NextDouble(), crossoverScale);
            thermalErosion.size = terrain.size;
            thermalErosion.view = view;

            MakeHeightmap();
            
        }

        public void Awake()
        {
            detailFractal.Awake();
        }

        public void OnValidate()
        {
            detailFractal.OnValidate();
        }

        void MakeHeightmap()
        {
            Debug.Log("Started Generating Heightmap");

            float frequency = 1f;
            

            PreSeedGrid();


            while (view.squareResolution > 1)
            {

                Profiler.BeginSample("ApplyDisplacement");
                ApplyDisplacement(frequency);
                Profiler.EndSample();
                Profiler.BeginSample("ApplyPertubation");
                ApplyPertubation(frequency);
                Profiler.EndSample();

                Profiler.BeginSample("SubdivideHeights");
                SubdivideHeights();
                Profiler.EndSample();

                frequency *= 2;
            }

            Profiler.BeginSample("RemoveMinima");
            var minHeight = Mathf.Infinity;
            var minIndex = 0;
            var heightMap = new NativeArray<float>((view.squareCount + 1) * (view.squareCount + 1), Allocator.Persistent);

            for (var x = 0; x <= view.squareCount; x++)
            {
                for (var y = 0; y <= view.squareCount; y++)
                {
                    var h = view.DirectSample(x, y);
                    heightMap[x + y * (view.squareCount + 1)] = h;
                    if (h >= minHeight) continue;
                    minIndex = x + y * (view.squareCount + 1);
                    minHeight = h;
                }
            }

            PriorityFlood.FloodHeightmap(view.squareCount + 1, heightMap, minIndex);

            for (var i = 0; i < (view.squareCount + 1) * (view.squareCount + 1); i++)
            {
                view.DirectSet(i % (view.squareCount + 1), i / (view.squareCount + 1), heightMap[i]);
            }

            heightMap.Dispose();
            Profiler.EndSample();


            Debug.Log("Finished Generating Heights");


            Debug.Log("Started post-processing");

            
             thermalErosion.Erode();


            Debug.Log("Re-adding lost details.");

             for (var x = 0; x <= view.squareCount; x++)
             {
                 for (var y = 0; y <= view.squareCount; y++)
                 {
                     var h = view.DirectSample(x, y);
                     var dx = (float)x / (float)view.squareCount;
                     var dy = (float)y / (float)view.squareCount;

                     view.DirectSet(x, y, Mathf.Clamp(h + detailFractal.Sample(dx, dy), 0f, 1f));
                 }
             }

            terrain.SetHeights(0, 0, heights);

            Debug.Log("Finished Heightmap");
        }

        void SubdivideHeights()
        {
            view.Subdivide();
            // Create midpoints along the top and left edges.
            for (int x = 0; x < view.squareCount / 2; x++)
            {
                view.SetHeight(x * 2 + 1, 0, view.SampleHeightHEdge(x, 0, 2));
            }

            for (int y = 0; y < view.squareCount / 2; y++)
            {
                view.SetHeight(0, y * 2 + 1, view.SampleHeightVEdge(0, y, 2));
            }

            // Create midpoints for each grid.
            for (int x = 0; x < view.squareCount / 2; x++) 
            {
                for (int y = 0; y < view.squareCount / 2; y++)
                {
                    view.SetHeight(x * 2 + 1, y * 2 + 1, view.SampleHeightMidpoint(x, y, 2));
                    view.SetHeight((x + 1) * 2, y * 2 + 1, view.SampleHeightVEdge(x + 1, y, 2));
                    view.SetHeight(x * 2 + 1, (y + 1) * 2, view.SampleHeightHEdge(x, y + 1, 2));
                }
            }
        }

        private void ApplyDisplacement(float frequency)
        {
            float amplitude = 1 / Mathf.Pow(frequency, beta);

            for (int x = 0; x < view.squareCount; x++) 
            {
                for (int y = 0; y < view.squareCount; y++)
                {
                    if (!view.CrossoverScale(x, y))
                    {
                        continue;
                    }

                    Matrix4x4 m = new Matrix4x4();

                    Vector2 displacement = amplitude * new Vector2((float)rand.NextDouble() - 0.5f, (float)rand.NextDouble() - 0.5f);

                    int offsetX = displacement.x < 0 ? -1 : 0;
                    int offsetY = displacement.y < 0 ? -1 : 0;

                    for (int i = 0; i < 4; i++)
                    {
                        int columnX = (x + i - 1 + offsetX);

                        m.SetColumn(i, new Vector4(
                            view.SampleHeight(columnX, y - 1 + offsetY),
                            view.SampleHeight(columnX, y + offsetY),
                            view.SampleHeight(columnX, y + 1 + offsetY),
                            view.SampleHeight(columnX, y + 2 + offsetY)
                        ));
                    }

                    Vector2 samplePos = new Vector2(0, 0);

                    if (displacement.x < 0)
                    {
                        samplePos.x = 1;
                    }

                    if (displacement.y < 0)
                    {
                        samplePos.y = 1;
                    }

                    samplePos += displacement;

                    view.SetHeight(x, y, Interpolation.Bicubic2D(m, samplePos.x, samplePos.y));
                }
            }
        }

        private void ApplyPertubation(float frequency)
        {
            for (int x = 0; x <= view.squareCount; x++) 
            {
                for (int y = 0; y <= view.squareCount; y++)
                {
                    if (!view.CrossoverScale(x, y))
                    {
                        continue;
                    }
                    view.SetHeight(x, y, view.SampleHeight(x, y) + FractalPertubation(frequency));
                }
            }
        }

        private float FractalPertubation(float frequency)
        {
            float amplitude = 1 / Mathf.Pow(frequency, beta);

            return (float)rand.NextDouble() * amplitude;
        }

        public void PreSeedGrid()
        {
            view.SetHeight(0, 0, 0.0f);
            view.SetHeight(0, 1, 0.0f);
            view.SetHeight(1, 0, 0.0f);
            view.SetHeight(1, 1, 0.0f);
        }

        // Apply erosion simulation
        // - Thermal Erosion
        // - Reapply detail with a small scale fractal
        // - Hydraulic Erosion

        [System.Serializable]
        public class ThermalErosion
        {
            [HideInInspector]
            public Vector3 size;
            public HeightfieldView view;

            private float[,] horizontalGradients;

            [Range(0, 256)]
            public int iterations;

            [Range(30, 45)]
            public float talusAngle;

            [Range(0.0f, 0.5f)]
            public float relaxationLevel;

            [Range(0.0f, 1.0f)]
            public float thermalErosionDeposition;

            private float talusGradient;

            public ThermalErosion()
            {

            }

            public void Setup()
            {
                float horizontalStep = (new Vector2(size.x, 0) / (float)view.squareCount).magnitude;
                float verticalStep = (new Vector2(0, size.z) / (float)view.squareCount).magnitude;
                float diagonalStep = (new Vector2(size.x, size.z) / (float)view.squareCount).magnitude;
                talusGradient = Mathf.Tan(Mathf.Deg2Rad * talusAngle);

                horizontalGradients = new float[3, 3]{
                    {diagonalStep,   verticalStep,  diagonalStep},
                    {horizontalStep, 0,             horizontalStep},
                    {diagonalStep,   verticalStep,  diagonalStep}
                };
            }

            private float s(float h, float hi, int x, int y) => -(hi - h) * size.y / horizontalGradients[x + 1, y + 1];

            // first return is the actual sum.
            // second return is the talus sum.
            private (float, float) Sum(int x, int y, float h)
            {
                float sum = 0;
                float talusSum = 0;

                int xStart = x > 0 ? -1 : 0;
                int xEnd = x < view.squareCount ? 1 : 0;
                int yStart = y > 0 ? -1 : 0;
                int yEnd = y < view.squareCount ? 1 : 0;

                for (int dx = xStart; dx <= xEnd; dx++)
                {
                    for (int dy = yStart; dy <= yEnd; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        
                        float hi = view.DirectSample(x + dx, y + dy);
                        

                        if (hi < h)
                        {
                            float grad = s(h, hi, dx, dy);
                            sum += grad;
                            if (grad > talusGradient)
                                talusSum += grad - talusGradient;

                        }
                    }
                }

                return (sum, talusSum);
            }

            private void DistributeTalus(int x, int y, float h, float talusSum, float dhdt)
            {
                // dhdt will be negative, so we must make it positive as we're increasing heights.
                dhdt *= -thermalErosionDeposition;

                int xStart = x > 0 ? -1 : 0;
                int xEnd = x < view.squareCount ? 1 : 0;
                int yStart = y > 0 ? -1 : 0;
                int yEnd = y < view.squareCount ? 1 : 0;

                for (int dx = xStart; dx <= xEnd; dx++)
                {
                    for (int dy = yStart; dy <= yEnd; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        
                        float hi = view.DirectSample(x + dx, y + dy);

                        if (hi < h)
                        {
                            float grad = s(h, hi, dx, dy);

                            // Distribute in proportion to S(pi - p) - T
                            if (grad > talusGradient)
                                view.DirectSet(x + dx, y + dy, hi + (dhdt / (float)size.y) * (grad - talusGradient) / talusSum);

                        }
                    }
                }
            }

            public void Erode()
            {
                Debug.Log("Started thermal erosion.");
                Setup();
                // Thermal Erosion
                // TODO:
                // - Water saturation changing slope angle?
                // - altitude dependent, temperature profile - wind speed.
                // how are erosive processes dependent on altitude?
                for (int it = 0; it < iterations; it++)
                {
                    for (int x = 0; x <= view.squareCount; x++)
                    {
                        for (int y = 0; y <= view.squareCount; y++)
                        {                           
                            float h = view.DirectSample(x, y); 
                            var g = Sum(x, y, h);

                            float sum = g.Item1;
                            float talusSum = g.Item2;
                            
                            float dhdt;
                            if (sum / 2f > talusGradient)
                                dhdt = (talusGradient - sum / 2f) * relaxationLevel;
                            else
                                continue;
                            

                            if (dhdt > 0)
                                continue;
                            
                            // dhdt gives us the change in the height of the landscape.
                            // landscapeHeight = heightmapHeight * size.y
                            // Therefore we need to convert back into landscape space.

                            view.DirectSet(x, y, h + dhdt / (float)size.y);

                            if (thermalErosionDeposition > 0)
                                DistributeTalus(x, y, h, talusSum, dhdt);
                        }
                    }
                }
            }
        }
        [System.Serializable]
        public class HydraulicErosion
        {
            
        }
    }
}

// todo:
// options for adjusting crossover scale fractal + detail fractal
// - ability to edit properties of the perlin fractal being used
// - ability to change subdivision level where fractal behaviour starts


// GPU Thermal Erosion (10s)
// - Only the midpoint in a 3x3 grid of points can be eroded. This stops points from being made higher than the surrounding points.
// We'll have to do this for each midpoint on our texture, then change the midpoint to another point and repeat.
// After every point is eroded we can continue to the next iteration of erosion.

// GPU Terrain Generation (11s)
// - everything but RemoveMinima can probably be done quite easily.