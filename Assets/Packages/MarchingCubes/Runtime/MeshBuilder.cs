using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Abecombe.MarchingCubes
{
    //
    // Isosurface mesh builder with the marching cubes algorithm
    //
    public class MeshBuilder : IDisposable
    {
        public Mesh Mesh { get; private set; }

        private int[] _grids;
        private int _triangleBudget;
        private ComputeShader _cs;

        public void Init(int dimX, int dimY, int dimZ, int triangleBudget)
        {
            _grids = new[] { dimX, dimY, dimZ };
            _triangleBudget = triangleBudget;
            _cs = Resources.Load<ComputeShader>("MarchingCubes");

            AllocateBuffers();
            AllocateMesh(3 * _triangleBudget);
        }

        public void Dispose()
        {
            ReleaseBuffers();
            ReleaseMesh();
        }

        public void Update(GraphicsBuffer voxels, float target, Vector3 meshScale)
        {
            _counterBuffer.SetCounterValue(0);

            _cs.SetInt("MaxTriangle", _triangleBudget);

            // Isosurface reconstruction
            _cs.SetInts("Dims", _grids);
            _cs.SetVector("Scale", new Vector4(meshScale.x / _grids[0], meshScale.y / _grids[1], meshScale.z / _grids[2], 0));
            _cs.SetFloat("Isovalue", target);
            _cs.SetBuffer(0, "TriangleTable", _triangleTable);
            _cs.SetBuffer(0, "Voxels", voxels);
            _cs.SetBuffer(0, "VertexBuffer", _vertexBuffer);
            _cs.SetBuffer(0, "IndexBuffer", _indexBuffer);
            _cs.SetBuffer(0, "CounterBuffer", _counterBuffer);
            _cs.Dispatch(0, (_grids[0] + 3) >> 2, (_grids[1] + 3) >> 2, (_grids[2] + 3) >> 2);

            // Compute the dispatch indirect arguments
            GraphicsBuffer.CopyCount(_counterBuffer, _counterCopyBuffer, 0);
            _cs.SetBuffer(1, "CounterBuffer", _counterCopyBuffer);
            _cs.SetBuffer(1, "PrevCounterBuffer", _prevCounterBuffer);
            _cs.SetBuffer(1, "ThreadCountBuffer", _threadCountBuffer);
            _cs.SetBuffer(1, "DispatchIndirectBuffer", _dispatchIndirectBuffer);
            _cs.Dispatch(1, 1, 1, 1);

            // Clear unused area of the buffers.
            _cs.SetBuffer(2, "VertexBuffer", _vertexBuffer);
            _cs.SetBuffer(2, "IndexBuffer", _indexBuffer);
            _cs.SetBuffer(2, "CounterBuffer", _counterCopyBuffer);
            _cs.SetBuffer(2, "ThreadCountBuffer", _threadCountBuffer);
            _cs.DispatchIndirect(2, _dispatchIndirectBuffer);

            // Bounding box
            Mesh.bounds = new Bounds(Vector3.zero, meshScale);
        }

        #region Graphics buffer objects
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
            _counterBuffer.Dispose();
            _counterCopyBuffer.Dispose();
            _prevCounterBuffer.Dispose();
            _threadCountBuffer.Dispose();
            _dispatchIndirectBuffer.Dispose();
        }
        #endregion

        #region Mesh objects
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
            _indexBuffer.Dispose();
            Object.Destroy(Mesh);
        }
        #endregion
    }
}