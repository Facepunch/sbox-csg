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

        private List<CsgHull> _hulls;

        public int CreateHulls( List<CsgHull> outHulls )
        {
            UpdateSolids();

            foreach ( var hull in _hulls )
            {
                outHulls.Add( hull.Clone() );
            }

            return _hulls.Count;
        }

        private void UpdateSolids()
        {
            if ( _hulls != null ) return;

            _hulls = new List<CsgHull>();

            if ( ConvexSolids == null ) return;

            foreach ( var solidInfo in ConvexSolids )
            {
                var hull = new CsgHull();

                if ( solidInfo.Planes != null )
                {
                    foreach ( var plane in solidInfo.Planes )
                    {
                        hull.Clip( plane );
                    }
                }

                if ( hull.IsEmpty )
                {
                    continue;
                }

                if ( !hull.IsFinite )
                {
                    Log.Warning( "Incomplete convex solid" );
                    continue;
                }

                _hulls.Add( hull );
            }
        }

        protected override void PostLoad()
        {
            base.PostLoad();

            _hulls = null;
        }

        protected override void PostReload()
        {
            base.PostReload();

            _hulls = null;
        }
    }
}
