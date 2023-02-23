#if !SANDBOX_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Csg
{
    public partial class CsgEntity : ModelEntity
    {
        [ConVar.Server( "csg_log", Help = "If set, CSG timing info is logged" )]
        public static bool LogTimings { get; set; }

        public CsgSolid Solid { get; private set; }

        [Net]
        public Vector3 GridSize { get; set; }

        [Net]
        public bool IsStatic { get; set; } = true;

        public CsgEntity()
        {
            Game.AssertClient( nameof( CsgEntity ) );
        }

        public CsgEntity( Vector3 gridSize )
        {
            GridSize = gridSize;
            Solid = new CsgSolid( gridSize );
        }

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        public override void ClientSpawn()
        {
            base.ClientSpawn();

            Solid = new CsgSolid( GridSize );

            OnModificationsChanged();
        }

        [Event.Tick.Server]
        private void ServerTick()
        {
            if ( _invalidConnectivity.Count > 0 )
            {
                if ( Disconnect() && Deleted )
                {
                    return;
                }
            }

            SendModifications();
            CollisionUpdate();
        }

        [Event.Tick.Client]
        private void ClientTick()
        {
            if ( !IsClientOnly )
            {
                CheckInitialGeometry();
            }

            if ( Deleted ) return;

            MeshUpdate();
            CollisionUpdate();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            DeleteSceneObjects();
        }

    }
}

#endif
