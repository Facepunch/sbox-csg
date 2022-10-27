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
			if ( !IsClientOnly && ServerDisconnectedFrom != null )
			{
				if ( ServerDisconnectedFrom.ClientDisconnections.TryGetValue( ServerDisconnectionIndex, out var clientCopy ) )
				{
					ServerDisconnectedFrom.ClientDisconnections.Remove( ServerDisconnectionIndex );

					ServerDisconnectedFrom = null;
					ServerDisconnectionIndex = default;

					_appliedModifications = 0;
					
					ClearPolyhedra();

					_polyhedra.AddRange( clientCopy._polyhedra );
					
					foreach ( var poly in _polyhedra )
					{
						poly.Collider = null;
						poly.InvalidateMesh();
					}
					
					clientCopy.Delete();

					_collisionInvalid = true;
					_meshInvalid = true;
					
					OnModificationsChanged();
				}
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
