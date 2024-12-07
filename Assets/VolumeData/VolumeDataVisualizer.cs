using Abecombe.MarchingCubes;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Properties;

public class VolumeDataVisualizer : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] TextAsset _volumeData = null;
    [SerializeField] Vector3Int _dimensions = new Vector3Int(256, 256, 113);
    [SerializeField] float _gridScale = 4.0f / 256;
    [SerializeField] int _triangleBudget = 65536 * 16;

    #endregion

    #region Project asset references

    [SerializeField, HideInInspector] ComputeShader _converterCompute = null;

    #endregion

    #region Target isovalue

    [CreateProperty] public float TargetValue { get; set; } = 0.4f;
    float _prevTargetValue;

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
        _builder.Init(_dimensions.x, _dimensions.y, _dimensions.z, _triangleBudget);

        // Voxel data conversion (ushort -> float)
        using var readBuffer = new ComputeBuffer(VoxelCount / 2, sizeof(uint));
        readBuffer.SetData(_volumeData.bytes);

        _converterCompute.SetInts("Dims", _dimensions.x, _dimensions.y, _dimensions.z);
        _converterCompute.SetBuffer(0, "Source", readBuffer);
        _converterCompute.SetBuffer(0, "Voxels", _voxelBuffer);
        _converterCompute.Dispatch(0, _dimensions.x + 7 >> 3, _dimensions.y + 7 >> 3, _dimensions.z + 7 >> 3);

        // UI data source
        FindFirstObjectByType<UIDocument>().rootVisualElement.dataSource = this;
    }

    void OnDestroy()
    {
        _voxelBuffer.Dispose();
        _builder.Dispose();
    }

    void Update()
    {
        // Rebuild the isosurface only when the target value has been changed.
        if (TargetValue == _prevTargetValue) return;

        _builder.Update(_voxelBuffer, TargetValue, _gridScale * (Vector3)_dimensions);
        GetComponent<MeshFilter>().sharedMesh = _builder.Mesh;

        _prevTargetValue = TargetValue;
    }

    #endregion
}