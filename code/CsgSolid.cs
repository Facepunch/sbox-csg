using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgSolid : ModelEntity
    {
        private readonly List<CsgConvexSolid> _polyhedra = new List<CsgConvexSolid>();

        [Event.Tick.Server]
		private void ServerTick()
        {
            CollisionUpdate();
        }
        
        [Event.Tick.Client]
        private void ClientTick()
        {
			if ( !IsClientOnly )
			{
				CheckInitialGeometry();
			}

			CollisionUpdate();
			MeshUpdate();
        }

        private void ClearPolyhedra()
        {
            foreach ( var poly in _polyhedra )
            {
                poly.Dispose();
            }

            _polyhedra.Clear();
        }

        void OnDrawGizmosSelected()
        {
            foreach ( var poly in _polyhedra )
            {
                poly.DrawGizmos();
            }
        }
    }
}
