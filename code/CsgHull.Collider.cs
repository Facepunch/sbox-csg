using System.IO;
using System.Text;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    partial class CsgHull
    {
        public PhysicsShape Collider { get; internal set; }

        public void InvalidateCollision()
        {
            GridCell?.InvalidateCollision();
            GridCell?.InvalidateConnectivity();

            RemoveCollider();
        }

        public void RemoveCollider()
        {
            if ( Collider.IsValid() && Collider.Body.IsValid() )
            {
                Collider.Remove();
            }

            Collider = null;
        }

        public bool UpdateCollider( PhysicsBody body )
        {
            if ( Collider.IsValid() ) return false;
            if ( IsEmpty ) return false;

            RemoveCollider();

            UpdateVertices();

            Assert.True( _vertices.Count > 3 );

            Collider = body.AddHullShape( Vector3.Zero, Rotation.Identity, _vertices );

            return true;
        }
    }
}
