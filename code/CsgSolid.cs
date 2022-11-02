using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgSolid : ModelEntity
    {
        public const bool LogTimings = true;

        public CsgSolid()
        {

        }

        public CsgSolid( Vector3 gridSize )
        {
            GridSize = gridSize;

            SetupContainers( GridSize );
        }

        public override void Spawn()
        {
            base.Spawn();

            Transmit = TransmitType.Always;
        }

        public override void ClientSpawn()
        {
            base.ClientSpawn();

            SetupContainers( GridSize );
        }

        [Event.Tick.Server]
        private void ServerTick()
        {
            if ( _connectivityInvalid )
            {
                _connectivityInvalid = false;

                Disconnect();
            }

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

        private void Clear( bool removeColliders )
        {
            if ( removeColliders )
            {
                foreach ( var pair in _grid )
                {
                    foreach ( var hull in pair.Value.Hulls )
                    {
                        hull.RemoveCollider();

                        hull.Island = null;
                        hull.GridCell = null;
                        hull.GridCoord = default;
                    }
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
                _grid[gridCoord] = cell = new GridCell { Solid = this };
            }

            hull.GridCoord = gridCoord;
            hull.GridCell = cell;
            hull.Island = null;

            cell.Hulls.Add( hull );
            cell.CollisionInvalid = true;
            cell.MeshInvalid = true;
            hull.GridCell.ConnectivityInvalid = true;
        }

        private void RemoveHull( CsgHull hull )
        {
            if ( hull.GridCell?.Solid != this )
            {
                throw new Exception( "Hull isn't owned by this solid" );
            }

            Assert.True( hull.GridCell.Hulls.Remove( hull ) );

            hull.RemoveCollider();

            hull.GridCell.CollisionInvalid = true;
            hull.GridCell.MeshInvalid = true;
            hull.GridCell.ConnectivityInvalid = true;

            hull.GridCell = null;
            hull.GridCoord = default;
            hull.Island = null;
        }
    }
}
