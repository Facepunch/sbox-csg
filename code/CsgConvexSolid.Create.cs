using System;

namespace Sandbox.Csg
{
	partial class CsgConvexSolid
	{
		public static CsgConvexSolid CreateCube( BBox bounds )
		{
			var mesh = new CsgConvexSolid();

			mesh.Clip( new CsgPlane( new Vector3( 1f, 0f, 0f ), bounds.Mins ) );
			mesh.Clip( new CsgPlane( new Vector3( -1f, 0f, 0f ), bounds.Maxs ) );
			mesh.Clip( new CsgPlane( new Vector3( 0f, 1f, 0f ), bounds.Mins ) );
			mesh.Clip( new CsgPlane( new Vector3( 0f, -1f, 0f ), bounds.Maxs ) );
			mesh.Clip( new CsgPlane( new Vector3( 0f, 0f, 1f ), bounds.Mins ) );
			mesh.Clip( new CsgPlane( new Vector3( 0f, 0f, -1f ), bounds.Maxs ) );

			return mesh;
		}

		private static Vector3 DistortNormal( Vector3 normal, Random random, float distortion )
		{
			// TODO
			return normal;
		}

		public static CsgConvexSolid CreateDodecahedron( Vector3 center, float radius )
		{
			return CreateDodecahedron( center, radius, null, 0f );
		}

		public static CsgConvexSolid CreateDodecahedron( Vector3 center, float radius, Random random, float distortion )
		{
			distortion = Math.Clamp( distortion, 0f, 1f ) * 0.25f;

			var mesh = new CsgConvexSolid();

			mesh.Clip( new CsgPlane( DistortNormal( Vector3.Up, random, distortion ), -radius ) );
			mesh.Clip( new CsgPlane( DistortNormal( Vector3.Down, random, distortion ), -radius ) );

			var rot = Rotation.FromAxis( Vector3.Right, 60f );

			for ( var i = 0; i < 5; ++i )
			{
				mesh.Clip( new CsgPlane( DistortNormal( rot * Vector3.Down, random, distortion ), -radius ) );
				mesh.Clip( new CsgPlane( DistortNormal( rot * Vector3.Up, random, distortion ), -radius ) );

				rot = Rotation.FromAxis( Vector3.Up, 72f ) * rot;
			}

			mesh.Transform( Matrix.CreateTranslation( center ) );

			return mesh;
		}
	}
}
