using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Csg
{
    partial class CsgHull
    {
        public void AddNeighbors( HashSet<CsgHull> visited, Queue<CsgHull> queue )
        {
            foreach ( var face in _faces )
            {
                foreach ( var subFace in face.SubFaces )
                {
                    if ( subFace.Neighbor != null && visited.Add( subFace.Neighbor ) )
                    {
                        queue.Enqueue( subFace.Neighbor );
                    }
                }
            }
        }
    }

    partial class CsgSolid
    {
        public const float MinVolume = 0.125f;

        [ThreadStatic] private static List<(CsgHull, int, float)> _sChunks;
        [ThreadStatic] private static HashSet<CsgHull> _sVisited;
        [ThreadStatic] private static Queue<CsgHull> _sVisitQueue;

        private bool _connectivityInvalid;

        private int _nextDisconnectionIndex;

        [Net]
        public CsgSolid ServerDisconnectedFrom { get; set; }

        public int ClientDisconnectionIndex { get; set; }

        [Net]
        public int ServerDisconnectionIndex { get; set; }

        private bool _copiedInitialGeometry;

        private Dictionary<int, CsgSolid> ClientDisconnections { get; } = new();

        private static void GetConnectivityContainers( out List<(CsgHull Root, int Count, float Volume)> chunks,
            out HashSet<CsgHull> visited, out Queue<CsgHull> queue )
        {
            chunks = _sChunks ??= new List<(CsgHull, int, float)>();
            visited = _sVisited ??= new HashSet<CsgHull>();
            queue = _sVisitQueue ??= new Queue<CsgHull>();

            chunks.Clear();
            visited.Clear();
            queue.Clear();
        }

        private void FindChunks( List<(CsgHull Root, int Count, float Volume)> chunks, HashSet<CsgHull> visited, Queue<CsgHull> queue )
        {
            var allHulls = CsgHelpers.RentHullList();

            try
            {
                GetHullsTouching( new BBox( float.NegativeInfinity, float.PositiveInfinity ), allHulls );

                while ( visited.Count < allHulls.Count )
                {
                    queue.Clear();

                    CsgHull root = null;

                    foreach ( var hull in allHulls )
                    {
                        if ( visited.Contains( hull ) ) continue;

                        root = hull;
                        break;
                    }

                    Assert.NotNull( root );

                    visited.Add( root );
                    queue.Enqueue( root );

                    var volume = 0f;
                    var count = 0;

                    while ( queue.Count > 0 )
                    {
                        var next = queue.Dequeue();

                        volume += next.Volume;
                        count += 1;

                        next.AddNeighbors( visited, queue );
                    }

                    chunks.Add( (root, count, volume) );
                }
            }
            finally
            {
                CsgHelpers.ReturnHullList( allHulls );
            }
        }

        private void CheckInitialGeometry()
        {
            if ( _copiedInitialGeometry || ServerDisconnectedFrom == null ) return;

            if ( ServerDisconnectedFrom.ClientDisconnections.TryGetValue( ServerDisconnectionIndex, out var clientCopy ) )
            {
                ServerDisconnectedFrom.ClientDisconnections.Remove( ServerDisconnectionIndex );

                _copiedInitialGeometry = true;
                _appliedModifications = 0;

                Assert.AreEqual( _gridSize, clientCopy._gridSize );

                Clear( true );

                foreach ( var pair in clientCopy._grid )
                {
                    _grid.Add( pair.Key, pair.Value );

                    pair.Value.Solid = this;

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
            if ( !_connectivityInvalid ) return false;

            _connectivityInvalid = false;

            GetConnectivityContainers( out var chunks, out var visited, out var queue );
            FindChunks( chunks, visited, queue );

            chunks.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );

            if ( chunks.Count == 0 || chunks[0].Volume < MinVolume )
            {
                if ( IsClientOnly || IsServer )
                {
                    Delete();
                }
                else
                {
                    EnableDrawing = false;
                    PhysicsEnabled = false;
                    EnableSolidCollisions = false;
                }

                return true;
            }

            if ( !IsStatic && PhysicsBody != null )
            {
                PhysicsBody.Sleeping = false;
            }

            if ( chunks.Count == 1 ) return false;

            foreach ( var chunk in chunks.Skip( 1 ) )
            {
                visited.Clear();
                queue.Clear();

                queue.Enqueue( chunk.Root );
                visited.Add( chunk.Root );

                while ( queue.Count > 0 )
                {
                    var next = queue.Dequeue();

                    next.AddNeighbors( visited, queue );
                }

                var child = chunk.Volume < MinVolume ? null : new CsgSolid( 0f )
                {
                    IsStatic = false,
                    PhysicsEnabled = true,
                    Transform = Transform
                };

                foreach ( var hull in visited )
                {
                    RemoveHull( hull );
                    child?.AddHull( hull );
                }

                if ( child == null )
                {
                    continue;
                }

                var disconnectionIndex = _nextDisconnectionIndex++;

                if ( IsServer )
                {
                    child.ServerDisconnectionIndex = disconnectionIndex;
                    child.ServerDisconnectedFrom = this;
                }

                if ( IsClient )
                {
                    child.ClientDisconnectionIndex = disconnectionIndex;

                    ClientDisconnections.Add( disconnectionIndex, child );
                }
            }

            return true;
        }
    }
}
