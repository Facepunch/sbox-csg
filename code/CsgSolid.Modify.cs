using System;
using System.Collections.Generic;

namespace Sandbox.Csg
{
    public enum CsgOperator
    {
        Add,
        Subtract,
        Replace,
        Paint,
		Disconnect
    }

    partial class CsgSolid
    {
        public record struct Modification( int Brush, int Material, CsgOperator Operator, Matrix Transform );

        private int _appliedModifications;

        [Net, Change, HideInEditor]
        public IList<Modification> Modifications { get; set; }

        [ThreadStatic]
        private static List<CsgConvexSolid> _sModifySolids;

        private static Matrix CreateMatrix( Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            var transform = Matrix.Identity;

            if ( position != null )
            {
                transform = Matrix.CreateTranslation( position.Value );
            }

            if ( scale != null )
            {
                transform = Matrix.CreateScale( scale.Value ) * transform;
            }

            if ( rotation != null )
            {
                transform = Matrix.CreateRotation( rotation.Value ) * transform;
            }
            
            return transform;
        }

        public bool Add( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Add, CreateMatrix( position, scale, rotation ) );
        }

        public bool Subtract( CsgBrush brush,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            return Modify( brush, null, CsgOperator.Subtract, CreateMatrix( position, scale, rotation ) );
        }

