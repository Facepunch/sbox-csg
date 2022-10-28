using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Csg
{
    partial class CsgConvexSolid
    {
        public PhysicsShape Collider { get; internal set; }

        [ThreadStatic]
        private static List<Vector3> _sPhysicsHullVertices;

        public void InvalidateCollider()
        {
	        RemoveCollider();
        }

        private void RemoveCollider()
        {
	        if ( Collider.IsValid() && Collider.Body.IsValid() )
	        {
		        Collider?.Remove();
			}

	        Collider = null;
        }

		public void UpdateCollider( PhysicsBody body )
        {
	        if ( Collider.IsValid() ) return;

	        RemoveCollider();

			var vertices = _sPhysicsHullVertices ??= new List<Vector3>();

	        vertices.Clear();

	        foreach ( var face in _faces )
	        {
		        if ( face.FaceCuts.Count < 3 ) continue;

		        var basis = face.Plane.GetHelper();

		        foreach ( var cut in face.FaceCuts )
		        {
			        vertices.Add( basis.GetPoint( cut, cut.Max ) );
		        }
	        }

	        Collider = body.AddHullShape( Vector3.Zero, Rotation.Identity, vertices );
        }
    }
}
