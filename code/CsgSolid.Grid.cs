using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    partial class CsgSolid
    {
        [Net]
        public Vector3 GridSize { get; set; }

        private Vector3 _gridSize;
        private Vector3 _invGridSize;

        public bool HasGrid { get; private set; }

        internal class GridCell
        {
            public List<CsgHull> Hulls { get; } = new List<CsgHull>();
            public Dictionary<int, Mesh> Meshes { get; } = new();

            public SceneObject SceneObject { get; set; }

            public CsgSolid Solid { get; set; }

            public float Volume { get; set; }
            public float Mass { get; set; }

            public bool CollisionInvalid { get; set; }
            public bool MeshInvalid { get; set; }
        }

        private Dictionary<(int X, int Y, int Z), GridCell> _grid;

        private void SetupContainers( Vector3 gridSize )
        {
            if ( _grid != null )
            {
                throw new Exception( "Containers already set up" );
            }

            Log.Info( $"{Host.Name} SetupContainers( {gridSize} )" );

            if ( gridSize.x < 0f || gridSize.y < 0f || gridSize.z < 0f )
            {
                throw new ArgumentException( "Grid size must be non-negative.", nameof(gridSize) );
            }

            _gridSize = gridSize;

            _invGridSize.x = gridSize.x <= 0f ? 0f : 1f / gridSize.x;
            _invGridSize.y = gridSize.y <= 0f ? 0f : 1f / gridSize.y;
            _invGridSize.z = gridSize.z <= 0f ? 0f : 1f / gridSize.z;

            HasGrid = _gridSize != Vector3.Zero;

            _grid = new Dictionary<(int X, int Y, int Z), GridCell>();

            if ( IsClient )
            {
                OnModificationsChanged();
            }
        }

        private void SubdivideGridAxis( Vector3 axis, List<CsgHull> hulls )
        {
            var gridSize = Vector3.Dot( axis, _gridSize );

            if ( gridSize <= 0f ) return;

            for ( var i = hulls.Count - 1; i >= 0; i-- )
            {
                var poly = hulls[i];
                var bounds = poly.VertexBounds;

                var min = Vector3.Dot( bounds.Mins, axis );
                var max = Vector3.Dot( bounds.Maxs, axis );

                var minGrid = (int) MathF.Floor( min / gridSize ) + 1;
                var maxGrid = (int) MathF.Ceiling( max / gridSize ) - 1;

                for ( var grid = minGrid; grid <= maxGrid; grid++ )
                {
                    var plane = new CsgPlane( axis, grid * gridSize );
                    var child = poly.Split( plane );
                    
                    if ( child != null )
                    {
                        hulls.Add( child );
                    }
                }
            }
        }

        private (int X, int Y, int Z) GetGridCoord( Vector3 pos )
        {
            return HasGrid
                ? (
                    (int)MathF.Floor( pos.x * _invGridSize.x ),
                    (int)MathF.Floor( pos.y * _invGridSize.y ),
                    (int)MathF.Floor( pos.z * _invGridSize.z ))
                : default;
        }

        private int GetHullsTouching( BBox bounds, List<CsgHull> outHulls )
        {
            bounds.Mins -= CsgHelpers.DistanceEpsilon;
            bounds.Maxs += CsgHelpers.DistanceEpsilon;

            var insideCount = 0;

            var gridMin = GetGridCoord( bounds.Mins );
            var gridMax = GetGridCoord( bounds.Maxs );

            var cellCount = (gridMax.X - gridMin.X + 1) * (gridMax.Y - gridMin.Y + 1) * (gridMax.Z - gridMin.Z + 1);

            if ( cellCount > _grid.Count )
            {
                foreach ( var pair in _grid )
                {
                    if ( pair.Key.X < gridMin.X || pair.Key.Y < gridMin.Y || pair.Key.Z < gridMin.Z ) continue;
                    if ( pair.Key.X > gridMax.X || pair.Key.Y > gridMax.Y || pair.Key.Z > gridMax.Z ) continue;
                    
                    foreach ( var hull in pair.Value.Hulls )
                    {
                        if ( hull.IsEmpty ) continue;
                        if ( !hull.VertexBounds.Overlaps( bounds ) ) continue;

                        outHulls.Add( hull );
                        ++insideCount;
                    }
                }

                return insideCount;
            }

            for ( var x = gridMin.X; x <= gridMax.X; x++ )
            {
                for ( var y = gridMin.Y; y <= gridMax.Y; y++ )
                {
                    for ( var z = gridMin.Z; z <= gridMax.Z; z++ )
                    {
                        if ( !_grid.TryGetValue( (x, y, z), out var cell ) ) continue;

                        foreach ( var hull in cell.Hulls )
                        {
                            if ( hull.IsEmpty ) continue;
                            if ( !hull.VertexBounds.Overlaps( bounds ) ) continue;

                            outHulls.Add( hull );
                            ++insideCount;
                        }
                    }
                }
            }

            return insideCount;
        }
    }
}
