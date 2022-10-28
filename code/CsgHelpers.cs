using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Csg
{
    internal static class CsgHelpers
    {
        public const float UnitEpsilon = 9.5367431640625E-7f; // 0x35800000
        public const float DistanceEpsilon = 0.00390625f; // 0x3b800000

        [ThreadStatic]
        private static List<List<CsgConvexSolid.FaceCut>> _sFaceCutListPool;

        private const int FaceCutPoolCapacity = 8;

        public static List<CsgConvexSolid.FaceCut> RentFaceCutList()
        {
            if ( _sFaceCutListPool == null )
            {
                _sFaceCutListPool = new List<List<CsgConvexSolid.FaceCut>>(
                    Enumerable.Range( 0, FaceCutPoolCapacity ).Select( x => new List<CsgConvexSolid.FaceCut>() ) );
            }

            if ( _sFaceCutListPool.Count == 0 )
            {
                Log.Warning( "Face cut list pool is empty!" );
                _sFaceCutListPool.Add( new List<CsgConvexSolid.FaceCut>() );
            }

            var list = _sFaceCutListPool[_sFaceCutListPool.Count - 1];
            _sFaceCutListPool.RemoveAt( _sFaceCutListPool.Count - 1 );

            list.Clear();

            return list;
        }

        public static void ReturnFaceCutList( List<CsgConvexSolid.FaceCut> list )
        {
            if ( _sFaceCutListPool.Count >= FaceCutPoolCapacity ) return;

            _sFaceCutListPool.Add( list );
        }

        public static Vector3 GetTangent( this Vector3 normal )
        {
            var absX = Math.Abs(normal.x);
            var absY = Math.Abs(normal.y);
            var absZ = Math.Abs(normal.z);

            return Vector3.Cross(normal, absX <= absY && absX <= absZ
                ? new Vector3(1f, 0f, 0f) : absY <= absZ
                    ? new Vector3(0f, 1f, 0f)
                    : new Vector3(0f, 0f, 1f));
        }

        private static int NextPowerOfTwo( int value )
        {
            var po2 = 1;
            while ( po2 < value ) po2 <<= 1;
            return po2;
        }

        public static void EnsureCapacity<T>( ref T[] array, int minSize )
            where T : struct
        {
            if (array != null && array.Length >= minSize) return;

            var oldArray = array;

            array = new T[NextPowerOfTwo(minSize)];

            if (oldArray != null)
            {
                Array.Copy(oldArray, 0, array, 0, oldArray.Length);
            }
        }

        public static float Cross( Vector2 a, Vector2 b )
        {
            return a.x * b.y - a.y * b.x;
        }

        public static void Flip( this List<CsgConvexSolid.FaceCut> faceCuts,
            in CsgPlane.Helper oldHelper, in CsgPlane.Helper newHelper )
        {
            for ( var i = 0; i < faceCuts.Count; i++ )
            {
                faceCuts[i] = -oldHelper.Transform( faceCuts[i], newHelper );
            }
        }

        public static bool IsDegenerate( this List<CsgConvexSolid.FaceCut> faceCuts )
        {
            if ( faceCuts == null )
            {
                return false;
            }

            foreach ( var cut in faceCuts )
            {
                if ( float.IsInfinity( cut.Min ) ) return false;
                if ( float.IsInfinity( cut.Max ) ) return false;
            }

            return faceCuts.Count < 3;
        }

        public static bool Contains( this List<CsgConvexSolid.FaceCut> faceCuts, Vector2 pos )
        {
            foreach ( var faceCut in faceCuts )
            {
                if ( Dot( faceCut.Normal, pos ) - faceCut.Distance < -DistanceEpsilon )
                {
                    return false;
                }
            }

            return true;
        }

        public static Vector2 GetAveragePos( this List<CsgConvexSolid.FaceCut> faceCuts )
        {
            if ( faceCuts.Count == 0 )
            {
                return Vector2.Zero;
            }

            var avgPos = Vector2.Zero;

            foreach ( var faceCut in faceCuts )
            {
                avgPos += faceCut.GetPos( faceCut.Mid );
            }

            return avgPos / faceCuts.Count;
        }

        public static float Dot( Vector2 a, Vector2 b )
        {
            return a.x * b.x + a.y * b.y;
        }

        public static bool Split( this List<CsgConvexSolid.FaceCut> faceCuts, CsgConvexSolid.FaceCut splitCut, List<CsgConvexSolid.FaceCut> outNegative = null )
        {
            outNegative?.Clear();

            if ( splitCut.ExcludesNone || splitCut.ExcludesAll )
            {
                return false;
            }

            var outPositive = RentFaceCutList();

            var newCut = new CsgConvexSolid.FaceCut( splitCut.Normal, splitCut.Distance,
                float.NegativeInfinity, float.PositiveInfinity );

            try
            {
                foreach ( var faceCut in faceCuts )
                {
                    var cross = Cross( splitCut.Normal, faceCut.Normal );
                    var dot = Dot( splitCut.Normal, faceCut.Normal );

                    if ( Math.Abs( cross ) <= UnitEpsilon )
                    {
                        // Edge case: parallel cuts

                        if ( faceCut.Distance * dot - splitCut.Distance < DistanceEpsilon )
                        {
                            // splitCut is pointing away from faceCut,
                            // so faceCut is negative

                            if ( dot < 0f && splitCut.Distance * dot - faceCut.Distance < DistanceEpsilon )
                            {
                                // faceCut is also pointing away from splitCut,
                                // so the whole face must be negative

                                outNegative?.Clear();
                                return false;
                            }

                            outNegative?.Add( faceCut );
                            continue;
                        }

                        if ( splitCut.Distance * dot - faceCut.Distance < DistanceEpsilon )
                        {
                            // faceCut is pointing away from splitCut,
                            // so splitCut is redundant

                            outNegative?.Clear();
                            return false;
                        }

                        // Otherwise the two cuts are pointing towards each other

                        outPositive.Add( faceCut );
                        continue;
                    }

                    // Not parallel, so check for intersection

                    var proj0 = (faceCut.Distance - splitCut.Distance * dot) / cross;
                    var proj1 = (splitCut.Distance - faceCut.Distance * dot) / -cross;

                    var posFaceCut = faceCut;
                    var negFaceCut = faceCut;

                    if ( cross > 0f )
                    {
                        splitCut.Min = Math.Max( splitCut.Min, proj0 );
                        newCut.Min = Math.Max( newCut.Min, proj0 );
                        posFaceCut.Max = Math.Min( faceCut.Max, proj1 );
                        negFaceCut.Min = Math.Max( faceCut.Min, proj1 );
                    }
                    else
                    {
                        splitCut.Max = Math.Min( splitCut.Max, proj0 );
                        newCut.Max = Math.Min( newCut.Max, proj0 );
                        posFaceCut.Min = Math.Max( faceCut.Min, proj1 );
                        negFaceCut.Max = Math.Min( faceCut.Max, proj1 );
                    }

                    if ( splitCut.Max - splitCut.Min < DistanceEpsilon )
                    {
                        // splitCut must be fully outside the face

                        outNegative?.Clear();
                        return false;
                    }

                    if ( posFaceCut.Max - posFaceCut.Min >= DistanceEpsilon )
                    {
                        outPositive.Add( posFaceCut );
                    }

                    if ( negFaceCut.Max - negFaceCut.Min >= DistanceEpsilon )
                    {
                        outNegative?.Add( negFaceCut );
                    }
                }

                outPositive.Add( newCut );
                outNegative?.Add( -newCut );

                if ( outPositive.IsDegenerate() || outNegative.IsDegenerate() )
                {
                    outNegative?.Clear();
                    return false;
                }

                faceCuts.Clear();
                faceCuts.AddRange( outPositive );

                return true;
            }
            finally
            {
                ReturnFaceCutList( outPositive );
            }
        }
    }
}
