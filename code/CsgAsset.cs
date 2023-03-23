using System;
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
        Asset,

        [Icon("pentagon")]
        Prism
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
        private bool _assetInvalid = true;

        [HideInEditor, JsonIgnore]
        public CsgAsset Asset
        {
            get
            {
                if ( _assetInvalid || _asset == null )
                {
                    UpdateAsset();
                }

                return _asset;
            }
        }

        public BrushOperator Operator { get; set; }

        public Vector3 Position { get; set; }
        public Angles Angles { get; set; }

        [HideInEditor]
        public Vector3 Scale { get; set; } = new Vector3( 1f, 1f, 1f );

        /// <summary>
        /// Only used by <see cref="BrushGeometryKind.Prism"/>
        /// </summary>
        [HideInEditor]
        public List<Vector2> BaseVertices { get; set; }

        /// <summary>
        /// Only used by <see cref="BrushGeometryKind.Prism"/>
        /// </summary>
        [HideInEditor]
        public Vector3 Extrusion { get; set; }

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

        public void InvalidateAsset()
        {
            _assetInvalid = true;
        }

        public void UpdateAsset()
        {
            _assetInvalid = false;
            _asset ??= GeometryKind switch
            {
                BrushGeometryKind.Cube => CsgAsset.Cube,
                BrushGeometryKind.Asset => string.IsNullOrEmpty( AssetPath )
                    ? CsgAsset.Empty
                    : ResourceLibrary.Get<CsgAsset>( AssetPath ),
                BrushGeometryKind.Prism => new CsgAsset(),
                _ => throw new NotImplementedException()
            };

            if ( GeometryKind == BrushGeometryKind.Prism )
            {
                _asset.UpdatePrism( BaseVertices, Extrusion );
            }
        }

        public CsgBrush Copy()
        {
            return new CsgBrush
            {
                GeometryKind = GeometryKind,
                AssetPath = AssetPath,
                Operator = Operator,

                BaseVertices = GeometryKind == BrushGeometryKind.Prism ? BaseVertices.ToList() : null,
                Extrusion = GeometryKind == BrushGeometryKind.Prism ? Extrusion : default,

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
                    },
                    Mins = -64f,
                    Maxs = 64f
                }
            },
            CompiledMins = -64f,
            CompiledMaxs = 64f
        };

        private static CsgMaterial _defaultMaterial;

        public static CsgMaterial DefaultMaterial => _defaultMaterial ??= ResourceLibrary.Get<CsgMaterial>( "materials/csgeditor/default.csgmat" );

        public struct ConvexSolid
        {
            public CsgMaterial Material { get; set; }
            public List<Plane> Planes { get; set; }
            public Vector3 Mins { get; set; }
            public Vector3 Maxs { get; set; }

            [JsonIgnore]
            public BBox Bounds => new BBox( Mins, Maxs );
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
        public Vector3 CompiledMins { get; set; }
        [HideInEditor]
        public Vector3 CompiledMaxs { get; set; }

        [JsonIgnore]
        public BBox CompiledBounds => new BBox( CompiledMins, CompiledMaxs );

        [HideInEditor]
        public int EditCount { get; set; }

        [HideInEditor, JsonIgnore]
        public Model Model
        {
            get
            {
                UpdateModel( ref _model, ref _modelInvalid, _modelMeshes, false );
                return _model;
            }
        }

        [HideInEditor, JsonIgnore]
        public Model Wireframe
        {
            get
            {
                UpdateModel( ref _wireframe, ref _wireframeInvalid, _wireframeMeshes, true );
                return _wireframe;
            }
        }

        private bool _hullsInvalid = true;
        private bool _modelInvalid = true;
        private bool _wireframeInvalid = true;

        private readonly List<CsgHull> _hulls = new List<CsgHull>();
        private readonly Dictionary<int, Mesh> _modelMeshes = new Dictionary<int, Mesh>();
        private readonly Dictionary<int, Mesh> _wireframeMeshes = new Dictionary<int, Mesh>();

        private Model _model;
        private Model _wireframe;

        public void InvalidateGeometry()
        {
            _hullsInvalid = true;
            _modelInvalid = true;
            _wireframeInvalid = true;
        }

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
            if ( !_hullsInvalid ) return;

            _hulls.Clear();
            _hullsInvalid = false;

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

        private void UpdateModel( ref Model model, ref bool invalid, Dictionary<int, Mesh> meshes, bool wireframe )
        {
            if ( model != null && !invalid ) return;

            invalid = false;

            UpdateHulls();

            if ( !CsgSceneObject.UpdateMeshes( meshes, _hulls, wireframe ) && model != null ) return;

            var modelBuilder = new ModelBuilder();

            foreach ( var (_, mesh) in meshes )
            {
                modelBuilder.AddMesh( mesh );
            }

            model = modelBuilder.Create();
        }

        protected override void PostLoad()
        {
            base.PostLoad();

            InvalidateGeometry();
        }

        protected override void PostReload()
        {
            base.PostReload();

            InvalidateGeometry();
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
