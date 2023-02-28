﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    public record struct SubFaceIndices( int PolyIndex, int FaceIndex, int SubFaceIndex, int MaterialIndex );

    partial class CsgSceneObject
    {
        private static Stopwatch Timer { get; } = new Stopwatch();

        public float Volume { get; private set; }

        internal bool MeshUpdate()
        {
            if ( _grid.Count == 0 || _invalidMesh.Count == 0 ) return false;

            Timer.Restart();

            foreach ( var cell in _invalidMesh )
            {
                if ( cell.Solid != this ) continue;

                if ( cell.Hulls.Count == 0 )
                {
                    cell.SceneObject?.Delete();
                    cell.PostMeshUpdate();
                    continue;
                }

                if ( UpdateMeshes( cell.Meshes, cell.Hulls, false ) || !cell.SceneObject.IsValid() && cell.Meshes.Count > 0 )
                {
                    var modelBuilder = new ModelBuilder();
                    
                    foreach ( var (_, mesh) in cell.Meshes )
                    {
                        modelBuilder.AddMesh( mesh );
                    }

                    var model = modelBuilder.Create();

                    cell.SceneObject?.Delete();
                    cell.SceneObject = new SceneObject( World, model );

                    AddChild( $"Cell {cell.Coord}", cell.SceneObject );
                }

                cell.PostMeshUpdate();
            }

            _invalidMesh.Clear();
            
            return true;
        }

        [ThreadStatic]
        private static List<SubFaceIndices> _sSubFaces;

        [ThreadStatic]
        private static List<CsgVertex> _sVertices;

        [ThreadStatic]
        private static List<int> _sIndices;
        
        public static Material FrontWireframeMaterial { get; } = Material.Load( "materials/csgeditor/wireframe_front.vmat" );
        public static Material BackWireframeMaterial { get; } = Material.Load( "materials/csgeditor/wireframe_back.vmat" );

        internal static bool UpdateMeshes<T>( Dictionary<int, Mesh> meshes, T polyhedra, bool wireframe )
            where T : IList<CsgHull>
        {
            _sSubFaces ??= new List<SubFaceIndices>();
            _sVertices ??= new List<CsgVertex>();
            _sIndices ??= new List<int>();

            _sSubFaces.Clear();

            for ( var i = 0; i < polyhedra.Count; ++i )
            {
                polyhedra[i].FindSubFaces( i, _sSubFaces );
            }

            if ( wireframe )
            {
                for ( var i = 0; i < _sSubFaces.Count; ++i )
                {
                    _sSubFaces[i] = _sSubFaces[i] with { MaterialIndex = 0 };
                }
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
                    material = wireframe ? null : ResourceLibrary.Get<CsgMaterial>( nextIndices.MaterialIndex );

                    UpdateMesh( mesh, mins, maxs, _sVertices, _sIndices );

                    _sVertices.Clear();
                    _sIndices.Clear();

                    mins = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
                    maxs = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );

                    if ( !meshes.TryGetValue( nextIndices.MaterialIndex, out mesh ) || !mesh.IsValid )
                    {
                        newMeshes = true;
                        meshes[nextIndices.MaterialIndex] = mesh = new Mesh( wireframe ? BackWireframeMaterial.CreateCopy() : material.RuntimeMaterial,
                            wireframe ? MeshPrimitiveType.Lines : MeshPrimitiveType.Triangles );
                    }
                }

                poly.WriteMeshSubFaces( _sSubFaces, ref offset, material, _sVertices, _sIndices, wireframe );

                var bounds = poly.VertexBounds;

                mins = Vector3.Min( mins, bounds.Mins );
                maxs = Vector3.Max( maxs, bounds.Maxs );
            }

            UpdateMesh( mesh, mins, maxs, _sVertices, _sIndices );

            if ( wireframe )
            {
                if ( !meshes.TryGetValue( 1, out mesh ) || !mesh.IsValid )
                {
                    newMeshes = true;
                    meshes[1] = mesh = new Mesh( FrontWireframeMaterial.CreateCopy(), MeshPrimitiveType.Lines );
                }

                UpdateMesh( mesh, mins, maxs, _sVertices, _sIndices );
            }

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

    partial class CsgHull
    {
        public void FindSubFaces( int polyIndex, List<SubFaceIndices> subFaces )
        {
            var materialId = Material?.ResourceId ?? 0;

            for ( var faceIndex = 0; faceIndex < _faces.Count; ++faceIndex )
            {
                var face = _faces[faceIndex];

                for ( var subFaceIndex = 0; subFaceIndex < face.SubFaces.Count; ++subFaceIndex )
                {
                    var subFace = face.SubFaces[subFaceIndex];

                    if ( subFace.Neighbor != null ) continue;
                    if ( subFace.FaceCuts.Count < 3 ) continue;

                    subFaces.Add( new( polyIndex, faceIndex, subFaceIndex, subFace.Material?.ResourceId ?? materialId ) );
                }
            }
        }

        public void WriteMeshSubFaces( List<SubFaceIndices> subFaces, ref int offset,
            CsgMaterial material, List<CsgVertex> vertices, List<int> indices, bool wireframe )
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

                if ( wireframe )
                {
                    indices.Add( firstIndex + subFace.FaceCuts.Count - 1 );
                    indices.Add( firstIndex );

                    for ( var i = 1; i < subFace.FaceCuts.Count; ++i )
                    {
                        indices.Add( firstIndex + i - 1 );
                        indices.Add( firstIndex + i );
                    }
                }
                else
                {
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
}
