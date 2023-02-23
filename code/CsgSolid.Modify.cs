using System;
using System.Collections.Generic;
using Sandbox.Diagnostics;

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

    internal record struct CsgModification( CsgOperator Operator, CsgBrush Brush, CsgMaterial Material, Matrix? Transform );
    
    partial class CsgSolid
    {
        private static Matrix? CreateMatrix( Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            if ( position == null && scale == null && rotation == null )
            {
                return null;
            }

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

        public bool Modify( CsgHull solid, CsgOperator op )
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

                solidA.InvalidateNeighbors();
                solidB.InvalidateNeighbors();

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
