using System.Collections.Generic;

namespace Sandbox.Csg
{
    partial class CsgConvexSolid
    {
        /// <summary>
        /// Split this solid by the given plane, returning the negative side.
        /// </summary>
        /// <param name="plane">Plane to split by.</param>
        /// <param name="faceCuts">Optional constraints on the split plane.</param>
        /// <returns>If a split occurred, returns a new solid with the removed faces. Otherwise null.</returns>
        public CsgConvexSolid Split( CsgPlane plane, List<FaceCut> faceCuts = null )
        {
            return Split( plane, faceCuts, false ).NegativeSolid;
        }

        /// <summary>
        /// Split this solid by the given plane, discarding the negative side.
        /// </summary>
        /// <param name="plane">Plane to clip by.</param>
        /// <param name="faceCuts">Optional constraints on the clip plane.</param>
        /// <returns>Returns true if a clip occurred.</returns>
        public bool Clip( CsgPlane plane, List<FaceCut> faceCuts = null )
        {
            return Split( plane, faceCuts, true ).Changed;
        }
        
        private (bool Changed, CsgConvexSolid NegativeSolid) Split( CsgPlane cutPlane, List<FaceCut> faceCuts, bool discard )
        {
            if ( IsEmpty ) return (false, null);

            var splitPlaneHelper = cutPlane.GetHelper();
            
            var splitFaceCuts = CsgHelpers.RentFaceCutList();
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                splitFaceCuts.Clear();

                if ( faceCuts != null )
                {
                    splitFaceCuts.AddRange( faceCuts );
                }

                // Cut down split face to see if there is any intersection

                foreach ( var face in _faces )
                {
                    if ( face.Plane.ApproxEquals( cutPlane ) || face.Plane.ApproxEquals( -cutPlane ) )
                    {
                        return (false, null);
                    }

                    splitFaceCuts.Split( splitPlaneHelper.GetCut( face.Plane ) );
                }

                if ( faceCuts != null && splitFaceCuts.IsDegenerate() )
                {
                    return (false, null);
                }

                if ( GetSign( splitPlaneHelper.GetAveragePos( splitFaceCuts ) ) < 0 )
                {
                    return (false, null);
                }

                // If we survived that, the cut plane must intersect this solid

                var negSolid = discard
                    ? null
                    : new CsgConvexSolid
                    {
                        Material = Material
                    };

                var posSubFace = new SubFace
                {
                    FaceCuts = new List<FaceCut>( splitFaceCuts ),
                    Neighbor = negSolid
                };

                if ( faceCuts != null )
                {
                    splitFaceCuts.Clear();
                }

                for ( var i = _faces.Count - 1; i >= 0; i-- )
                {
                    var face = _faces[i];

                    if ( faceCuts != null )
                    {
                        // Cut unbounded split plane to find shared split face.
                        // If faceCuts == null, we've already found it (posSubFace will be the whole face)
                        
                        splitFaceCuts.Split( splitPlaneHelper.GetCut( face.Plane ) );
                    }

                    // Cut original face

                    var facePlaneHelper = face.Plane.GetHelper();
                    var facePlaneCut = facePlaneHelper.GetCut( cutPlane );

                    if ( !face.FaceCuts.Split( facePlaneCut, negCuts ) )
                    {
                        // Face isn't split by cut plane, so check which side it's on

                        if ( cutPlane.GetSign( facePlaneHelper.GetAveragePos( face.FaceCuts ) ) >= 0 )
                        {
                            continue;
                        }

                        // Negative side

                        _faces.RemoveAt( i );
                        negSolid?._faces.Add( face );

                        foreach ( var subFace in face.SubFaces )
                        {
                            subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid );
                        }

                        continue;
                    }
                    
                    // Otherwise split face in two

                    var posFace = face with { SubFaces = new List<SubFace>() };

                    var negFace = discard
                        ? (Face?)null
                        : new Face
                        {
                            Plane = face.Plane,
                            FaceCuts = new List<FaceCut>( negCuts ),
                            SubFaces = new List<SubFace>()
                        };

                    _faces[i] = posFace;
                    negSolid?._faces.Add( negFace.Value );

                    foreach ( var subFace in face.SubFaces )
                    {
                        if ( !subFace.FaceCuts.Split( facePlaneCut, negCuts ) )
                        {
                            // Face isn't split by cut plane, so check which side it's on

                            if ( cutPlane.GetSign( facePlaneHelper.GetAveragePos( subFace.FaceCuts ) ) >= 0 )
                            {
                                posFace.SubFaces.Add( subFace );
                                continue;
                            }

                            negFace?.SubFaces.Add( subFace );

                            subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid, -cutPlane );
                            continue;
                        }
                        
                        // Otherwise split sub-face in two

                        posFace.SubFaces.Add( subFace );

                        subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, negSolid, -cutPlane );

