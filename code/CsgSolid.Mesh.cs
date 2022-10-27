using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sandbox.Csg
{
    partial class CsgSolid : IHotloadManaged
    {
        private Mesh _mesh;

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
	        if ( !_collisionInvalid && _body.IsValid() ) return;

	        _collisionInvalid = false;

	        if ( !_body.IsValid() )
			{
				Log.Info( $"new collision body ( IsClient: {IsClient}, _polyhedra.Count: {_polyhedra.Count} )" );

				SetupPhysicsFromSphere( PhysicsMotionType.Static, 0f, 1f );

		        if ( !PhysicsBody.IsValid() )
		        {
			        Log.Error( "Unable to set up physics body" );
			        return;
		        }

		        _body = PhysicsBody;
				_body.ClearShapes();
	        }

	        foreach ( var poly in _polyhedra )
	        {
		        poly.UpdateCollider( _body );
	        }
        }

        private void MeshUpdate()
        {
	        if ( !_meshInvalid ) return;

	        _meshInvalid = false;

	        if ( _mesh is not { IsValid: true } )
	        {
		        var material = Material.Load( "materials/csgdemo/default.vmat" );

		        _mesh = new Mesh( material );
	        }

	        UpdateMesh( _mesh, _polyhedra );

	        if ( Model == null )
	        {
		        var modelBuilder = new ModelBuilder();

		        modelBuilder.AddMesh( _mesh );

		        Model = modelBuilder.Create();
	        }

	        EnableDrawing = true;
	        EnableShadowCasting = true;
		}

		[ThreadStatic]
        private static List<CsgVertex> _sVertices;

        [ThreadStatic]
		private static List<int> _sIndices;

		private static void UpdateMesh<T>( Mesh mesh, T polyhedra )
            where T : IEnumerable<CsgConvexSolid>
        {
	        var mins = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
            var maxs = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );

            _sVertices ??= new List<CsgVertex>();
			_sIndices ??= new List<int>();

			_sVertices.Clear();
			_sIndices.Clear();

            foreach (var poly in polyhedra)
			{
				poly.WriteMesh( _sVertices, _sIndices );

				mins = Vector3.Min( mins, poly.VertexMin );
				maxs = Vector3.Max( maxs, poly.VertexMax );
            }

            if ( !mesh.HasVertexBuffer )
            {
	            mesh.CreateVertexBuffer( _sVertices.Count, CsgVertex.Layout, _sVertices );
	            mesh.CreateIndexBuffer( _sIndices.Count, _sIndices );
            }
            else
			{
				mesh.SetVertexBufferSize( _sVertices.Count );
				mesh.SetIndexBufferSize( _sIndices.Count );

				mesh.SetVertexBufferData( _sVertices );
				mesh.SetIndexBufferData( _sIndices );
			}

            mesh.SetIndexRange( 0, _sIndices.Count );
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
		public void WriteMesh( List<CsgVertex> vertices, List<int> indices )
		{
			const float uvScale = 1f / 128f;

			foreach ( var face in _faces )
			{
				var basis = face.Plane.GetHelper();
				var normal = -face.Plane.Normal;
				var tangent = basis.Tu;

				foreach ( var subFace in face.SubFaces )
				{
					if ( subFace.Neighbor != null ) continue;
					if ( subFace.FaceCuts.Count < 3 ) continue;

					var firstIndex = vertices.Count;
					var materialIndex = subFace.MaterialIndex ?? MaterialIndex;

					subFace.FaceCuts.Sort( FaceCut.Comparer );

					foreach ( var cut in subFace.FaceCuts )
					{
						var vertex = basis.GetPoint( cut, cut.Max );

						vertices.Add( new CsgVertex(
							vertex, normal, tangent, new Vector3(
								Vector3.Dot( basis.Tu, vertex ) * uvScale,
								Vector3.Dot( basis.Tv, vertex ) * uvScale,
								materialIndex ) ) );
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
}
