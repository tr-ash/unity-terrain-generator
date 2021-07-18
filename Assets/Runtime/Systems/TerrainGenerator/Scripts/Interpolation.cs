using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainGenerator {
    public static class Interpolation
    {
        private static Matrix4x4 _catmullRomMatrix = new Matrix4x4(
            new Vector4(0, -1, 2, -1),
            new Vector4(2, 0, -5, 3),
            new Vector4(0, 1, 4, -3),
            new Vector4(0, 0, -1, 1)
        );

        private static readonly Vector4 MidpointCoefficients = GetCoefficients(0.5f);

        public static float BicubicMidpoint1D(Vector4 v)
        {
            return (1.0f/2.0f) * Vector4.Dot(MidpointCoefficients, v);
        }

        public static float BicubicMidpoint2D(Matrix4x4 m)
        {
            var v = m * MidpointCoefficients;
            return (1.0f/4.0f) * Vector4.Dot(MidpointCoefficients, v);
        }

        public static float Bicubic1D(Vector4 points, float t)
        {
            return (1.0f/2.0f) * Vector4.Dot(GetCoefficients(t), points);
        }

        private static Vector4 GetCoefficients(float t)
        {
            var v = new Vector4(1, t, t * t, t * t * t);

            var coefficients = new Vector4(
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(0)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(1)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(2)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(3))
            );

            return coefficients;
        }

        private static Vector4 GradCoeffs(Vector4 v, float t)
        {
            var coefficients = new Vector4(
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(0)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(1)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(2)),
                Vector4.Dot(v, _catmullRomMatrix.GetColumn(3))
            );

            return coefficients;
        }

        public static float Bicubic2D(Matrix4x4 points, float x, float y)
        {
            var xCoefficients = GetCoefficients(x);
            var yCoefficients = GetCoefficients(y);

            return (1.0f/4.0f) * Vector4.Dot(points * xCoefficients, yCoefficients);
        }

        public static Vector2 BicubicDirectionalGrad(Matrix4x4 points, Vector2 dir, float x, float y)
        {
            var vx = new Vector4(0, 1, 2 * x, 3 * x * x);
            var pvx = new Vector4(1, x, x * x, x * x * x);

            var vy = new Vector4(0, 1, 2 * y, 3 * y * y);
            var pvy = new Vector4(1, y, y * y, y * y * y);

            // fx(x, y)
            var fx = (1f/4f) * Vector4.Dot(points * GradCoeffs(vx, x), GradCoeffs(pvy, y));

            // fy(x, y)
            var fy = (1f/4f) * Vector4.Dot(points * GradCoeffs(pvx, x), GradCoeffs(vy, x));

            return dir * new Vector2(fx, fy);
        }
    }
}