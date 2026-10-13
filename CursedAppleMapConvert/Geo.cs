using System;
using System.Runtime.InteropServices;

namespace CursedAppleMapConvert
{
    // Plain structs laid out like Unity's Vector3 / Vector2 / Color32: the cache is read straight into Unity arrays.

    [StructLayout(LayoutKind.Sequential)]
    internal struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
        public static V3 operator *(V3 a, float s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public static V3 operator /(V3 a, float s) => new V3(a.X / s, a.Y / s, a.Z / s);
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Normalized { get { float l = Length; return l > 1e-8f ? this / l : new V3(0, 1, 0); } }
        public static V3 Min(V3 a, V3 b) => new V3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static V3 Max(V3 a, V3 b) => new V3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        public static readonly V3 Up = new V3(0, 1, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct V2
    {
        public float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);
        public static V2 operator *(V2 a, float s) => new V2(a.X * s, a.Y * s);
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public float SqrLength => X * X + Y * Y;
        public static float Dot(V2 a, V2 b) => a.X * b.X + a.Y * b.Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct C32
    {
        public byte R, G, B, A;
        public C32(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public static readonly C32 White = new C32(255, 255, 255, 255);
        /// <summary>Component-wise multiply (both as 0-1).</summary>
        public static C32 operator *(C32 a, C32 b) => new C32((byte)((a.R * b.R + 127) / 255), (byte)((a.G * b.G + 127) / 255), (byte)((a.B * b.B + 127) / 255), (byte)((a.A * b.A + 127) / 255));
        public static C32 FromFloats(float r, float g, float b, float a = 1f) => new C32(Byte(r), Byte(g), Byte(b), Byte(a));
        public static byte Byte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
    }

    /// <summary>A 4x4 matrix (row-major M[r,c]), for glTF node transforms.</summary>
    internal struct M4
    {
        public float[] M; // 16, row-major

        public static M4 Identity() { var m = new M4 { M = new float[16] }; m.M[0] = m.M[5] = m.M[10] = m.M[15] = 1; return m; }

        /// <summary>From glTF's column-major array.</summary>
        public static M4 FromColumnMajor(float[] c)
        {
            var m = new M4 { M = new float[16] };
            for (int col = 0; col < 4; col++) for (int row = 0; row < 4; row++) m.M[row * 4 + col] = c[col * 4 + row];
            return m;
        }

        public static M4 Trs(V3 t, float qx, float qy, float qz, float qw, V3 s)
        {
            var m = Identity();
            float xx = qx * qx, yy = qy * qy, zz = qz * qz, xy = qx * qy, xz = qx * qz, yz = qy * qz, wx = qw * qx, wy = qw * qy, wz = qw * qz;
            float[] r = { 1 - 2 * (yy + zz), 2 * (xy - wz), 2 * (xz + wy), 2 * (xy + wz), 1 - 2 * (xx + zz), 2 * (yz - wx), 2 * (xz - wy), 2 * (yz + wx), 1 - 2 * (xx + yy) };
            for (int row = 0; row < 3; row++)
            {
                m.M[row * 4 + 0] = r[row * 3 + 0] * s.X;
                m.M[row * 4 + 1] = r[row * 3 + 1] * s.Y;
                m.M[row * 4 + 2] = r[row * 3 + 2] * s.Z;
            }
            m.M[3] = t.X; m.M[7] = t.Y; m.M[11] = t.Z;
            return m;
        }

        public static M4 operator *(M4 a, M4 b)
        {
            var o = new M4 { M = new float[16] };
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    float s = 0;
                    for (int k = 0; k < 4; k++) s += a.M[r * 4 + k] * b.M[k * 4 + c];
                    o.M[r * 4 + c] = s;
                }
            return o;
        }

        public V3 Point(float x, float y, float z) => new V3(M[0] * x + M[1] * y + M[2] * z + M[3], M[4] * x + M[5] * y + M[6] * z + M[7], M[8] * x + M[9] * y + M[10] * z + M[11]);
        public V3 Dir(float x, float y, float z) => new V3(M[0] * x + M[1] * y + M[2] * z, M[4] * x + M[5] * y + M[6] * z, M[8] * x + M[9] * y + M[10] * z);
    }
}
