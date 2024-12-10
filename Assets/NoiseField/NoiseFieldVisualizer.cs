using Abecombe.MarchingCubes;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class NoiseFieldVisualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] Vector3Int _dimensions = new Vector3Int(64, 32, 64);
    [SerializeField] float _gridScale = 4.0f / 64;
    [SerializeField] int _triangleBudget = 65536;
    [SerializeField] float _targetValue = 0;

    #endregion

    #region Project asset references

    [SerializeField, HideInInspector] ComputeShader _volumeCompute = null;

    #endregion

    #region Private members

    int VoxelCount => _dimensions.x * _dimensions.y * _dimensions.z;

    GraphicsBuffer _voxelBuffer;
    MeshBuilder _builder = new();

    #endregion

    #region MonoBehaviour implementation

    void Start()
    {
        _voxelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VoxelCount, sizeof(float));
        _builder.Init(_triangleBudget);
    }

    void OnDestroy()
    {
        _voxelBuffer.Dispose();
        _builder.Dispose();
    }

    void Update()
    {
        // Noise field update
        _volumeCompute.SetInts("Dims", _dimensions.x, _dimensions.y, _dimensions.z);
        _volumeCompute.SetFloat("Scale", _gridScale);
        _volumeCompute.SetFloat("Time", Time.time);
        _volumeCompute.SetBuffer(0, "Voxels", _voxelBuffer);
        _volumeCompute.Dispatch(0, _dimensions.x + 7 >> 3, _dimensions.y + 7 >> 3, _dimensions.z + 7 >> 3);

        // Isosurface reconstruction
        _builder.Update(_voxelBuffer, _dimensions, _targetValue, _gridScale * (Vector3)_dimensions);
        GetComponent<MeshFilter>().sharedMesh = _builder.Mesh;
    }

    #endregion
}