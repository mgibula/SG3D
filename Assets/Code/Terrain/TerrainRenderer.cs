﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace SG3D {

public struct TerrainTypeMaterialInfo {
    public TerrainType type;
    public float smoothness;
    public float shaderIndex;
};

// This class is responsible for displaying terrain.
// It takes data from Terrain class and uses it to generate meshes, colliders and
// other things required for user interaction
public class TerrainRenderer : MonoBehaviour
{
    public delegate void OnTileClick(Vector3Int tile);

    public event OnTileClick tileClicked;   // Called when user clicked on a tile

    public float tileWidth = 1f;
    public float tileDepth = 1f;
    public float tileHeight = 1f;

    public TerrainChunk terrainChunkPrefab;
    public int chunkSize = 10;
    public int chunkTextureSize = 10;
    public Material grassMaterial;
    public Material dirtMaterial;
    public Material softRocksMaterial;
    public Material hardRocksMaterial;
    public Material sandMaterial;

    TerrainChunk[,] chunks;
    Terrain terrainData;
    Texture2DArray terrainBaseTextures;
    Texture2DArray terrainNormalMaps;
    Texture2DArray terrainMetallicMaps;
    Texture2DArray terrainOcclusionMaps;

    public bool useNormalMaps;
    public bool useMetallicMaps;
    public bool useOcclusionMaps;

    public Material terrainMaterial;
    Material materialRuntimeCopy;

    Dictionary<TerrainType, TerrainTypeMaterialInfo> materialInfo;

    public void Initialise(Terrain terrainData)
    {
        this.terrainData = terrainData;
        PrepareTerrainMaterial();
    }

    private void PrepareTerrainMaterial()
    {
        materialInfo = new Dictionary<TerrainType, TerrainTypeMaterialInfo>();
        int materialsCount = Enum.GetNames(typeof(TerrainType)).Length;

        // We read format properties from grass texture and assume rest is the same. It better be :)
        Texture2D grassTexture = grassMaterial.GetTexture("_BaseMap") as Texture2D;

        if (useNormalMaps) {
            Texture2D grassNormals = grassMaterial.GetTexture("_BumpMap") as Texture2D;
            terrainNormalMaps = new Texture2DArray(grassNormals.width, grassNormals.height, materialsCount, grassNormals.format, false);
        }

        if (useMetallicMaps) {
            Texture2D grassMetallic = grassMaterial.GetTexture("_MetallicGlossMap") as Texture2D;
            terrainMetallicMaps = new Texture2DArray(grassMetallic.width, grassMetallic.height, materialsCount, grassMetallic.format, false);
        }

        if (useOcclusionMaps) {
            Texture2D grassOcclusion = grassMaterial.GetTexture("_OcclusionMap") as Texture2D;
            terrainOcclusionMaps = new Texture2DArray(grassOcclusion.width, grassOcclusion.height, materialsCount, grassOcclusion.format, false);
        }

        terrainBaseTextures = new Texture2DArray(grassTexture.width, grassTexture.height, materialsCount, grassTexture.format, false);

        ReadMaterialInfo(TerrainType.Grass, grassMaterial);
        ReadMaterialInfo(TerrainType.Dirt, dirtMaterial);
        ReadMaterialInfo(TerrainType.SoftRocks, softRocksMaterial);
        ReadMaterialInfo(TerrainType.HardRocks, hardRocksMaterial);
        ReadMaterialInfo(TerrainType.Sand, sandMaterial);

        // Make a copy so we won't mess with original
        materialRuntimeCopy = new Material(terrainMaterial);
        materialRuntimeCopy.SetTexture("BaseTextures", terrainBaseTextures);

        if (useNormalMaps) {
            materialRuntimeCopy.SetTexture("NormalTextures", terrainNormalMaps);
            materialRuntimeCopy.EnableKeyword("HASNORMALMAP");
        }

        if (useMetallicMaps) {
            materialRuntimeCopy.SetTexture("MetallicTextures", terrainMetallicMaps);
            materialRuntimeCopy.EnableKeyword("HASMETALLICMAP");
        }

        if (useOcclusionMaps) {
            materialRuntimeCopy.SetTexture("OcclusionTextures", terrainOcclusionMaps);
            materialRuntimeCopy.EnableKeyword("HASOCCLUSIONMAP");
        }
    }
    private void ReadMaterialInfo(TerrainType type, Material material)
    {
        Graphics.CopyTexture(material.GetTexture("_BaseMap"), 0, 0, terrainBaseTextures, (int) type, 0);

        if (useNormalMaps)
            Graphics.CopyTexture(material.GetTexture("_BumpMap"), 0, 0, terrainNormalMaps, (int) type, 0);
        
        if (useMetallicMaps)
            Graphics.CopyTexture(material.GetTexture("_MetallicGlossMap"), 0, 0, terrainMetallicMaps, (int) type, 0);

        if (useOcclusionMaps)
            Graphics.CopyTexture(material.GetTexture("_OcclusionMap"), 0, 0, terrainOcclusionMaps, (int) type, 0);

        TerrainTypeMaterialInfo info;
        info.smoothness = (useMetallicMaps) ? material.GetFloat("_Smoothness") : 0f;
        info.type = type;
        info.shaderIndex = (float) type + 0.1f; // +0.1f is to ensure that truncation to int will return correct number in shader
        materialInfo[type] = info;
    }

    public int CreateWorld()
    {
        // Because map can be really large, generating a single mesh is a no-go, as updates to it would take too
        // much time. So we divide world into equaly sized chunks, slicing the world along the X and Z coordinates 
        // (Y is expected to be small anyway). Each chunk then generates its meshes, colliders, etc
        int width = (terrainData.terrainWidth / chunkSize) + ((terrainData.terrainWidth % chunkSize > 0) ? 1 : 0);
        int depth = (terrainData.terrainDepth / chunkSize) + ((terrainData.terrainDepth % chunkSize > 0) ? 1 : 0);

        chunks = new TerrainChunk[width, depth];

        for (int x = 0; x < width; x++) {
            for (int z = 0; z < depth; z++) {
                TerrainChunk chunk = Instantiate<TerrainChunk>(terrainChunkPrefab, this.transform);
                chunk.transform.localPosition = new Vector3(x * chunkSize * tileWidth, 0f, z * chunkSize * tileDepth);
                chunk.transform.localRotation = Quaternion.identity;
                chunk.name = $"Chunk X: {x * chunkSize} Z:{z * chunkSize}, size: {chunkSize}";
                chunk.Initialise(terrainData, this, x, z, chunkSize, chunkTextureSize, materialRuntimeCopy);
                chunk.CreateVoxels();
                chunks[x, z] = chunk;
            }
        }

        return width * depth;
    }

    // This updates entire world, should be called only on startup really
    public void UpdateWorldMesh()
    {
        for (int x = 0; x < chunks.GetLength(0); x++) {
            for (int z = 0; z < chunks.GetLength(1); z++) {
                chunks[x, z].UpdateMesh();
            }
        }
    }

    // Updates mesh for chunk containing given tile
    public void UpdateWorldMeshForTile(Vector3Int tile)
    {
        GetChunkForTile(tile).UpdateMesh();

        // Check if tile is at the chunk boundary and refresh neighbours if needed
        int tileX = tile.x % chunkSize;
        int tileZ = tile.z % chunkSize;

        if (tileX == chunkSize - 1 && terrainData.terrainWidth > tile.x + 1) {  // +1 because tiles are indexed from 0
            GetChunkForTile(new Vector3Int(tile.x + 1, tile.y, tile.z)).UpdateMesh();
        } else if (tileX == 1 && tile.x > 0) {
            GetChunkForTile(new Vector3Int(tile.x - 1, tile.y, tile.z)).UpdateMesh();
        }

        if (tileZ == chunkSize - 1 && terrainData.terrainDepth > tile.z + 1) {  // +1 because tiles are indexed from 0
            GetChunkForTile(new Vector3Int(tile.x, tile.y, tile.z + 1)).UpdateMesh();
        } else if (tileZ == 1 && tile.x > 0) {
            GetChunkForTile(new Vector3Int(tile.x, tile.y , tile.z - 1)).UpdateMesh();
        }
    }

    public TerrainVoxelCollider GetVoxel(Vector3Int tile)
    {
        return GetChunkForTile(tile).GetVoxel(tile);
    }

    public TerrainTypeMaterialInfo GetMaterialInfo(TerrainType type)
    {
        return materialInfo[type];
    }

    private TerrainChunk GetChunkForTile(Vector3Int tile)
    {
        return chunks[tile.x / chunkSize, tile.z / chunkSize];
    }

    void Update()
    {
        // Detection of clicking on tiles
        if (Input.GetMouseButtonDown(0)) {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100f)) {
                TerrainVoxelCollider voxel = hit.collider.GetComponent<TerrainVoxelCollider>();
                if (voxel)
                    tileClicked?.Invoke(new Vector3Int(voxel.tileX, voxel.tileY, voxel.tileZ));
            }
        }
    }
}

}
