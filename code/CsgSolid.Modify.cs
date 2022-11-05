using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public enum CsgOperator
    {
        Add,
        Subtract,
        Replace,
        Paint,
        Disconnect
    }

    internal record struct CsgModification( int Brush, int Material, CsgOperator Operator, Matrix Transform );
    
    partial class CsgSolid
    {
        private const int MaxModificationsPerMessage = 8;
        private const int SendModificationsRpc = 269924031; // CsgSolid.SendModifications

        private int _appliedModifications;
        private readonly List<CsgModification> _modifications = new List<CsgModification>();
        private readonly Dictionary<Entity, int> _sentModifications = new Dictionary<Entity, int>();

        [ThreadStatic]
        private static List<Entity> _sToRemove;

        private void AddModification( in CsgModification modification )
        {
            _modifications.Add( modification );
        }

        private void SendModifications()
        {
            _sToRemove ??= new List<Entity>();
            _sToRemove.Clear();

            foreach ( var (pawn, _) in _sentModifications )
            {
                if ( !pawn.IsValid )
                {
                    _sToRemove.Add( pawn );
                }
            }

            foreach ( var entity in _sToRemove )
            {
                _sentModifications.Remove( entity );
            }

            foreach ( var client in Client.All )
            {
                if ( client.IsBot ) continue;
                if ( client.Pawn is null ) continue;

                if ( !_sentModifications.TryGetValue( client.Pawn, out var prevCount ) )
                {
                    prevCount = 0;
                    _sentModifications.Add( client.Pawn, prevCount );
                }

                Assert.True( prevCount <= _modifications.Count );

                if ( prevCount == _modifications.Count ) continue;

                var msg = NetWrite.StartRpc( SendModificationsRpc, this );

                var msgCount = Math.Min( _modifications.Count - prevCount, MaxModificationsPerMessage );

                msg.Write( prevCount );
                msg.Write( msgCount );
                msg.Write( _modifications.Count );

                for ( var i = 0; i < msgCount; i++ )
                {
                    msg.Write( _modifications[prevCount + i] );
                }

                msg.SendRpc( To.Single( client ), null );

                _sentModifications[client.Pawn] = prevCount + msgCount;
            }
        }

        private void ReceiveModifications( NetRead read )
        {
            var prevCount = read.Read<int>();
            var msgCount = read.Read<int>();
            var totalCount = read.Read<int>();

            Assert.AreEqual( prevCount, _modifications.Count );

            for ( var i = 0; i < msgCount; ++i )
            {
                _modifications.Add( read.Read<CsgModification>() );
            }

            OnModificationsChanged();
        }

        protected override void OnCallRemoteProcedure( int id, NetRead read )
        {
            switch ( id )
            {
                case SendModificationsRpc:
                    ReceiveModifications( read );
                    break;

                default:
                    base.OnCallRemoteProcedure( id, read );
                    break;
            }
        }

        private static Matrix CreateMatrix( Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            var transform = Matrix.Identity;

            if ( position != null )
            {
                transform = Matrix.CreateTranslation( position.Value );
            }

            if ( scale != null )
            {
                transform = Matrix.CreateScale( scale.Value ) * transform;
            }

            if ( rotation != null )
            {
                transform = Matrix.CreateRotation( rotation.Value ) * transform;
            }
            
            return transform;
        }

        public bool Add( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Add, CreateMatrix( position, scale, rotation ) );
        }

        public bool Subtract( CsgBrush brush,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            return Modify( brush, null, CsgOperator.Subtract, CreateMatrix( position, scale, rotation ) );
        }

        public bool Replace( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Replace, CreateMatrix( position, scale, rotation ) );
        }

        public bool Paint( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Paint, CreateMatrix( position, scale, rotation ) );
        }

        private bool Disconnect()
        {
            return Modify( null, null, CsgOperator.Disconnect, default );
        }

        private void OnModificationsChanged()
        {
            if ( _grid == null ) return;

            if ( ServerDisconnectedFrom != null && !_copiedInitialGeometry )
            {
                return;
            }

            while ( _appliedModifications < _modifications.Count )
            {
                var next = _modifications[_appliedModifications++];
                var brush = next.Brush == 0 ? null : ResourceLibrary.Get<CsgBrush>( next.Brush );
                var material = next.Material == 0 ? null : ResourceLibrary.Get<CsgMaterial>( next.Material );

                Modify( next, brush, material );
            }
        }

        private Matrix WorldToLocal => Matrix.CreateTranslation( -Position )
            * Matrix.CreateScale( 1f / Scale )
            * Matrix.CreateRotation( Rotation.Inverse );

        private bool Modify( CsgBrush brush, CsgMaterial material, CsgOperator op, in Matrix transform )
        {
            Host.AssertServer( nameof(Modify) );

            var mod = new CsgModification( brush?.ResourceId ?? 0, material?.ResourceId ?? 0, op, transform * WorldToLocal );
            
            if ( Modify( mod, brush, material ) )
            {
                AddModification( mod );
                return true;
            }

            return false;
        }

        private bool Modify( in CsgModification modification, CsgBrush brush, CsgMaterial material )
        {
            if ( Deleted )
            {
                if ( IsServer )
                {
                    Log.Warning( $"Attempting to modify a deleted {nameof(CsgSolid)}" );
                }

                return false;
            }

            Timer.Restart();

            var hulls = CsgHelpers.RentHullList();

            try
            {
                if ( modification.Operator == CsgOperator.Disconnect )
                {
                    return ConnectivityUpdate();
                }

                brush.CreateHulls( hulls );

                var changed = false;

                foreach ( var solid in hulls )
                {
                    solid.Material = material;
                    solid.Transform( modification.Transform );
                }

                foreach ( var solid in hulls )
                {
                    changed |= Modify( solid, modification.Operator );
                }

                return changed;
            }
            finally
            {
                CsgHelpers.Return( hulls );

                if ( LogTimings )
                {
                    Log.Info( $"{Host.Name} Modify {modification.Operator}: {Timer.Elapsed.TotalMilliseconds:F2}ms" );
                }
            }
        }

        private bool Modify( CsgHull solid, CsgOperator op )
        {
            if ( solid.IsEmpty ) return false;

            var faces = solid.Faces;

            var nearbyHulls = CsgHelpers.RentHullList();
            var addedHulls = CsgHelpers.RentHullList();
            var removedHulls = CsgHelpers.RentHullList();
            var changedHulls = CsgHelpers.RentHullSet();

            var changed = false;

            try
            {
                var elapsedBefore = Timer.Elapsed;

                GetHullsTouching( solid, nearbyHulls );

                if ( LogTimings )
                {
                    Log.Info( $"{Host.Name} GetHullsTouching: {(Timer.Elapsed - elapsedBefore).TotalMilliseconds:F2}ms" );
                }

                foreach ( var next in nearbyHulls )
                {
                    var skip = false;
                    var nextChanged = false;

                    switch ( op )
                    {
                        case CsgOperator.Replace:
                            changed |= nextChanged = next.Paint( solid, null );
                            skip = next.Material == solid.Material;
                            break;

                        case CsgOperator.Paint:
                            changed |= nextChanged = next.Paint( solid, solid.Material );
                            skip = true;
                            break;

                        case CsgOperator.Add:
                            for ( var faceIndex = 0; faceIndex < faces.Count; ++faceIndex )
                            {
                                var solidFace = faces[faceIndex];

                                if ( !next.TryGetFace( -solidFace.Plane, out var nextFace ) )
                                {
                                    continue;
                                }

                                skip = true;

                                if ( ConnectFaces( solidFace, solid, nextFace, next ) )
                                {
                                    changed = nextChanged = true;
                                }
                                break;
                            }
                            break;
                    }

                    if ( skip )
                    {
                        if ( nextChanged )
                        {
                            changedHulls.Add( next );
                        }

                        continue;
                    }

                    for ( var faceIndex = 0; faceIndex < faces.Count && !next.IsEmpty; ++faceIndex )
                    {
                        var face = faces[faceIndex];
                        var child = next.Split( face.Plane, face.FaceCuts );

                        if ( child == null )
                        {
                            continue;
                        }

                        changed = nextChanged = true;

                        if ( child.Faces.Count < 4 )
                        {
                            child.SetEmpty( null );
                        }
                        else if ( !child.IsEmpty )
                        {
                            addedHulls.Add( child );
                            changedHulls.Add( child );
                        }

                        if ( next.Faces.Count < 4 )
                        {
                            next.SetEmpty( null );
                        }
                    }

                    if ( next.IsEmpty )
                    {
                        changed = true;

                        removedHulls.Add( next );
                        continue;
                    }

                    if ( solid.GetSign( next.VertexAverage ) < 0 )
                    {
                        if ( nextChanged )
                        {
                            changedHulls.Add( next );
                        }

                        continue;
                    }

                    // next will now contain only the intersection with solid.
                    // We'll copy its faces and remove it

                    switch ( op )
                    {
                        case CsgOperator.Replace:
                            changed = true;

                            next.Material = solid.Material;
                            next.InvalidateMesh();

                            changedHulls.Add( next );
                            break;

                        case CsgOperator.Add:
                            changed = true;

                            removedHulls.Add( next );

                            solid.MergeSubFacesFrom( next );
                            next.SetEmpty( null );
                            break;

                        case CsgOperator.Subtract:
                            changed = true;

                            removedHulls.Add( next );

                            next.SetEmpty( null );
                            break;
                    }
                }

                switch ( op )
                {
                    case CsgOperator.Add:
                        changed = true;

                        solid.RemoveCollider();

                        addedHulls.Add( solid );
                        changedHulls.Add( solid );
                        break;
                }

                foreach ( var hull in removedHulls )
                {
                    RemoveHull( hull );
                }

                foreach ( var hull in addedHulls )
                {
                    AddHull( hull );
                }

                // Try to merge adjacent hulls / sub faces

                var hullMergeCount = 0;
                var subFaceMergeCount = 0;

                var elapsed = Timer.Elapsed;

                foreach ( var hull in changedHulls )
                {
                    if ( hull.IsEmpty ) continue;
                    
                    if ( op != CsgOperator.Paint )
                    {
                        bool merged;

                        do
                        {
                            merged = false;

                            nearbyHulls.Clear();

                            hull.GetNeighbors( nearbyHulls );

                            foreach ( var neighbor in nearbyHulls )
                            {
                                Assert.NotNull( neighbor.GridCell );

                                if ( hull.TryMerge( neighbor ) )
                                {
                                    ++hullMergeCount;

                                    RemoveHull( neighbor );

                                    merged = true;
                                    break;
                                }
                            }
                        } while ( merged );
                    }

                    subFaceMergeCount += hull.MergeSubFaces();
                }

                if ( hullMergeCount + subFaceMergeCount > 0 && LogTimings )
                {
                    Log.Info( $"{Host.Name} Merged {hullMergeCount} hulls, {subFaceMergeCount} sub faces in {(Timer.Elapsed - elapsed).TotalMilliseconds:F2}ms" );
                }
            }
            finally
            {
                CsgHelpers.Return( nearbyHulls );
                CsgHelpers.Return( addedHulls );
                CsgHelpers.Return( removedHulls );
                CsgHelpers.Return( changedHulls );
            }

            return changed;
        }

        private static bool ConnectFaces( CsgHull.Face faceA, CsgHull solidA, CsgHull.Face faceB, CsgHull solidB )
        {
            var intersectionCuts = CsgHelpers.RentFaceCutList();

            var faceAHelper = faceA.Plane.GetHelper();
            var faceBHelper = faceB.Plane.GetHelper();
            
            try
            {
                intersectionCuts.AddRange( faceA.FaceCuts );

                foreach ( var faceCut in faceB.FaceCuts )
                {
                    intersectionCuts.Split( -faceBHelper.Transform( faceCut, faceAHelper ) );
                }

                if ( intersectionCuts.IsDegenerate() || solidB.GetSign( faceAHelper.GetAveragePos( intersectionCuts ) ) < 0 )
                {
                    return false;
                }

                faceA.RemoveSubFacesInside( intersectionCuts );
                faceA.SubFaces.Add( new CsgHull.SubFace
                {
                    FaceCuts = new List<CsgHull.FaceCut>( intersectionCuts ),
                    Neighbor = solidB
                } );

                for ( var i = 0; i < intersectionCuts.Count; i++ )
                {
                    intersectionCuts[i] = -faceAHelper.Transform( intersectionCuts[i], faceBHelper );
                }

                faceB.RemoveSubFacesInside( intersectionCuts );
                faceB.SubFaces.Add( new CsgHull.SubFace
                {
                    FaceCuts = new List<CsgHull.FaceCut>( intersectionCuts ),
                    Neighbor = solidA
                } );

                solidA.InvalidateMesh();
                solidB.InvalidateMesh();

                return true;
            }
            finally
            {
                CsgHelpers.Return( intersectionCuts );
            }
        }
    }
}
