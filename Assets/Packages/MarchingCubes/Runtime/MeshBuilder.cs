using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Abecombe.MarchingCubes
{
    /// <summary>
    /// Iso-surface reconstruction using Marching Cubes algorithm.
    /// Fork from https://github.com/keijiro/ComputeMarchingCubes
    /// </summary>
    public class MeshBuilder : IDisposable
    {
        public Mesh Mesh { get; private set; }
        public ComputeShader Cs { get; private set; }

        private int _triangleBudget;

        private int[] _gridSize = new int[3];
        private bool _inited = false;

        public void Init(ComputeShader cs)
        {
            Init(cs, 65536);
        }
        public void Init(int triangleBudget = 65536)
        {
            Init(null, triangleBudget);
        }
        public void Init(ComputeShader cs, int triangleBudget)
        {
            Dispose();

            _triangleBudget = triangleBudget;
            Cs = cs ?? Resources.Load<ComputeShader>("MarchingCubes");

            AllocateBuffers();
            AllocateMesh(3 * _triangleBudget);

            _inited = true;
        }

        public void Dispose()
        {
            if (!_inited) return;

            ReleaseBuffers();
            ReleaseMesh();

            _inited = false;
        }

        public void Update(GraphicsBuffer dataBuffer, int gridSizeX, int gridSizeY, int gridSizeZ, float isoValue, Vector3 meshScale)
        {
            if (!_inited)
            {
                Debug.LogError("MeshBuilder is not initialized.");
                return;
            }

            Cs.SetInt("_TriangleBudget", _triangleBudget);

            // Iso-surface reconstruction
            _counterBuffer.SetCounterValue(0);
            _gridSize[0] = gridSizeX;
            _gridSize[1] = gridSizeY;
            _gridSize[2] = gridSizeZ;
            Cs.SetInts("_GridSize", _gridSize);
            Cs.SetVector("_Scaling", new Vector4(meshScale.x / gridSizeX, meshScale.y / gridSizeY, meshScale.z / gridSizeZ, 0));
            Cs.SetFloat("_IsoValue", isoValue);
            Cs.SetBuffer(0, "_TriangleTable", _triangleTable);
            if (dataBuffer != null)
            {
                Cs.SetBuffer(0, "_DataBuffer", dataBuffer);
            }
            Cs.SetBuffer(0, "_VertexBuffer", _vertexBuffer);
            Cs.SetBuffer(0, "_IndexBuffer", _indexBuffer);
            Cs.SetBuffer(0, "_CounterBuffer", _counterBuffer);
            Cs.Dispatch(0, (gridSizeX + 3) / 4, (gridSizeY + 3) / 4, (gridSizeZ + 3) / 4);

            // Compute the dispatch indirect arguments
            GraphicsBuffer.CopyCount(_counterBuffer, _counterCopyBuffer, 0);
            Cs.SetBuffer(1, "_CounterBuffer", _counterCopyBuffer);
            Cs.SetBuffer(1, "_PrevCounterBuffer", _prevCounterBuffer);
            Cs.SetBuffer(1, "_ThreadCountBuffer", _threadCountBuffer);
            Cs.SetBuffer(1, "_DispatchIndirectBuffer", _dispatchIndirectBuffer);
            Cs.Dispatch(1, 1, 1, 1);

            // Clear unused area of the buffers.
            Cs.SetBuffer(2, "_VertexBuffer", _vertexBuffer);
            Cs.SetBuffer(2, "_IndexBuffer", _indexBuffer);
            Cs.SetBuffer(2, "_CounterBuffer", _counterCopyBuffer);
            Cs.SetBuffer(2, "_ThreadCountBuffer", _threadCountBuffer);
            Cs.DispatchIndirect(2, _dispatchIndirectBuffer);

            // Bounding box
            Mesh.bounds = new Bounds(Vector3.zero, meshScale);
        }
        public void Update(GraphicsBuffer dataBuffer, Vector3Int gridSize, float isoValue, Vector3 meshScale)
        {
            Update(dataBuffer, gridSize.x, gridSize.y, gridSize.z, isoValue, meshScale);
        }
        public void Update(int gridSizeX, int gridSizeY, int gridSizeZ, float isoValue, Vector3 meshScale)
        {
            Update(null, gridSizeX, gridSizeY, gridSizeZ, isoValue, meshScale);
        }
        public void Update(Vector3Int gridSize, float isoValue, Vector3 meshScale)
        {
            Update(null, gridSize.x, gridSize.y, gridSize.z, isoValue, meshScale);
        }

        #region Graphics Buffer Objects
        private GraphicsBuffer _triangleTable;
        private GraphicsBuffer _counterBuffer;
        private GraphicsBuffer _counterCopyBuffer;
        private GraphicsBuffer _prevCounterBuffer;
        private GraphicsBuffer _threadCountBuffer;
        private GraphicsBuffer _dispatchIndirectBuffer;

        private void AllocateBuffers()
        {
            // Marching cubes triangle table
            _triangleTable = new GraphicsBuffer(GraphicsBuffer.Target.Structured, PrecalculatedData.TriangleTable.Length, sizeof(ulong));
            _triangleTable.SetData(PrecalculatedData.TriangleTable);
            // Buffer for triangle counting
            _counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Counter, 1, 4);
            _counterCopyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 4);
            // Buffer for the previous counter value
            _prevCounterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            _prevCounterBuffer.SetData(new uint[] { 0 });
            // Buffer for thread count
            _threadCountBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            _threadCountBuffer.SetData(new uint[] { 0 });
            // Buffer for dispatch indirect arguments
            _dispatchIndirectBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, 4);
            _dispatchIndirectBuffer.SetData(new uint[] { 0, 1, 1 });
        }

        private void ReleaseBuffers()
        {
            _triangleTable.Dispose();
            _triangleTable = null;
            _counterBuffer.Dispose();
            _counterBuffer = null;
            _counterCopyBuffer.Dispose();
            _counterCopyBuffer = null;
            _prevCounterBuffer.Dispose();
            _prevCounterBuffer = null;
            _threadCountBuffer.Dispose();
            _threadCountBuffer = null;
            _dispatchIndirectBuffer.Dispose();
            _dispatchIndirectBuffer = null;
        }
        #endregion

        #region Mesh Objects
        private GraphicsBuffer _vertexBuffer;
        private GraphicsBuffer _indexBuffer;

        private void AllocateMesh(int vertexCount)
        {
            Mesh = new Mesh();

            // We want GraphicsBuffer access as Raw (ByteAddress) buffers.
            Mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            Mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

            // Vertex position: float32 x 3
            var vp = new VertexAttributeDescriptor(VertexAttribute.Position);
            // Vertex normal: float32 x 3
            var vn = new VertexAttributeDescriptor(VertexAttribute.Normal);

            // Vertex/index buffer formats
            Mesh.SetVertexBufferParams(vertexCount, vp, vn);
            Mesh.SetIndexBufferParams(vertexCount, IndexFormat.UInt32);

            // Submesh initialization
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount), MeshUpdateFlags.DontRecalculateBounds);

            // GraphicsBuffer references
            _vertexBuffer = Mesh.GetVertexBuffer(0);
            _indexBuffer = Mesh.GetIndexBuffer();
        }

        private void ReleaseMesh()
        {
            _vertexBuffer.Dispose();
            _vertexBuffer = null;
            _indexBuffer.Dispose();
            _indexBuffer = null;
            UnityEngine.Object.Destroy(Mesh);
            Mesh = null;
        }
        #endregion
    }
}