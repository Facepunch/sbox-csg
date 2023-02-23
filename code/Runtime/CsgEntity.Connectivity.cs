#if !SANDBOX_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Csg
{
    partial class CsgEntity
    {
        public const float MinVolume = 0.125f;

        private int _nextDisconnectionIndex;

        [Net]
        public bool DisconnectIslands { get; set; } = true;

        [Net]
        public CsgEntity ServerDisconnectedFrom { get; set; }

        public int ClientDisconnectionIndex { get; set; }

        [Net]
        public int ServerDisconnectionIndex { get; set; }

        private bool _copiedInitialGeometry;

        private Dictionary<int, CsgEntity> ClientDisconnections { get; } = new();

        public bool Deleted { get; private set; }

        [ThreadStatic]
        private static List<(CsgIsland Root, int Count, float Volume)> _sChunks;

        private void CheckInitialGeometry()
        {
            if ( _copiedInitialGeometry || ServerDisconnectedFrom == null ) return;

            if ( ServerDisconnectedFrom.ClientDisconnections.TryGetValue( ServerDisconnectionIndex, out var clientCopy ) )
            {
                ServerDisconnectedFrom.ClientDisconnections.Remove( ServerDisconnectionIndex );

                _copiedInitialGeometry = true;
                _appliedModifications = 0;

                CsgHelpers.AssertAreEqual( Solid.GridSize, clientCopy.Solid.GridSize );

                Solid.Clear( true );

                foreach ( var pair in clientCopy._grid )
                {
                    _grid.Add( pair.Key, pair.Value );

                    pair.Value.Solid = this;
                    pair.Value.InvalidateMesh();
                    pair.Value.InvalidateCollision();
                    pair.Value.InvalidateConnectivity();

                    foreach ( var hull in pair.Value.Hulls )
                    {
                        hull.RemoveCollider();
                    }
                }

                clientCopy.Clear( false );
                clientCopy.Delete();

                OnModificationsChanged();
            }
        }

        private bool ConnectivityUpdate()
        {
            if ( !DisconnectIslands )
            {
                return false;
            }

            var chunks = _sChunks ??= new List<(CsgIsland Root, int Count, float Volume)>();
            chunks.Clear();

            if ( !Solid.ConnectivityUpdate( chunks ) )
            {
                return false;
            }

            if ( LogTimings )
            {
                Log.Info( $"Chunks: {chunks.Count}" );

                foreach ( var chunk in chunks )
                {
                    Log.Info( $"  {chunk.Volume}, {chunk.Count}" );
                }
            }

            // Handle the whole solid being too small / empty

            if ( chunks.Count == 0 || chunks[0].Volume < MinVolume )
            {
                Deleted = true;

                if ( IsClientOnly || Game.IsServer )
                {
                    Delete();
                }
                else
                {
                    EnableDrawing = false;
                    PhysicsEnabled = false;
                    EnableSolidCollisions = false;

                    DeleteSceneObjects();
                }

                return true;
            }

            if ( chunks.Count == 1 ) return false;

            // Time to split, let's wake up

            if ( !IsStatic && PhysicsBody != null )
            {
                PhysicsBody.Sleeping = false;
            }

            // Leave most voluminous chunk in this solid, but create new solids for the rest

            var visited = remaining;

            foreach ( var chunk in chunks.Skip( 1 ) )
            {
                visited.Clear();
                queue.Clear();

                queue.Enqueue( chunk.Root );
                visited.Add( chunk.Root );

                while ( queue.Count > 0 )
                {
                    var next = queue.Dequeue();

                    foreach ( var neighbor in next.Neighbors )
                    {
                        if ( visited.Add( neighbor ) )
                        {
                            queue.Enqueue( neighbor );
                        }
                    }
                }

                var child = chunk.Volume < MinVolume ? null : new CsgSolid( 0f )
                {
                    IsStatic = false,
                    PhysicsEnabled = true,
                    Transform = Transform
                };

                foreach ( var island in visited )
                {
                    foreach ( var hull in island.Hulls )
                    {
                        RemoveHull( hull );
                        child?.AddHull( hull );
                    }
                }

                if ( child == null )
                {
                    continue;
                }

                var disconnectionIndex = _nextDisconnectionIndex++;

                if ( Game.IsServer )
                {
                    child.ServerDisconnectionIndex = disconnectionIndex;
                    child.ServerDisconnectedFrom = this;
                }

                if ( Game.IsClient )
                {
                    child.ClientDisconnectionIndex = disconnectionIndex;

                    ClientDisconnections.Add( disconnectionIndex, child );
                }
            }

            return true;
        }
    }
}

#endif
