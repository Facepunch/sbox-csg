using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Csg
{
    partial class CsgConvexSolid
    {
        public void AddNeighbors( HashSet<CsgConvexSolid> visited, Queue<CsgConvexSolid> queue )
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

        [ThreadStatic] private static List<(CsgConvexSolid, int, float)> _sChunks;
        [ThreadStatic] private static HashSet<CsgConvexSolid> _sVisited;
        [ThreadStatic] private static Queue<CsgConvexSolid> _sVisitQueue;

        private bool _connectivityInvalid;

        private int _nextDisconnectionIndex;

        [Net]
        public CsgSolid ServerDisconnectedFrom { get; set; }

        public int ClientDisconnectionIndex { get; set; }

        [Net]
		public int ServerDisconnectionIndex { get; set; }

		private bool _copiedInitialGeometry;

		private Dictionary<int, CsgSolid> ClientDisconnections { get; } = new ();

		private static void GetConnectivityContainers( out List<(CsgConvexSolid Root, int Count, float Volume)> chunks,
            out HashSet<CsgConvexSolid> visited, out Queue<CsgConvexSolid> queue )
        {
            chunks = _sChunks ??= new List<(CsgConvexSolid, int, float)>();
            visited = _sVisited ??= new HashSet<CsgConvexSolid>();
            queue = _sVisitQueue ??= new Queue<CsgConvexSolid>();

            chunks.Clear();
            visited.Clear();
            queue.Clear();
        }

        private void FindChunks( List<(CsgConvexSolid Root, int Count, float Volume)> chunks, HashSet<CsgConvexSolid> visited, Queue<CsgConvexSolid> queue )
        {
            while ( visited.Count < _polyhedra.Count )
            {
                queue.Clear();

                CsgConvexSolid root = null;

                foreach ( var poly in _polyhedra )
                {
                    if ( visited.Contains( poly ) ) continue;

                    root = poly;
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

        private void ConnectivityUpdate()
        {
	        if ( !_connectivityInvalid ) return;

	        _connectivityInvalid = false;
			
			if ( _polyhedra.Count == 0 )
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

                return;
            }

            GetConnectivityContainers( out var chunks, out var visited, out var queue );
            FindChunks( chunks, visited, queue );

            chunks.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );

            if ( chunks.Count == 0 || chunks[0].Volume < MinVolume )
            {
	            Delete();
                return;
            }

            if ( !IsStatic && PhysicsBody != null )
            {
	            PhysicsBody.Sleeping = false;
            }

            if ( chunks.Count == 1 ) return;

            foreach ( var chunk in chunks.Skip( 1 ) )
            {
                visited.Clear();
                queue.Clear();

                queue.Enqueue( chunk.Root );
                visited.Add( chunk.Root );

                while ( queue.Count > 0 )
                {
                    var next = queue.Dequeue();

                    next.InvalidateMesh();
                    next.AddNeighbors( visited, queue );
                }

                _polyhedra.RemoveAll( x => visited.Contains( x ) );

                if ( chunk.Volume < MinVolume )
				{
					continue;
                }

				var child = new CsgSolid
				{
					IsStatic = false,
					PhysicsEnabled = true,
					Transform = Transform
				};

                child._polyhedra.AddRange( visited );
                child._meshInvalid = child._collisionInvalid = true;

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
        }
    }
}
