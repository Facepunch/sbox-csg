using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    partial class CsgAsset
    {
        public void UpdatePrism( IList<Vector2> baseVertices, Vector3 extrude )
        {
            Assert.True( baseVertices.Count >= 3 );

            var baseNormal = new Vector3( 0f, 0f, 1f );

            if ( Vector3.Dot( extrude, baseNormal ) < 0f )
            {
                baseNormal = -baseNormal;
            }

            var basePlane = new CsgPlane( baseNormal, baseVertices[0] );
            var extrudePlane = new CsgPlane( -baseNormal, (Vector3) baseVertices[0] + extrude );

            var polygonVertices = baseVertices.ToList();
            var polygonVertCounts = new List<int>();

            CsgHelpers.MakeConvex( polygonVertices, polygonVertCounts );

            var offset = 0;

            CompiledSolids ??= new List<ConvexSolid>();
            CompiledSolids.Clear();

            CompiledMins = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
            CompiledMaxs = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );

            foreach ( var count in polygonVertCounts )
            {
                var solid = new ConvexSolid
                {
                    Planes = new List<Plane>( count + 2 ),
                    Mins = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity ),
                    Maxs = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity )
                };

                solid.Planes.Add( basePlane );
                solid.Planes.Add( extrudePlane );

                var prev = (Vector3) polygonVertices[offset + count - 1];

                for ( var i = 0; i < count; i++ )
                {
                    var next = (Vector3) polygonVertices[offset + i];
                    var top = next + extrude;

                    solid.Mins = Vector3.Min( solid.Mins, Vector3.Min( next, top ) );
                    solid.Maxs = Vector3.Max( solid.Maxs, Vector3.Max( next, top ) );

                    var faceNormal = Vector3.Cross( next - prev, extrude ).Normal;

                    solid.Planes.Add( new Plane { Normal = faceNormal, Distance = Vector3.Dot( faceNormal, next ) } );

                    prev = next;
                }

                CompiledSolids.Add( solid );

                CompiledMins = Vector3.Min( CompiledMins, solid.Mins );
                CompiledMaxs = Vector3.Max( CompiledMaxs, solid.Maxs );

                offset += count;
            }

            InvalidateGeometry();
        }
    }
}
