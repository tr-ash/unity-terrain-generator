using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainGenerator {

    [System.Serializable]
    public class PerlinFractal
    {
        [Range(1, 16)]
        public int octaves;

        [Range(1.0f, 8.0f)]
        public float lacunarity = 2.0f;

        [Range(0.0f, 1.0f)]
        public float persistence = 0.5f;
        [Range(1, 64)]
        public float scale = 1;

        [Range(0, 1)]
        public float maxDisplacement = 1;

        public int seed = 0;

        private System.Random prng;

        public Texture2D fractalTexture;

        private Vector2[] offsets;

        


        void Setup()
        {
            offsets = new Vector2[octaves];
            prng = new System.Random(seed);

            for (int octave = 0; octave < octaves; octave++)
            {
                offsets[octave] = new Vector2((float)prng.NextDouble() * 2000f - 1000f, (float)prng.NextDouble() * 2000f - 1000f);
            }
        }

        public void Awake()
        {
            Setup();

            fractalTexture = new Texture2D(256, 256);
            for (int x = 0; x < fractalTexture.width; x++)
            {
                for (int y = 0; y < fractalTexture.height; y++)
                {
                    float value = this.Sample(x / (float)fractalTexture.width, y / (float)fractalTexture.height) / maxDisplacement;
                    fractalTexture.SetPixel(x, y, new Color(value, value, value, 1.0f));
                }
            }

            fractalTexture.Apply();
        }

        public PerlinFractal()
        {
            Setup();
        }

        public PerlinFractal(int seed)
        {
            this.seed = seed;
            Setup();
        }

        public void OnValidate()
        {
            Awake();
        }

        public PerlinFractal(int seed, float lacunarity, float persistence)
        {
            this.seed = seed;
            this.lacunarity = lacunarity;
            this.persistence = persistence;
            Setup();
        }

        public float Sample(float x, float y)
        {
            float v = 0;

            float amplitude = scale;
            float frequency = scale;

            for (int octave = 0; octave < octaves; octave++)
            {

                v += amplitude * Mathf.PerlinNoise(offsets[octave].x + frequency * x, offsets[octave].y + frequency * y);


                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return Mathf.Clamp(v / ((1 - Mathf.Pow(persistence, octaves)) / (1 - persistence)), 0.0f, 1.0f) * maxDisplacement;
        }
    }
}