using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class World_Generator : Node
{

    [Export]
    private int Seed { get; set; } = 00000;
    private BiomeGenerator BiomeGen { get; set; }

    public List<IGenerationStage> stages;

    public World_Generator(int seed)
    {
        this.Seed = seed;
        this.BiomeGen = new BiomeGenerator(seed);
        stages = new List<IGenerationStage>
        {
            new TerrainStage(this),
            new SurfaceStage(this),
            new CaveStage(this),
            new OreStage(this),
            new FeatureStage(this)
        };
    }

    

    //biomes defined here
    public class BiomeGenerator
    {
        private int Seed;
        

        public BiomeGenerator(int seed)
        {
            this.Seed = seed;
        }
    }




    //layers also defined here (surface, underground forest, etc)
    public Layer[] Layers = new Layer[]
    {
        new Layer("Surface", 0.02f, new Vector2(-20, 20)),
        new Layer("Underground Forest", 0.04f, new Vector2(-300, -10)),
        new Layer("Purple Crystal Area", 0.07f, new Vector2(-600, -310)),
        // new Layer("Deep Caves", 0.1f, new Vector2(-60, -10)),
        // new Layer("Abyss", 0.16f, new Vector2(-100, -30))
    };

    public class Layer
    {
        public string Name;
        public float NoiseScale;
        public Vector2 HeightRange;

        public Layer(string name, float noiseScale, Vector2 heightRange)
        {
            Name = name;
            NoiseScale = noiseScale;
            HeightRange = heightRange;
        }
    }

    public Layer GetLayerForHeight(float height)
    {
        Layer layerNdx1 = null;
        Layer layerNdx2 = null;
        foreach (var layer in Layers)
        {
            if (height >= layer.HeightRange.X && height <= layer.HeightRange.Y)
            {
                if(layerNdx1 == null) layerNdx1 = layer;
                else 
                {
                    layerNdx2 = layer;
                    break;
                }
            }
        }
        if(layerNdx2 == null) return layerNdx1;
        else 
        {
            float midPoint = (layerNdx1.HeightRange.Y + layerNdx2.HeightRange.X) / 2;
            if(height <= midPoint) return layerNdx1;
            else return layerNdx2;
        }
        // return null; // Default layer if none match
    }

    

    //gonna need some stages of world gen yeah (terrain,)
    public byte[] GenerateChunk(Vector3I chunkPos, int chunkSize)
    {
        GenerationContext ctx = new GenerationContext(Seed, chunkPos, chunkSize);
        
        foreach (var stage in stages)
        {
            stage.Generate(ctx);
        }
        
        return ctx.Voxels;
    }

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

    public class TerrainStage : IGenerationStage
    {
        public World_Generator WorldGen;
        public TerrainStage(World_Generator WorldGen)
        {
            this.WorldGen = WorldGen;
        }
        public void Generate(GenerationContext ctx)
        {
            // GD.Print(BiomeGen);
            // Simple terrain generation logic (e.g., flat terrain at y=0)
            for (int y = 0; y < ctx.ChunkSize; y++)
            {
                Layer layer = WorldGen.GetLayerForHeight(ctx.WorldY(y));
                for (int z = 0; z < ctx.ChunkSize; z++)
                {
                    for (int x = 0; x < ctx.ChunkSize; x++)
                    {
                        // int worldY = ctx.WorldY(y);
                        // if (worldY <= 0)
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 1; // Solid block
                        // }
                        // else
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 0; // Air
                        // }
                    }
                }
            }
        }
    }

    public class SurfaceStage : IGenerationStage
    {

        public World_Generator WorldGen;
        public SurfaceStage(World_Generator WorldGen)
        {
            this.WorldGen = WorldGen;
        }
        public void Generate(GenerationContext ctx)
        {
            // Simple terrain generation logic (e.g., flat terrain at y=0)
            for (int x = 0; x < ctx.ChunkSize; x++)
            {
                for (int z = 0; z < ctx.ChunkSize; z++)
                {
                    for (int y = 0; y < ctx.ChunkSize; y++)
                    {
                        // int worldY = ctx.WorldY(y);
                        // if (worldY <= 0)
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 1; // Solid block
                        // }
                        // else
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 0; // Air
                        // }
                    }
                }
            }
        }
    }

    public class CaveStage : IGenerationStage
    {
        public World_Generator WorldGen;
        public CaveStage(World_Generator WorldGen)
        {
            this.WorldGen = WorldGen;
        }
        public void Generate(GenerationContext ctx)
        {
            // Simple terrain generation logic (e.g., flat terrain at y=0)
            for (int x = 0; x < ctx.ChunkSize; x++)
            {
                for (int z = 0; z < ctx.ChunkSize; z++)
                {
                    for (int y = 0; y < ctx.ChunkSize; y++)
                    {
                        // int worldY = ctx.WorldY(y);
                        // if (worldY <= 0)
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 1; // Solid block
                        // }
                        // else
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 0; // Air
                        // }
                    }
                }
            }
        }
    }

    public class OreStage : IGenerationStage
    {
        public World_Generator WorldGen;
        public OreStage(World_Generator WorldGen)
        {
            this.WorldGen = WorldGen;
        }
        public void Generate(GenerationContext ctx)
        {
            // Simple terrain generation logic (e.g., flat terrain at y=0)
            for (int x = 0; x < ctx.ChunkSize; x++)
            {
                for (int z = 0; z < ctx.ChunkSize; z++)
                {
                    for (int y = 0; y < ctx.ChunkSize; y++)
                    {
                        // int worldY = ctx.WorldY(y);
                        // if (worldY <= 0)
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 1; // Solid block
                        // }
                        // else
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 0; // Air
                        // }
                    }
                }
            }
        }
    }

    public class FeatureStage : IGenerationStage
    {
        public World_Generator WorldGen;
        public FeatureStage(World_Generator WorldGen)
        {
            this.WorldGen = WorldGen;
        }
        
        public void Generate(GenerationContext ctx)
        {
            // Simple terrain generation logic (e.g., flat terrain at y=0)
            for (int x = 0; x < ctx.ChunkSize; x++)
            {
                for (int z = 0; z < ctx.ChunkSize; z++)
                {
                    for (int y = 0; y < ctx.ChunkSize; y++)
                    {
                        // int worldY = ctx.WorldY(y);
                        // if (worldY <= 0)
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 1; // Solid block
                        // }
                        // else
                        // {
                        //     ctx.Voxels[ctx.VoxelIndex(x, y, z)] = 0; // Air
                        // }
                    }
                }
            }
        }
    }
}