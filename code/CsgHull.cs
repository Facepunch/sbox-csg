using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgHull
    {
        private readonly List<Face> _faces = new List<Face>();
        private readonly List<Vector3> _vertices = new List<Vector3>();

        public static int NextIndex { get; set; }

        public int Index { get; }

        public CsgMaterial Material { get; set; }

        internal (int X, int Y, int Z) GridCoord { get; set; }
        internal CsgSolid.GridCell GridCell { get; set; }

        public bool IsEmpty { get; private set; }
        public bool IsFinite => !float.IsPositiveInfinity( Volume );

        public IReadOnlyList<Face> Faces => _faces;

        private bool _vertexPropertiesInvalid = true;

        private Vector3 _vertexAverage;
        private BBox _vertexBBox;
        private float _volume;

        public Vector3 VertexAverage
        {
            get
            {
                UpdateVertexProperties();
                return _vertexAverage;
            }
        }

        public BBox VertexBounds
        {
            get
            {
                UpdateVertexProperties();
                return _vertexBBox;
            }
        }

        public float Volume
        {
            get
            {
                UpdateVertexProperties();
                return _volume;
            }
        }

        public CsgHull()
        {
            Index = NextIndex++;
        }

        public void InvalidateMesh()
        {
            if ( GridCell != null ) GridCell.MeshInvalid = true;
        }
        
        public CsgHull Clone()
        {
            var copy = new CsgHull
            {
                Material = Material,
                IsEmpty = IsEmpty
            };

            foreach ( var face in _faces )
            {
                copy._faces.Add( face.Clone() );
            }

            copy.InvalidateMesh();
            copy.InvalidateCollision();

            return copy;
        }

        private static bool HasSeparatingFace( List<Face> faces, List<Vector3> verts )
        {
            foreach ( var face in faces )
            {
                var anyPositive = false;

                foreach ( var vertex in verts )
                {
                    if ( face.Plane.GetSign( vertex ) >= 0 )
                    {
                        anyPositive = true;
                        break;
                    }
                }

                if ( !anyPositive ) return true;
            }

            return false;
        }

        public bool IsTouching( CsgHull other )
        {
            other.UpdateVertexProperties();

            if ( HasSeparatingFace( _faces, other._vertices ) )
            {
                return false;
            }

            UpdateVertexProperties();

            return !HasSeparatingFace( other._faces, _vertices );
        }

        public int GetSign( Vector3 pos )
        {
            if ( IsEmpty ) return -1;

            var sign = 1;

            foreach ( var face in _faces )
            {
                sign = Math.Min( sign, face.Plane.GetSign( pos ) );

                if ( sign == -1 ) break;
            }

            return sign;
        }

        public bool TryGetFace( CsgPlane plane, out Face face )
        {
            foreach ( var candidate in _faces )
            {
                if ( candidate.Plane.ApproxEquals( plane ) )
                {
                    face = candidate;
                    return true;
                }
            }

            face = default;
            return false;
        }

        private void UpdateVertexProperties()
        {
            if ( !_vertexPropertiesInvalid ) return;

            _vertexPropertiesInvalid = false;
            _vertices.Clear();

            if ( IsEmpty )
            {
                _vertexAverage = Vector3.Zero;
                _vertexBBox = default;
                _volume = 0f;
                return;
            }

            var min = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
            var max = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );
            var avgPos = Vector3.Zero;
            var posCount = 0;

            const float volumeScale = 1.638716e-5f;

            foreach ( var face in _faces )
            {
                var basis = face.Plane.GetHelper();

                foreach ( var cut in face.FaceCuts )
                {
                    if ( float.IsNegativeInfinity( cut.Min ) || float.IsPositiveInfinity( cut.Max ) )
                    {
                        _volume = float.PositiveInfinity;
                        _vertexBBox = new BBox( float.NegativeInfinity, float.PositiveInfinity );
                        _vertexAverage = 0f;
                        return;
                    }

                    var a = basis.GetPoint( cut, cut.Min );

                    min = Vector3.Min( min, a );
                    max = Vector3.Max( max, a );

                    avgPos += a;
                    posCount += 1;

                    _vertices.Add( a );
                }
            }

            _vertexAverage = posCount == 0 ? Vector3.Zero : avgPos / posCount;
            _vertexBBox = new BBox( min, max );

            var volume = 0f;

            foreach ( var face in _faces )
            {
                if ( face.FaceCuts.Count < 3 ) continue;

                var basis = face.Plane.GetHelper();

                var a = basis.GetPoint( face.FaceCuts[0], face.FaceCuts[0].Max ) - _vertexAverage;
                var b = basis.GetPoint( face.FaceCuts[1], face.FaceCuts[1].Max ) - _vertexAverage;

                for ( var i = 2; i < face.FaceCuts.Count; ++i )
                {
                    var c = basis.GetPoint( face.FaceCuts[i], face.FaceCuts[i].Max ) - _vertexAverage;

                    volume += Math.Abs( Vector3.Dot( a, Vector3.Cross( b, c ) ) );

                    b = c;
                }
            }

            _volume = volume * volumeScale / 6f;
        }

        ~CsgHull()
        {
            if ( Collider.IsValid() )
            {
                Log.Warning( "Collider not disposed!" );
            }
        }
    }
}
