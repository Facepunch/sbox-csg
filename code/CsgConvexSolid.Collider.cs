using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Csg
{
    partial class CsgConvexSolid
    {
        public PhysicsShape Collider { get; set; }
        public bool ColliderInvalid { get; private set; }

        [ThreadStatic]
        private static List<Vector3> _sPhysicsHullVertices;

        partial void InvalidateCollider()
        {
            ColliderInvalid = true;
        }

        public void UpdateCollider( PhysicsBody body )
        {
	        if ( !ColliderInvalid && Collider.IsValid() ) return;

            ColliderInvalid = false;

			Collider?.Remove();
			Collider = null;

			var vertices = _sPhysicsHullVertices ??= new List<Vector3>();

			vertices.Clear();

            foreach (var face in _faces)
            {
                if (face.FaceCuts.Count < 3) continue;

                var basis = face.Plane.GetHelper();

                foreach (var cut in face.FaceCuts)
                {
                    vertices.Add( basis.GetPoint( cut, cut.Max ) );
				}
            }

            Collider = body.AddHullShape( Vector3.Zero, Rotation.Identity, vertices );
        }
    }
}
