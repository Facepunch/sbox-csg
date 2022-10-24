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
        public const float MinVolume = 1f;

        [ThreadStatic] private static List<(CsgConvexSolid, int, float)> _sChunks;
        [ThreadStatic] private static HashSet<CsgConvexSolid> _sVisited;
        [ThreadStatic] private static Queue<CsgConvexSolid> _sVisitQueue;

        private bool _connectivityInvalid;

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

        private bool ConnectivityUpdate()
        {
	        if ( !_connectivityInvalid ) return true;

	        _connectivityInvalid = false;

			if ( _polyhedra.Count == 0 )
            {
				Delete();
                return false;
            }

            GetConnectivityContainers( out var chunks, out var visited, out var queue );
            FindChunks( chunks, visited, queue );

            chunks.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );

            if ( chunks.Count == 0 || chunks[0].Volume < MinVolume )
            {
				Delete();
                return false;
            }

            Volume = chunks[0].Volume;

            if ( chunks.Count == 1 ) return true;

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

                var child = new CsgSolid { Transform = Transform };

                child._polyhedra.AddRange( visited );
                child._meshInvalid = true;

                if ( IsServer )
				{
					child.ServerTick();
				}

                if ( IsClient )
                {
					child.ClientTick();
                }
            }

            return true;
        }
    }
}
