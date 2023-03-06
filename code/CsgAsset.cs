﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Sandbox.Diagnostics;

namespace Sandbox.Csg
{
    public enum BrushGeometryKind
    {
        [Icon("view_in_ar")]
        Cube,

        [Icon("extension")]
        Asset
    }

    public enum BrushOperator
    {
        [Icon("add")]
        Add,

        [Icon("remove")]
        Subtract
    }

    public class CsgBrush
    {
        [HideInEditor]
        public BrushGeometryKind GeometryKind { get; set; }

        [ShowIf( nameof( GeometryKind ), BrushGeometryKind.Asset ), ResourceType( "csg" )]
        public string AssetPath { get; set; }

        private CsgAsset _asset;

        [HideInEditor, JsonIgnore]
        public CsgAsset Asset => _asset ??= GeometryKind switch
        {
            BrushGeometryKind.Cube => CsgAsset.Cube,
            BrushGeometryKind.Asset => string.IsNullOrEmpty( AssetPath )
                ? CsgAsset.Empty
                : ResourceLibrary.Get<CsgAsset>( AssetPath ),
            _ => throw new NotImplementedException()
        };

        public BrushOperator Operator { get; set; }

        public Vector3 Position { get; set; }
        public Angles Angles { get; set; }

        [HideInEditor]
        public Vector3 Scale { get; set; } = new Vector3( 1f, 1f, 1f );

        [JsonIgnore]
        public Vector3 Size
        {
            get => Scale * Asset.CompiledBounds.Size;
            set
            {
                var assetSize = Asset.CompiledBounds.Size;

                if ( assetSize.x <= CsgHelpers.DistanceEpsilon ) assetSize.x = 1f;
                if ( assetSize.y <= CsgHelpers.DistanceEpsilon ) assetSize.y = 1f;
                if ( assetSize.z <= CsgHelpers.DistanceEpsilon ) assetSize.y = 1f;

                Scale = value * new Vector3( 1f / assetSize.x, 1f / assetSize.y, 1f / assetSize.z );
            }
        }

        public CsgBrush Copy()
        {
            return new CsgBrush
            {
                GeometryKind = GeometryKind,
                AssetPath = AssetPath,
                Operator = Operator,

                Position = Position,
                Angles = Angles,
                Scale = Scale
            };
        }
    }

    [GameResource("CSG Asset", "csg", "A simple mesh that can be used to modify a CsgSolid.", Icon = "brush")]
    public partial class CsgAsset : GameResource
    {
        public static CsgAsset Empty { get; } = new();

        public static CsgAsset Cube { get; } = new ()
        {
            CompiledSolids = new List<ConvexSolid>
            {
                new ()
                {
                    Planes = new List<Plane>
                    {
                        new ()
                        {
                            Normal = new Vector3( 1f, 0f, 0f ),
                            Distance = -64f
                        },
                        new ()
                        {
                            Normal = new Vector3( -1f, 0f, 0f ),
                            Distance = -64f
                        },
                        new ()
                        {
                            Normal = new Vector3( 0f, 1f, 0f ),
                            Distance = -64f
                        },
                        new ()
                        {
                            Normal = new Vector3( 0f, -1f, 0f ),
                            Distance = -64f
                        },
                        new ()
                        {
                            Normal = new Vector3( 0f, 0f, 1f ),
                            Distance = -64f
                        },
                        new ()
                        {
                            Normal = new Vector3( 0f, 0f, -1f ),
                            Distance = -64f
                        }
                    }
                }
            },
            CompiledBounds = new BBox( new Vector3( 0f, 0f, 0f ), 128f )
        };

        private static CsgMaterial _defaultMaterial;

        public static CsgMaterial DefaultMaterial => _defaultMaterial ??= ResourceLibrary.Get<CsgMaterial>( "materials/csgeditor/default.csgmat" );

        public struct ConvexSolid
        {
            public CsgMaterial Material { get; set; }
            public List<Plane> Planes { get; set; }
        }

