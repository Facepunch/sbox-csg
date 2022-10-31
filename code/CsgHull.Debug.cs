using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    partial class CsgHull
    {
        public void DrawGizmos()
        {
            foreach ( var face in _faces )
            {
                face.DrawGizmos( VertexAverage );
            }
        }
        
        public override string ToString()
        {
            return $"[{Index}]";
        }

        partial struct Face
        {
            private static Vector3 DrawFaceGizmos( List<FaceCut> faceCuts, in CsgPlane.Helper helper, float scale, Color color )
            {
                var totalLength = 0f;

                var avgPos = Vector3.Zero;

                faceCuts.Sort( FaceCut.Comparer );

                foreach ( var cut in faceCuts )
                {
                    var min = helper.GetPoint( cut, cut.Min );
                    var max = helper.GetPoint( cut, cut.Max );

                    avgPos += max;

                    DebugOverlay.Line( min, max, color: color );

                    totalLength += ( max - min ).Length;
                }

                avgPos /= faceCuts.Count;

                scale *= MathF.Sqrt( totalLength / 16f );

                var arrowCount = Math.Max( 1, MathF.Floor( totalLength / (16f * scale) ) );
                var arrowGap = totalLength / arrowCount;

                var time = DateTime.UtcNow;

                var t = (-1f + (time.Millisecond / 1000f + time.Second % 2) * 0.5f) * arrowGap;

                foreach ( var cut in faceCuts )
                {
                    var min = helper.GetPoint( cut, cut.Min );
                    var max = helper.GetPoint( cut, cut.Max );
                    
                    var tangent = ( max - min ).Normal;
                    var normal = Vector3.Cross( tangent, helper.Normal );

                    var l = ( max - min ).Length;

                    t += l;

                    while ( t > 0f )
                    {
                        var mid = Vector3.Lerp( min, max, t / l );
                        var size = Math.Clamp( Math.Min( t, l - t ), 0f, 1f ) * scale;

                        DebugOverlay.Line( mid + tangent * size, mid - normal * size, color: color );
                        DebugOverlay.Line( mid, mid - normal * size, color: color );

                        t -= arrowGap;
                    }
                }

                return avgPos;
            }

            public void DrawGizmos( Vector3 vertexAverage )
            {
                var basis = Plane.GetHelper();
                
                DrawFaceGizmos( FaceCuts, basis, 1f, Color.White );

                foreach (var subFace in SubFaces)
                {
                    var avgPos = DrawFaceGizmos( subFace.FaceCuts, basis, 0.5f, Color.Green );

                    if ( subFace.Neighbor != null )
                    {
                        DebugOverlay.Line( avgPos, subFace.Neighbor.VertexAverage, color: subFace.Neighbor.IsEmpty ? Color.Red : Color.Yellow );
                    }
                }
            }

            public void DrawDebug( Color color )
            {
                var basis = Plane.GetHelper();

                foreach (var cut in FaceCuts)
                {
                    cut.DrawDebug(basis, color);
                }
            }
        }

        partial struct SubFace
        {
            public void DrawDebug( CsgPlane plane, Color color )
            {
                var basis = plane.GetHelper();

                foreach (var cut in FaceCuts)
                {
                    cut.DrawDebug(basis, color);
                }
            }
        }

        partial struct FaceCut
        {
            public void DrawDebug( CsgPlane plane, Color color )
            {
                DrawDebug(plane.GetHelper(), color);
            }

            public void DrawDebug( in CsgPlane.Helper basis, Color color )
            {
                var min = basis.GetPoint(this, Min);
                var max = basis.GetPoint(this, Max);

                var mid = (min + max) * 0.5f;
                var norm = Normal.x * basis.Tu + Normal.y * basis.Tv;

                DebugOverlay.Line(min, max, color);
                DebugOverlay.Line( mid, mid + norm, color);
            }
        }
    }
}
