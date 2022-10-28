using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Csg
{
    public record struct SubFaceIndices( int PolyIndex, int FaceIndex, int SubFaceIndex, int MaterialIndex );

    partial class CsgSolid : IHotloadManaged
    {
        private readonly Dictionary<int, Mesh> _meshes = new();
        private Model _model;

        private bool _meshInvalid;
        private bool _collisionInvalid;

        private PhysicsBody _body;

        public float Volume { get; private set; }
        public bool IsStatic { get; set; } = true;
        
        public void Created( IReadOnlyDictionary<string, object> state )
        {
            _meshInvalid = true;
            _collisionInvalid = true;
        }

        private void CollisionUpdate()
        {
            if ( !_collisionInvalid && _body.IsValid() || _polyhedra.Count == 0 ) return;

            _collisionInvalid = false;
            
            if ( !_body.IsValid() )
            {
                foreach ( var poly in _polyhedra )
                {
                    poly.Collider = null;
                }
                
                var group = SetupPhysicsFromSphere( IsStatic ? PhysicsMotionType.Static : PhysicsMotionType.Dynamic, 0f, 1f );

                if ( !group.IsValid() )
                {
                    return;
                }

                _body = group.GetBody( 0 );
                _body.ClearShapes();
            }
            
            const float density = 25f;

            var mass = 0f;
            var volume = 0f;

            foreach ( var poly in _polyhedra )
            {
                poly.UpdateCollider( _body );

                mass += poly.Volume * density;
                volume += poly.Volume;
            }
            
            Volume = volume;

            if ( _body.IsValid() && !IsStatic )
            {
                _body.Mass = mass;
                _body.RebuildMass();
                _body.Sleeping = false;
            }
        }
        
        private void MeshUpdate()
        {
            if ( !_meshInvalid && _model is { IsProcedural: true } || _polyhedra.Count == 0 )
            {
                return;
            }

            _meshInvalid = false;

            var newMeshes = UpdateMeshes( _meshes, _polyhedra );

            if ( _model is not { IsProcedural: true } || newMeshes )
            {
                var modelBuilder = new ModelBuilder();

                foreach ( var pair in _meshes )
                {
                    modelBuilder.AddMesh( pair.Value );
                }

                _model = modelBuilder.Create();
            }

            Model = null;
            Model = _model;

            EnableDrawing = true;
            EnableShadowCasting = true;
        }

        [ThreadStatic]
        private static List<SubFaceIndices> _sSubFaces;

        [ThreadStatic]
        private static List<CsgVertex> _sVertices;

        [ThreadStatic]
        private static List<int> _sIndices;

        private static bool UpdateMeshes<T>( Dictionary<int, Mesh> meshes, T polyhedra )
            where T : IList<CsgConvexSolid>
        {
            _sSubFaces ??= new List<SubFaceIndices>();
            _sVertices ??= new List<CsgVertex>();
            _sIndices ??= new List<int>();

            _sSubFaces.Clear();

            for ( var i = 0; i < polyhedra.Count; ++i )
            {
                polyhedra[i].FindSubFaces( i, _sSubFaces );
            }

            _sSubFaces.Sort( ( a, b ) => a.MaterialIndex - b.MaterialIndex );

            foreach ( var pair in meshes )
            {
                pair.Value.SetIndexRange( 0, 0 );
            }

            var lastMaterialIndex = -1;
            CsgMaterial material = null;
            Mesh mesh = null;

            Vector3 mins = default;
            Vector3 maxs = default;

            var offset = 0;
            var newMeshes = false;

            while ( offset < _sSubFaces.Count )
            {
                var nextIndices = _sSubFaces[offset];
                var poly = polyhedra[nextIndices.PolyIndex];

                if ( nextIndices.MaterialIndex != lastMaterialIndex )
                {
                    lastMaterialIndex = nextIndices.MaterialIndex;
                    material = ResourceLibrary.Get<CsgMaterial>( nextIndices.MaterialIndex );
                    
                    UpdateMesh( mesh, mins, maxs, _sVertices, _sIndices );

                    _sVertices.Clear();
                    _sIndices.Clear();

                    mins = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
                    maxs = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );

                    if ( !meshes.TryGetValue( nextIndices.MaterialIndex, out mesh ) || !mesh.IsValid )
                    {
                        newMeshes = true;
                        meshes[nextIndices.MaterialIndex] = mesh = new Mesh( material.RuntimeMaterial );
                    }
                }

                poly.WriteMeshSubFaces( _sSubFaces, ref offset, material, _sVertices, _sIndices );

                mins = Vector3.Min( mins, poly.VertexMin );
                maxs = Vector3.Max( maxs, poly.VertexMax );
            }
            
            UpdateMesh( mesh, mins, maxs, _sVertices, _sIndices );

            return newMeshes;
        }

        private static void UpdateMesh( Mesh mesh, Vector3 mins, Vector3 maxs, List<CsgVertex> vertices, List<int> indices )
        {
            if ( mesh == null ) return;

            if ( !mesh.HasVertexBuffer )
            {
                mesh.CreateVertexBuffer( vertices.Count, CsgVertex.Layout, vertices );
                mesh.CreateIndexBuffer( indices.Count, indices );
            }
            else
            {
                mesh.SetVertexBufferSize( vertices.Count );
                mesh.SetIndexBufferSize( indices.Count );

                mesh.SetVertexBufferData( vertices );
                mesh.SetIndexBufferData( indices );
            }

            mesh.SetIndexRange( 0, indices.Count );
            mesh.SetBounds( mins, maxs );
        }
    }

    [StructLayout( LayoutKind.Sequential )]
    public record struct CsgVertex( Vector3 Position, Vector3 Normal, Vector3 Tangent, Vector3 TexCoord )
    {
        public static VertexAttribute[] Layout { get; } =
        {
            new (VertexAttributeType.Position, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Normal, VertexAttributeFormat.Float32),
            new (VertexAttributeType.Tangent, VertexAttributeFormat.Float32),
            new (VertexAttributeType.TexCoord, VertexAttributeFormat.Float32)
        };
    }

    partial class CsgConvexSolid
    {
        public void FindSubFaces( int polyIndex, List<SubFaceIndices> subFaces )
        {
            var materialId = Material.ResourceId;

            for ( var faceIndex = 0; faceIndex < _faces.Count; ++faceIndex )
            {
                var face = _faces[faceIndex];

                for ( var subFaceIndex = 0; subFaceIndex < face.SubFaces.Count; ++subFaceIndex )
                {
                    var subFace = face.SubFaces[subFaceIndex];

                    if ( subFace.Neighbor != null ) continue;
                    if ( subFace.FaceCuts.Count < 3 ) continue;

                    subFaces.Add( new (polyIndex, faceIndex, subFaceIndex, subFace.Material?.ResourceId ?? materialId ) );
                }
            }
        }

        public void WriteMeshSubFaces( List<SubFaceIndices> subFaces, ref int offset,
            CsgMaterial material, List<CsgVertex> vertices, List<int> indices )
        {
            const float uvScale = 1f / 128f;

            var lastFaceIndex = -1;

            Face face = default;
            CsgPlane.Helper basis = default;
            Vector3 normal = default;
            Vector3 tangent = default;

            var firstIndices = subFaces[offset];

            for ( ; offset < subFaces.Count; ++offset )
            {
                var subFaceIndices = subFaces[offset];

                if ( subFaceIndices.PolyIndex != firstIndices.PolyIndex ) break;
                if ( subFaceIndices.MaterialIndex != firstIndices.MaterialIndex ) break;

                if ( lastFaceIndex != subFaceIndices.FaceIndex )
                {
                    lastFaceIndex = subFaceIndices.FaceIndex;

                    face = _faces[subFaceIndices.FaceIndex];
                    basis = face.Plane.GetHelper();
                    normal = -face.Plane.Normal;
                    tangent = basis.Tu;
                }

                var subFace = face.SubFaces[subFaceIndices.SubFaceIndex];

                var firstIndex = vertices.Count;

                subFace.FaceCuts.Sort( FaceCut.Comparer );

                foreach ( var cut in subFace.FaceCuts )
                {
                    var vertex = basis.GetPoint( cut, cut.Max );

                    vertices.Add( new CsgVertex(
                        vertex, normal, tangent, new Vector3(
                            Vector3.Dot( basis.Tu, vertex ) * uvScale,
                            Vector3.Dot( basis.Tv, vertex ) * uvScale,
                            0f ) ) );
                }

                for ( var i = 2; i < subFace.FaceCuts.Count; ++i )
                {
                    indices.Add( firstIndex );
                    indices.Add( firstIndex + i - 1 );
                    indices.Add( firstIndex + i );
                }
            }
        }
    }
}
