using System;

// 4D simplex noise with a seeded permutation table.
// Use Sample(x, y, z, w) — returns approximately [-1, 1].
// Call Reseed(seed) once at startup before any generation threads start.
//
// For seamless wrapping on a torus, pass:
//   x = cos(2π * canonX / PlanetWidth)  * scale
//   y = sin(2π * canonX / PlanetWidth)  * scale
//   z = cos(2π * canonZ / PlanetDepth)  * scale
//   w = sin(2π * canonZ / PlanetDepth)  * scale
//
// scale controls feature size: featureBlocks ≈ PlanetWidth / (2π * scale)
//
// Based on Stefan Gustavson's public domain implementation.
public static class Simplex4D
{
    // Skew / unskew factors for 4D: F4 = (sqrt(5)-1)/4,  G4 = (5-sqrt(5))/20
    private const float F4 = 0.309016994374947f;
    private const float G4 = 0.138196601125011f;

    // 32 gradient vectors: all sign-permutations of (0,±1,±1,±1) and rotations
    private static readonly sbyte[,] Grad4 =
    {
        { 0, 1, 1, 1}, { 0, 1, 1,-1}, { 0, 1,-1, 1}, { 0, 1,-1,-1},
        { 0,-1, 1, 1}, { 0,-1, 1,-1}, { 0,-1,-1, 1}, { 0,-1,-1,-1},
        { 1, 0, 1, 1}, { 1, 0, 1,-1}, { 1, 0,-1, 1}, { 1, 0,-1,-1},
        {-1, 0, 1, 1}, {-1, 0, 1,-1}, {-1, 0,-1, 1}, {-1, 0,-1,-1},
        { 1, 1, 0, 1}, { 1, 1, 0,-1}, { 1,-1, 0, 1}, { 1,-1, 0,-1},
        {-1, 1, 0, 1}, {-1, 1, 0,-1}, {-1,-1, 0, 1}, {-1,-1, 0,-1},
        { 1, 1, 1, 0}, { 1, 1,-1, 0}, { 1,-1, 1, 0}, { 1,-1,-1, 0},
        {-1, 1, 1, 0}, {-1, 1,-1, 0}, {-1,-1, 1, 0}, {-1,-1,-1, 0}
    };

    private static readonly int[] Perm = new int[512];

    static Simplex4D() => Reseed(0);

    public static void Reseed(int seed)
    {
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        var rng = new Random(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) Perm[i] = p[i & 255];
    }

    private static int FastFloor(float x) => x >= 0f ? (int)x : (int)x - 1;

    private static float Dot4(int gi, float x, float y, float z, float w)
        => Grad4[gi, 0] * x + Grad4[gi, 1] * y + Grad4[gi, 2] * z + Grad4[gi, 3] * w;

    private static float Corner(int gi, float x, float y, float z, float w)
    {
        float t = 0.6f - x*x - y*y - z*z - w*w;
        if (t < 0f) return 0f;
        t *= t;
        return t * t * Dot4(gi, x, y, z, w);
    }

    public static float Sample(float x, float y, float z, float w)
    {
        float s = (x + y + z + w) * F4;
        int i = FastFloor(x + s), j = FastFloor(y + s);
        int k = FastFloor(z + s), l = FastFloor(w + s);

        float t  = (i + j + k + l) * G4;
        float x0 = x - (i - t), y0 = y - (j - t);
        float z0 = z - (k - t), w0 = w - (l - t);

        // Rank each offset to find which simplex we're in
        int rx = 0, ry = 0, rz = 0, rw = 0;
        if (x0 > y0) rx++; else ry++;
        if (x0 > z0) rx++; else rz++;
        if (x0 > w0) rx++; else rw++;
        if (y0 > z0) ry++; else rz++;
        if (y0 > w0) ry++; else rw++;
        if (z0 > w0) rz++; else rw++;

        int i1 = rx>=3?1:0, j1 = ry>=3?1:0, k1 = rz>=3?1:0, l1 = rw>=3?1:0;
        int i2 = rx>=2?1:0, j2 = ry>=2?1:0, k2 = rz>=2?1:0, l2 = rw>=2?1:0;
        int i3 = rx>=1?1:0, j3 = ry>=1?1:0, k3 = rz>=1?1:0, l3 = rw>=1?1:0;

        int ii = i&255, jj = j&255, kk = k&255, ll = l&255;

        int gi0 = Perm[ii    + Perm[jj    + Perm[kk    + Perm[ll   ]]]] & 31;
        int gi1 = Perm[ii+i1 + Perm[jj+j1 + Perm[kk+k1 + Perm[ll+l1]]]] & 31;
        int gi2 = Perm[ii+i2 + Perm[jj+j2 + Perm[kk+k2 + Perm[ll+l2]]]] & 31;
        int gi3 = Perm[ii+i3 + Perm[jj+j3 + Perm[kk+k3 + Perm[ll+l3]]]] & 31;
        int gi4 = Perm[ii+1  + Perm[jj+1  + Perm[kk+1  + Perm[ll+1 ]]]] & 31;

        float g4 = G4, g42 = 2f*G4, g43 = 3f*G4, g44 = 4f*G4 - 1f;

        return 27f * (
            Corner(gi0,  x0,              y0,              z0,              w0            ) +
            Corner(gi1,  x0-i1+g4,        y0-j1+g4,        z0-k1+g4,        w0-l1+g4     ) +
            Corner(gi2,  x0-i2+g42,       y0-j2+g42,       z0-k2+g42,       w0-l2+g42    ) +
            Corner(gi3,  x0-i3+g43,       y0-j3+g43,       z0-k3+g43,       w0-l3+g43    ) +
            Corner(gi4,  x0+g44,          y0+g44,          z0+g44,          w0+g44        )
        );
    }
}
