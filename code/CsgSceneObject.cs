using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    public partial class CsgSceneObject : SceneCustomObject, IDisposable
    {
        public CsgSceneObject( SceneWorld world, Vector3 gridSize )
            : base( world )
        {
            GridSize = gridSize;

            SetupContainers( GridSize );
        }

        public void Dispose()
        {
            Clear();
        }

        public BBox CalculateBounds()
        {
            var mins = new Vector3( float.PositiveInfinity );
            var maxs = new Vector3( float.NegativeInfinity );

            foreach ( var (_, cell) in _grid )
            {
                foreach ( var hull in cell.Hulls )
                {
                    var bounds = hull.VertexBounds;

                    mins = Vector3.Min( mins, bounds.Mins );
                    maxs = Vector3.Max( maxs, bounds.Maxs );
                }
            }

            return float.IsPositiveInfinity( mins.x )
                ? new BBox( Vector3.Zero )
                : new BBox( mins, maxs );
        }

        internal void Clear()
        {
            foreach ( var (_, cell) in _grid )
            {
                cell.SceneObject?.Delete();
                cell.SceneObject = null;

                foreach ( var hull in cell.Hulls )
                {
                    hull.RemoveCollider();

                    hull.Island = null;
                    hull.GridCell = null;
                    hull.GridCoord = default;
                }
            }

            _grid.Clear();
        }

        public void AddHull( CsgHull hull )
        {
            if ( hull.IsEmpty )
            {
                throw new ArgumentException( "Can't add an empty hull", nameof( hull ) );
            }

            if ( !HasGrid )
            {
                AddHull( hull, default );
                return;
            }

            var bounds = hull.VertexBounds;
            var minCoord = GetGridCoord( bounds.Mins );
            var maxCoord = GetGridCoord( bounds.Maxs );

            if ( minCoord == maxCoord )
            {
                AddHull( hull, minCoord );
                return;
            }

            var toAdd = CsgHelpers.RentHullList();

            try
            {
                toAdd.Add( hull );

                SubdivideGridAxis( new Vector3( 1f, 0f, 0f ), toAdd );
                SubdivideGridAxis( new Vector3( 0f, 1f, 0f ), toAdd );
                SubdivideGridAxis( new Vector3( 0f, 0f, 1f ), toAdd );

                foreach ( var subHull in toAdd )
                {
                    AddHull( subHull, GetGridCoord( subHull.VertexBounds.Center ) );
                }
            }
            finally
            {
                CsgHelpers.Return( toAdd );
            }
        }

        private void AddHull( CsgHull hull, (int X, int Y, int Z) gridCoord )
        {
            if ( hull.GridCell != null )
            {
                throw new Exception( "Hull has already been added to a cell" );
            }

            if ( !_grid.TryGetValue( gridCoord, out var cell ) )
            {
                _grid[gridCoord] = cell = new GridCell( this, gridCoord );
            }

            hull.GridCoord = gridCoord;
            hull.GridCell = cell;
            hull.Island = null;

            cell.Hulls.Add( hull );

            cell.InvalidateCollision();
            cell.InvalidateMesh();
            cell.InvalidateConnectivity();
        }

        private void RemoveHull( CsgHull hull )
        {
            if ( hull.GridCell?.Solid != this )
            {
                throw new Exception( "Hull isn't owned by this solid" );
            }

            var cell = hull.GridCell;

            Assert.True( cell.Hulls.Remove( hull ) );

            hull.RemoveCollider();

            hull.GridCell = null;
            hull.GridCoord = default;
            hull.Island = null;

            cell.InvalidateCollision();
            cell.InvalidateMesh();
            cell.InvalidateConnectivity();
        }
    }
}
