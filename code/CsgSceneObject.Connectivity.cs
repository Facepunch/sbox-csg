using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    partial class CsgHull
    {
        internal CsgIsland Island { get; set; }

        internal void AddNeighbors( CsgIsland island, Queue<CsgHull> queue )
        {
            Assert.NotNull( GridCell );

            UpdateNeighbors();

            foreach ( var (neighbor, _) in _neighbors )
            {
                if ( neighbor.GridCell == null )
                {
                    Log.Warning( $"Null grid cell: {neighbor.VertexAverage}" );
                    continue;
                }

                if ( neighbor.GridCell != GridCell )
                {
                    island.NeighborHulls.Add( neighbor );
                    continue;
                }

                if ( neighbor.Island == island ) continue;

                Assert.AreEqual( null, neighbor.Island );

                neighbor.Island = island;

                Assert.True( island.Hulls.Add( neighbor ) );

                neighbor.UpdateNeighbors();

                Assert.True( neighbor._neighbors.ContainsKey( this ) );

                queue.Enqueue( neighbor );
            }
        }
    }

    internal class CsgIsland
    {
        public HashSet<CsgHull> Hulls { get; } = new HashSet<CsgHull>();
        public HashSet<CsgHull> NeighborHulls { get; } = new HashSet<CsgHull>();
        public HashSet<CsgIsland> Neighbors { get; } = new HashSet<CsgIsland>();

        public float Volume { get; set; }

        public void Clear()
        {
            foreach ( var neighbor in Neighbors )
            {
                Assert.True( neighbor.Neighbors.Remove( this ) );
            }

            Neighbors.Clear();
            NeighborHulls.Clear();

            foreach ( var hull in Hulls )
            {
                if ( hull.Island == this )
                {
                    hull.Island = null;
                }
            }

            Hulls.Clear();

            Volume = 0f;
        }

        [ThreadStatic] private static Queue<CsgHull> _sVisitQueue;

        public void Populate( CsgHull root )
        {
            Assert.AreEqual( null, root.Island );

            CsgHelpers.AssertAreEqual( 0, Hulls.Count );
            CsgHelpers.AssertAreEqual( 0, NeighborHulls.Count );

            var queue = _sVisitQueue ??= new Queue<CsgHull>();
            queue.Clear();

            Hulls.Add( root );
            queue.Enqueue( root );

            root.Island = this;

            while ( queue.TryDequeue( out var next ) )
            {
                Volume += next.Volume;

                next.AddNeighbors( this, queue );
            }
        }
    }

    partial class CsgSceneObject
    {
        private bool UpdateIslands()
        {
            if ( _invalidConnectivity.Count == 0 ) return false;

            foreach ( var cell in _invalidConnectivity )
            {
                if ( cell.Solid != this ) continue;

                // Clear existing islands for re-use

                foreach ( var island in cell.Islands )
                {
                    island.Clear();
                }

                // Populate islands

                var nextIslandIndex = 0;
                var remaining = CsgHelpers.RentHullSet();

                try
                {
                    foreach ( var hull in cell.Hulls )
                    {
                        remaining.Add( hull );
                    }

                    while ( remaining.Count > 0 )
                    {
                        var island = cell.GetOrCreateIsland( nextIslandIndex++ );
                        var root = remaining.First();

                        island.Populate( root );

                        Assert.True( island.Hulls.Count > 0 );

                        var oldCount = remaining.Count;

                        remaining.ExceptWith( island.Hulls );

                        CsgHelpers.AssertAreEqual( oldCount - island.Hulls.Count, remaining.Count );
                    }
                }
                finally
                {
                    CsgHelpers.Return( remaining );
                }

                // Remove empty islands

                for ( var i = cell.Islands.Count - 1; i >= 0; i-- )
                {
                    if ( cell.Islands[i].Hulls.Count == 0 )
                    {
                        cell.Islands.RemoveAt( i );
                    }
                }

                // Sort by volume (descending)

                cell.Islands.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );
            }

            // Update neighbors for changed islands

            foreach ( var cell in _invalidConnectivity )
            {
                if ( cell.Solid != this ) continue;

                foreach ( var island in cell.Islands )
                {
                    foreach ( var neighborHull in island.NeighborHulls )
                    {
                        Assert.NotNull( neighborHull.Island );
                        Assert.False( neighborHull.Island == island );

                        island.Neighbors.Add( neighborHull.Island );
                        neighborHull.Island.Neighbors.Add( island );
                    }

                    // Don't need these any more

                    island.NeighborHulls.Clear();
                }

                cell.PostConnectivityUpdate();
            }

            _invalidConnectivity.Clear();

            return true;
        }

        [ThreadStatic]
        private static HashSet<CsgIsland> _sIslandSet;
        [ThreadStatic]
        private static Queue<CsgIsland> _sIslandQueue;

        internal bool ConnectivityUpdate( List<(CsgIsland Root, int Count, float Volume)> outChunks )
        {
            if ( !UpdateIslands() ) return false;

            // Find all islands

            var remaining = _sIslandSet ??= new HashSet<CsgIsland>();
            remaining.Clear();

            foreach ( var (_, cell) in _grid )
            {
                foreach ( var island in cell.Islands )
                {
                    remaining.Add( island );
                }
            }

            // Find chunks of connected islands

            var queue = _sIslandQueue ??= new Queue<CsgIsland>();
            var chunks = outChunks;

            chunks.Clear();

            while ( remaining.Count > 0 )
            {
                queue.Clear();

                var root = remaining.First();
                var volume = root.Volume;
                var count = 1;

                remaining.Remove( root );
                queue.Enqueue( root );

                while ( queue.TryDequeue( out var next ) )
                {
                    foreach ( var neighbor in next.Neighbors )
                    {
                        if ( remaining.Remove( neighbor ) )
                        {
                            queue.Enqueue( neighbor );

                            volume += neighbor.Volume;
                            count += 1;
                        }
                    }
                }

                chunks.Add( (root, count, volume) );
            }

            // Sort by volume (descending)

            chunks.Sort( ( a, b ) => Math.Sign( b.Volume - a.Volume ) );

            return true;
        }
    }
}
