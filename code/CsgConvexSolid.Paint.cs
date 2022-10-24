using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Csg
{
    partial class CsgConvexSolid
    {
        private bool ShouldPaintSubFace( SubFace subFace, int? paintMaterialIndex )
        {
            return subFace.Neighbor == null && (subFace.MaterialIndex ?? MaterialIndex) != (paintMaterialIndex ?? MaterialIndex);
        }

        public void Paint( CsgConvexSolid brush, int? materialIndex )
        {
            var paintCuts = CsgHelpers.RentFaceCutList();
            var negCuts = CsgHelpers.RentFaceCutList();

            try
            {
                foreach ( var face in _faces )
                {
                    var anyToPaint = false;

                    foreach ( var subFace in face.SubFaces )
                    {
                        if ( ShouldPaintSubFace( subFace, materialIndex ) )
                        {
                            anyToPaint = true;
                            break;
                        }
                    }

                    if ( !anyToPaint )
                    {
                        continue;
                    }

                    var helper = face.Plane.GetHelper();

                    paintCuts.Clear();
                    paintCuts.AddRange( face.FaceCuts );

                    foreach ( var brushFace in brush.Faces )
                    {
                        paintCuts.Split( helper.GetCut( brushFace.Plane ) );
                    }
                    
                    if ( paintCuts.IsDegenerate() ) continue;
                    if ( brush.GetSign( helper.GetAveragePos( paintCuts ) ) < 0 ) continue;

                    var avgPos = paintCuts.GetAveragePos();

                    if ( !face.FaceCuts.Contains( avgPos ) ) continue;

                    for ( var i = face.SubFaces.Count - 1; i >= 0; i-- )
                    {
                        var subFace = face.SubFaces[i];

                        if ( !ShouldPaintSubFace( subFace, materialIndex ) ) continue;

                        foreach ( var brushFace in brush.Faces )
                        {
                            var cut = helper.GetCut( brushFace.Plane );

                            if ( paintCuts.Count > 0 && !paintCuts.Contains( cut ) ) continue;

                            if ( !subFace.FaceCuts.Split( cut, negCuts ) )
                            {
                                continue;
                            }

                            face.SubFaces.Add( new SubFace
                            {
                                FaceCuts = new List<FaceCut>( negCuts ),
                                MaterialIndex = subFace.MaterialIndex
                            } );
                        }

                        if ( brush.GetSign( helper.GetAveragePos( subFace.FaceCuts ) ) < 0 )
                        {
                            continue;
                        }

                        subFace.MaterialIndex = materialIndex;
                        face.SubFaces[i] = subFace;
                    }
                }
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( paintCuts );
                CsgHelpers.ReturnFaceCutList( negCuts );
            }
        }
    }
}