        public struct Plane
        {
            public static implicit operator CsgPlane( Plane plane ) =>
                new CsgPlane( plane.Normal.Normal, plane.Distance );

            public static implicit operator Plane( CsgPlane plane ) =>
                new Plane { Normal = plane.Normal, Distance = plane.Distance };

            public Vector3 Normal { get; set; }
            public float Distance { get; set; }
        }

        private static Angles DefaultEditorCameraAngles { get; } = new Angles( 30f, 240f, 0f );

        [HideInEditor]
        public List<CsgBrush> Brushes { get; set; }

        [HideInEditor]
        public Vector3 EditorCameraPos { get; set; } = -Rotation.From( DefaultEditorCameraAngles ).Forward * 256f;

        [HideInEditor]
        public Angles EditorCameraAngles { get; set; } = DefaultEditorCameraAngles;

        [HideInEditor]
        public List<ConvexSolid> CompiledSolids { get; set; }

        [HideInEditor]
        public BBox CompiledBounds { get; set; }

        [HideInEditor]
        public int EditCount { get; set; }

        [HideInEditor, JsonIgnore]
        public Model Model
        {
            get
            {
                UpdateModel( ref _model, false );
                return _model;
            }
        }

        [HideInEditor, JsonIgnore]
        public Model Wireframe
        {
            get
            {
                UpdateModel( ref _wireframe, true );
                return _wireframe;
            }
        }

        private List<CsgHull> _hulls;
        private Model _model;
        private Model _wireframe;

        public int CreateHulls( List<CsgHull> outHulls )
        {
            UpdateHulls();

            foreach ( var hull in _hulls )
            {
                outHulls.Add( hull.Clone() );
            }

            return _hulls.Count;
        }

        private void UpdateHulls()
        {
            if ( _hulls != null ) return;

            _hulls = new List<CsgHull>();

            if ( CompiledSolids == null ) return;

            foreach ( var solidInfo in CompiledSolids )
            {
                var hull = new CsgHull { Material = solidInfo.Material ?? DefaultMaterial };

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

        private void UpdateModel( ref Model model, bool wireframe )
        {
            if ( model != null ) return;

            UpdateHulls();

            var meshes = new Dictionary<int, Mesh>();

            var modelBuilder = new ModelBuilder();

            if ( CsgSceneObject.UpdateMeshes( meshes, _hulls, wireframe ) )
            {
                foreach ( var (_, mesh) in meshes )
                {
                    modelBuilder.AddMesh( mesh );
                }
            }

            model = modelBuilder.Create();
        }

        protected override void PostLoad()
        {
            base.PostLoad();

            _hulls = null;
            _model = null;
            _wireframe = null;
        }

        protected override void PostReload()
        {
            base.PostReload();

            _hulls = null;
            _model = null;
            _wireframe = null;
        }

#if !SANDBOX_EDITOR
        public static CsgBrush Deserialize( ref NetRead reader )
        {
            var resourceId = reader.Read<int>();

            if ( resourceId != 0 )
            {
                var brush = ResourceLibrary.Get<CsgBrush>( resourceId );

                Assert.NotNull( brush );

                return brush;
            }

            var solidCount = reader.Read<int>();
            var solids = new List<ConvexSolid>( solidCount );

            for ( var i = 0; i < solidCount; i++ )
            {
                var planeCount = reader.Read<int>();

                var solid = new ConvexSolid
                {
                    Planes = new List<Plane>( planeCount )
                };

                solids.Add( solid );

                for ( var j = 0; j < planeCount; j++ )
                {
                    solid.Planes.Add( reader.Read<Plane>() );
                }
            }

            return new CsgBrush
            {
                ConvexSolids = solids
            };
        }

        public void Serialize( NetWrite writer )
        {
            writer.Write( ResourceId );

            if ( ResourceId != 0 )
            {
                return;
            }

            writer.Write( ConvexSolids.Count );

            foreach ( var solid in ConvexSolids )
            {
                writer.Write( solid.Planes.Count );

                foreach ( var plane in solid.Planes )
                {
                    writer.Write( plane );
                }
            }
        }
#endif
    }
}
