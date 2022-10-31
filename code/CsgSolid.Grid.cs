using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    partial class CsgSolid
    {
        [Net]
        public Vector3 GridSize { get; set; }

        private void SubdivideGridAxis( Vector3 axis, List<CsgConvexSolid> polys )
        {
            var gridSize = Vector3.Dot( axis, GridSize );

            if ( gridSize <= 0f ) return;

            for ( var i = polys.Count - 1; i >= 0; i-- )
            {
                var poly = polys[i];

                var min = Vector3.Dot( poly.VertexMin, axis );
                var max = Vector3.Dot( poly.VertexMax, axis );

                if ( max - min <= gridSize ) continue;

                var minGrid = (int) MathF.Floor( min / gridSize ) + 1;
                var maxGrid = (int) MathF.Ceiling( max / gridSize ) - 1;

                for ( var grid = minGrid; grid <= maxGrid; grid++ )
                {
                    var plane = new CsgPlane( axis, grid * gridSize );
                    var child = poly.Split( plane );
                    
                    if ( child != null )
                    {
                        polys.Add( child );
                    }
                }
            }
        }
    }
}
