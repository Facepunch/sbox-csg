using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgSolid : ModelEntity
    {
	    public const bool LogTimings = false;

        private readonly List<CsgConvexSolid> _polyhedra = new List<CsgConvexSolid>();

        public override void Spawn()
        {
	        base.Spawn();

	        Transmit = TransmitType.Always;
        }

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

			MeshUpdate();
			CollisionUpdate();
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
