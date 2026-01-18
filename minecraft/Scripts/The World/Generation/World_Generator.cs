using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class World_Generator : Node
{

    [Export]
    private int Seed { get; set; } = 00000;
    private Variant BiomeGen { get; set; }

    // private 

    public World_Generator(int seed)
    {
        this.Seed = seed;
        // this.BiomeGen = new BiomeGenerator(seed);
    }

    

    //biomes defined here

    //layers also defined here (surface, underground forest, etc)

    //gonna need some stages of world gen yeah (terrain,)
    public class GenerationContext
    {
        public int Seed;
        public Vector3I ChunkPos;
        public byte[] Voxels;
        public int ChunkSize;
        
        public GenerationContext(int seed, Vector3I chunkPos, int chunkSize)
        {
            Seed = seed;
            ChunkPos = chunkPos;
            ChunkSize = chunkSize;
            Voxels = new byte[chunkSize * chunkSize * chunkSize];
        }
        
        public int WorldX(int localX) => ChunkPos.X * ChunkSize + localX;
        public int WorldY(int localY) => ChunkPos.Y * ChunkSize + localY;
        public int WorldZ(int localZ) => ChunkPos.Z * ChunkSize + localZ;
        
        public int VoxelIndex(int x, int y, int z)
        {
            return x + (z * ChunkSize) + (y * ChunkSize * ChunkSize);
        }
    }

    public interface IGenerationStage
    {
        void Generate(GenerationContext ctx);
    }
}