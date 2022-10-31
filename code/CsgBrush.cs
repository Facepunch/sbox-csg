using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Csg
{
    [GameResource("CSG Brush", "csg", "A simple mesh that can be used to modify a CsgSolid.", Icon = "brush")]
    public class CsgBrush : GameResource
    {
        public struct ConvexSolid
        {
            public List<Plane> Planes { get; set; }
        }

        public struct Plane
        {
            public static implicit operator CsgPlane( Plane plane ) =>
                new CsgPlane( plane.Normal.Normal, plane.Distance );

            public Vector3 Normal { get; set; }
            public float Distance { get; set; }
        }

        public List<ConvexSolid> ConvexSolids { get; set; }

        private List<CsgHull> _solids;

        public int CreateSolids( List<CsgHull> outSolids )
        {
            UpdateSolids();

            foreach ( var solid in _solids )
            {
                outSolids.Add( solid.Clone() );
            }

            return _solids.Count;
        }

        private void UpdateSolids()
        {
            if ( _solids != null ) return;

            _solids = new List<CsgHull>();

            if ( ConvexSolids == null ) return;

            foreach ( var solidInfo in ConvexSolids )
            {
                var solid = new CsgHull();

                if ( solidInfo.Planes != null )
                {
                    foreach ( var plane in solidInfo.Planes )
                    {
                        solid.Clip( plane );
                    }
                }

                if ( solid.IsEmpty )
                {
                    continue;
                }

                if ( !solid.IsFinite )
                {
                    Log.Warning( "Incomplete convex solid" );
                    continue;
                }

                _solids.Add( solid );
            }
        }

        protected override void PostLoad()
        {
            base.PostLoad();

            _solids = null;
        }

        protected override void PostReload()
        {
            base.PostReload();

            _solids = null;
        }
    }
}