                        negFace?.SubFaces.Add( subFace with { FaceCuts = new List<FaceCut>( negCuts ) } );
                    }
                }

                var posSplitFace = new Face
                {
                    Plane = cutPlane,
                    FaceCuts = new List<FaceCut>( splitFaceCuts )
                };

                posSplitFace.SubFaces = new List<SubFace>
                {
                    new SubFace
                    {
                        FaceCuts = new List<FaceCut>( posSplitFace.FaceCuts ),
                        Neighbor = negSolid
                    }
                };

                if ( faceCuts != null )
                {
                    // If cut was already constrained, add the constrained sub-face

                    posSplitFace.RemoveSubFacesInside( posSubFace.FaceCuts );
                    posSplitFace.SubFaces.Add( posSubFace );
                }

                _faces.Add( posSplitFace );
                negSolid?._faces.Add( posSplitFace.CloneFlipped( this ) );

                InvalidateMesh();
                negSolid?.InvalidateMesh();

                return (true, negSolid);
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( splitFaceCuts );
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }

        public void Remove( CsgConvexSolid replacement )
        {
            foreach ( var face in _faces )
            {
                foreach ( var subFace in face.SubFaces )
                {
                    subFace.Neighbor?.ReplaceNeighbor( -face.Plane, this, replacement );
                }
            }

            _faces.Clear();
            IsEmpty = true;

            InvalidateMesh();
            RemoveCollider();
        }

        private void ReplaceNeighbor( CsgPlane plane, CsgConvexSolid oldNeighbor, CsgConvexSolid newNeighbor )
        {
            if ( !TryGetFace( plane, out var face ) ) return;

            for ( var i = 0; i < face.SubFaces.Count; ++i )
            {
                var subFace = face.SubFaces[i];

                if ( subFace.Neighbor != oldNeighbor ) continue;
                
                subFace.Neighbor = newNeighbor;
                face.SubFaces[i] = subFace;
            }
        }

        private void ReplaceNeighbor( CsgPlane plane, CsgConvexSolid oldNeighbor, CsgConvexSolid newNeighbor, CsgPlane cutPlane )
        {
            if ( !TryGetFace( plane, out var face ) ) return;
            
            var helper = plane.GetHelper();

            var faceCut = helper.GetCut( cutPlane );
            
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                for ( var i = face.SubFaces.Count - 1; i >= 0; --i )
                {
                    var subFace = face.SubFaces[i];

                    if ( subFace.Neighbor != oldNeighbor ) continue;

                    if ( subFace.FaceCuts.Split( faceCut, negCuts ) )
                    {
                        face.SubFaces.Add( subFace with { FaceCuts = new List<FaceCut>( negCuts ) } );
                    }
                    else if ( cutPlane.GetSign( helper.GetAveragePos( subFace.FaceCuts ) ) < 0 )
                    {
                        continue;
                    }
                    
                    subFace.Neighbor = newNeighbor;
                    face.SubFaces[i] = subFace;
                }
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }
        
        /// <summary>
        /// Merge sub-faces from another solid.
        /// </summary>
        public void MergeSubFacesFrom( CsgConvexSolid other )
        {
            if ( other.IsEmpty || IsEmpty ) return;

            foreach ( var thisFace in _faces )
            {
                if ( !other.TryGetFace( thisFace.Plane, out var otherFace ) )
                {
                    continue;
                }
                
                thisFace.RemoveSubFacesInside( otherFace.FaceCuts );

                // Now just add the sub-faces from other

                foreach ( var subFace in otherFace.SubFaces )
                {
                    thisFace.SubFaces.Add( subFace.Clone() );

                    subFace.Neighbor?.ReplaceNeighbor( -thisFace.Plane, other, this );
                }
            }
        }
    }
}
