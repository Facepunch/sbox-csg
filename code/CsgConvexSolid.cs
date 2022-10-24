using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public partial class CsgConvexSolid
    {
        private readonly List<Face> _faces = new List<Face>();

        public static int NextIndex { get; set; }

        public int Index { get; }

        public int MaterialIndex { get; set; }

        public bool IsEmpty { get; private set; }

        public IReadOnlyList<Face> Faces => _faces;

        private bool _vertexPropertiesInvalid = true;

        private Vector3 _vertexAverage;
        private Vector3 _vertexMin;
        private Vector3 _vertexMax;
        private float _volume;

        public Vector3 VertexAverage
        {
            get
            {
                UpdateVertexProperties();
                return _vertexAverage;
            }
        }

        public Vector3 VertexMin
        {
            get
            {
                UpdateVertexProperties();
                return _vertexMin;
            }
        }

        public Vector3 VertexMax
        {
            get
            {
                UpdateVertexProperties();
                return _vertexMax;
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

        public CsgConvexSolid()
        {
            Index = NextIndex++;
        }

        public void InvalidateMesh()
        {
            _vertexPropertiesInvalid = true;

            InvalidateCollider();
        }

        partial void InvalidateCollider();

        public CsgConvexSolid Clone()
        {
            var copy = new CsgConvexSolid
            {
                MaterialIndex = MaterialIndex,
                IsEmpty = IsEmpty
            };

            foreach ( var face in _faces )
            {
                _faces.Add( face.Clone() );
            }

            return copy;
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

            if ( IsEmpty )
            {
                _vertexAverage = Vector3.Zero;
                _vertexMin = Vector3.Zero;
                _vertexMax = Vector3.Zero;
                _volume = 0f;
                return;
            }

            var min = new Vector3( float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity );
            var max = new Vector3( float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity );
            var avgPos = Vector3.Zero;
            var posCount = 0;

            foreach ( var face in _faces )
            {
                var basis = face.Plane.GetHelper();

                foreach ( var cut in face.FaceCuts )
                {
                    var a = basis.GetPoint( cut, cut.Min );
                    var b = basis.GetPoint( cut, cut.Max );

                    min = Vector3.Min( min, a );
                    max = Vector3.Max( max, a );

                    min = Vector3.Min( min, b );
                    max = Vector3.Max( max, b );

                    avgPos += a + b;
                    posCount += 2;
                }
            }

            _vertexAverage = posCount == 0 ? Vector3.Zero : avgPos / posCount;
            _vertexMin = min;
            _vertexMax = max;

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

                    volume += Vector3.Dot( a, Vector3.Cross( b, c ) );

                    b = c;
                }
            }

            _volume = volume / 6f;
        }
    }
}
