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

    partial class CsgSolid
    {
        private record struct Modification( int Brush, int Material, CsgOperator Operator, Matrix Transform );

        private int _appliedModifications;
        private bool _connectivityInvalid;

        [Net, Change, HideInEditor]
        private IList<Modification> Modifications { get; set; }

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

            while ( _appliedModifications < Modifications.Count )
            {
                var next = Modifications[_appliedModifications++];
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

            var mod = new Modification( brush?.ResourceId ?? 0, material?.ResourceId ?? 0, op, transform * WorldToLocal );
            
            if ( Modify( mod, brush, material ) )
            {
                Modifications.Add( mod );
                return true;
            }

            return false;
        }

        private bool Modify( in Modification modification, CsgBrush brush, CsgMaterial material )
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

                if ( changed && modification.Operator is CsgOperator.Add or CsgOperator.Subtract )
                {
                    _connectivityInvalid = true;
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
            var bounds = solid.VertexBounds;

            var nearbyHulls = CsgHelpers.RentHullList();
            var addedHulls = CsgHelpers.RentHullList();
            var removedHulls = CsgHelpers.RentHullList();

            var changed = false;

            try
            {
                GetHullsTouching( bounds, nearbyHulls );

                foreach ( var next in nearbyHulls )
                {
                    var skip = false;

                    switch ( op )
                    {
                        case CsgOperator.Replace:
                            changed |= next.Paint( solid, null );
                            skip = next.Material == solid.Material;
                            break;

                        case CsgOperator.Paint:
                            changed |= next.Paint( solid, solid.Material );
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
                                changed |= ConnectFaces( solidFace, solid, nextFace, next );
                                break;
                            }
                            break;
                    }

                    if ( skip ) continue;

                    for ( var faceIndex = 0; faceIndex < faces.Count && !next.IsEmpty; ++faceIndex )
                    {
                        var face = faces[faceIndex];
                        var child = next.Split( face.Plane, face.FaceCuts );

                        if ( child == null )
                        {
                            continue;
                        }

                        changed = true;

                        if ( child.Faces.Count < 4 )
                        {
                            child.SetEmpty( null );
                        }
                        else if ( !child.IsEmpty )
                        {
                            addedHulls.Add( child );
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

                    if ( solid.GetSign( next.VertexAverage ) < 0 ) continue;

                    // next will now contain only the intersection with solid.
                    // We'll copy its faces and remove it

                    switch ( op )
                    {
                        case CsgOperator.Replace:
                            changed = true;

                            next.Material = solid.Material;
                            next.InvalidateMesh();
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
            }
            finally
            {
                CsgHelpers.Return( nearbyHulls );
                CsgHelpers.Return( addedHulls );
                CsgHelpers.Return( removedHulls );
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