        public bool Replace( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Replace, CreateMatrix( position, scale, rotation ) );
        }

        public bool Paint( CsgBrush brush, CsgMaterial material,
            Vector3? position = null, Vector3? scale = null, Rotation? rotation = null )
        {
            Assert.NotNull( material );

            return Modify( brush, material, CsgOperator.Paint, CreateMatrix( position, scale, rotation ) );
        }

        public bool Disconnect()
		{
			return Modify( null, null, CsgOperator.Disconnect, default );
		}

        private void OnModificationsChanged()
        {
            if ( ServerDisconnectedFrom != null && !_copiedInitialGeometry )
            {
                return;
            }

            while ( _appliedModifications < Modifications.Count )
            {
                var next = Modifications[_appliedModifications++];
                var brush = next.Brush == 0 ? null : ResourceLibrary.Get<CsgBrush>( next.Brush );
                var material = next.Material == 0 ? null : ResourceLibrary.Get<CsgMaterial>( next.Material );

                Modify( next, brush, material );
            }
        }

        private Matrix WorldToLocal => Matrix.CreateTranslation( -Position )
            * Matrix.CreateScale( 1f / Scale )
            * Matrix.CreateRotation( Rotation.Inverse );

        private bool Modify( CsgBrush brush, CsgMaterial material, CsgOperator op, in Matrix transform )
        {
            Host.AssertServer( nameof(Modify) );

            var mod = new Modification( brush?.ResourceId ?? 0, material?.ResourceId ?? 0, op, transform * WorldToLocal );
			
            if ( Modify( mod, brush, material ) )
			{
				Modifications.Add( mod );
				return true;
			}

            return false;
        }

        private bool Modify( in Modification modification, CsgBrush brush, CsgMaterial material )
        {
	        if ( modification.Operator == CsgOperator.Disconnect )
			{
				return ConnectivityUpdate();
			}

            _sModifySolids ??= new List<CsgConvexSolid>();
            _sModifySolids.Clear();

            brush.CreateSolids( _sModifySolids );

            var changed = false;

            if ( modification.Operator == CsgOperator.Add )
            {
                SubdivideGridAxis( new Vector3( 1f, 0f, 0f ), _sModifySolids );
                SubdivideGridAxis( new Vector3( 0f, 1f, 0f ), _sModifySolids );
                SubdivideGridAxis( new Vector3( 0f, 0f, 1f ), _sModifySolids );
            }

            foreach ( var solid in _sModifySolids )
            {
                solid.Material = material;
                solid.Transform( modification.Transform );
                changed |= Modify( solid, modification.Operator );
            }

            return changed;
        }

        private bool Modify( CsgConvexSolid solid, CsgOperator op )
        {
            var renderMeshChanged = false;
            var collisionChanged = false;

            if ( solid.IsEmpty ) return false;

            var faces = solid.Faces;

            var min = solid.VertexMin - CsgHelpers.DistanceEpsilon;
            var max = solid.VertexMax + CsgHelpers.DistanceEpsilon;
            
            for ( var polyIndex = _polyhedra.Count - 1; polyIndex >= 0; --polyIndex )
            {
                var next = _polyhedra[polyIndex];

                if ( next.IsEmpty )
                {
                    _polyhedra.RemoveAt( polyIndex );
                    continue;
                }

                var nextMin = next.VertexMin;
                var nextMax = next.VertexMax;

                if ( nextMin.x > max.x || nextMin.y > max.y || nextMin.z > max.z ) continue;
                if ( nextMax.x < min.x || nextMax.y < min.y || nextMax.z < min.z ) continue;

                var skip = false;

                switch ( op )
                {
                    case CsgOperator.Replace:
                        next.Paint( solid, null );
                        skip = next.Material == solid.Material;
                        break;

                    case CsgOperator.Paint:
	                    renderMeshChanged |= next.Paint( solid, solid.Material );
                        skip = true;
                        break;

                    case CsgOperator.Add:
                        for ( var faceIndex = 0; faceIndex < faces.Count; ++faceIndex )
                        {
                            var solidFace = faces[faceIndex];

                            if ( !next.TryGetFace( -solidFace.Plane, out var nextFace ) )
                            {
                                continue;
                            }

                            skip = true;

                            if ( ConnectFaces( solidFace, solid, nextFace, next ) )
                            {
                                renderMeshChanged = true;
                            }

                            break;
                        }
                        break;
                }

                if ( skip ) continue;

                for ( var faceIndex = 0; faceIndex < faces.Count && !next.IsEmpty; ++faceIndex )
                {
                    var face = faces[faceIndex];
                    var child = next.Split( face.Plane, face.FaceCuts );

                    if ( child == null )
                    {
                        continue;
                    }

                    renderMeshChanged = true;
                    collisionChanged = true;

                    if ( child.Faces.Count < 4 )
                    {
                        child.Remove( null );
                    }
                    else
                    {
                        _polyhedra.Add( child );
                    }

                    if ( next.Faces.Count < 4 )
                    {
                        next.Remove( null );
                    }
                }

                if ( !next.IsEmpty && solid.GetSign( next.VertexAverage ) < 0 ) continue;
                
                // next will now contain only the intersection with solid.
                // We'll copy its faces and remove it

                switch ( op )
                {
                    case CsgOperator.Replace:
                        next.Material = solid.Material;

                        renderMeshChanged = true;
                        break;

                    case CsgOperator.Add:
                        _polyhedra.RemoveAt( polyIndex );

                        solid.MergeSubFacesFrom( next );
                        next.Remove( null );

                        renderMeshChanged = true;
                        collisionChanged = true;
                        break;

                    case CsgOperator.Subtract:
                        _polyhedra.RemoveAt( polyIndex );

                        next.Remove( null );

                        renderMeshChanged = true;
                        collisionChanged = true;
                        break;
                }
            }

            switch ( op )
            {
                case CsgOperator.Add:
                    solid.InvalidateCollider();
                    _polyhedra.Add( solid );
                    renderMeshChanged = true;
                    collisionChanged = true;
                    break;
            }

            _meshInvalid |= renderMeshChanged;
            _collisionInvalid |= collisionChanged;
            _connectivityInvalid |= collisionChanged;

            return renderMeshChanged;
        }

        private static bool ConnectFaces( CsgConvexSolid.Face faceA, CsgConvexSolid solidA, CsgConvexSolid.Face faceB, CsgConvexSolid solidB )
        {
            var intersectionCuts = CsgHelpers.RentFaceCutList();

            var faceAHelper = faceA.Plane.GetHelper();
            var faceBHelper = faceB.Plane.GetHelper();
            
            try
            {
                intersectionCuts.AddRange( faceA.FaceCuts );

                foreach ( var faceCut in faceB.FaceCuts )
                {
                    intersectionCuts.Split( -faceBHelper.Transform( faceCut, faceAHelper ) );
                }

                if ( intersectionCuts.IsDegenerate() || solidB.GetSign( faceAHelper.GetAveragePos( intersectionCuts ) ) < 0 )
                {
                    return false;
                }

                faceA.RemoveSubFacesInside( intersectionCuts );
                faceA.SubFaces.Add( new CsgConvexSolid.SubFace
                {
                    FaceCuts = new List<CsgConvexSolid.FaceCut>( intersectionCuts ),
                    Neighbor = solidB
                } );

                for ( var i = 0; i < intersectionCuts.Count; i++ )
                {
                    intersectionCuts[i] = -faceAHelper.Transform( intersectionCuts[i], faceBHelper );
                }

                faceB.RemoveSubFacesInside( intersectionCuts );
                faceB.SubFaces.Add( new CsgConvexSolid.SubFace
                {
                    FaceCuts = new List<CsgConvexSolid.FaceCut>( intersectionCuts ),
                    Neighbor = solidA
                } );

                return true;
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( intersectionCuts );
            }
        }
    }
}
