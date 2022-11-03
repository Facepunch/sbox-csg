namespace Sandbox.Csg
{
    partial class CsgHull
    {
        public PhysicsShape Collider { get; internal set; }

        public void InvalidateCollision()
        {
            if ( GridCell != null )
            {
                GridCell.CollisionInvalid = true;
                GridCell.ConnectivityInvalid = true;
            }

            _vertexPropertiesInvalid = true;

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

            UpdateVertexProperties();

            Collider = body.AddHullShape( Vector3.Zero, Rotation.Identity, _vertices );

            return true;
        }
    }
}
