using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgSolid : ModelEntity
    {
        private readonly List<CsgConvexSolid> _polyhedra = new List<CsgConvexSolid>();

        public int DebugIndex { get; set; }
		
        [Event.Tick.Server]
		private void ServerTick()
		{
			if ( ConnectivityUpdate() )
			{
				CollisionUpdate();
			}
		}
		
		[Event.Tick.Client]
        private void ClientTick()
		{
			if ( ConnectivityUpdate() )
			{
				CollisionUpdate();
				MeshUpdate();
			}
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
