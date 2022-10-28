using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Csg
{
	public enum CsgTextureMode
	{
		Default,
		Triplanar,
		Radial
	}

	[GameResource( "CSG Material", "csgmat", "Volumetric material used by a CSGSolid.", Icon = "image" )]
	public class CsgMaterial : GameResource
	{
		public CsgTextureMode TextureMode { get; set; }

		[ResourceType( "vmat" ), ShowIf( nameof(TextureMode), CsgTextureMode.Default )]
		public string Material { get; set; }

		[ResourceType( "vtex" ), ShowIf( nameof(TextureMode), CsgTextureMode.Triplanar )]
		public string TextureX { get; set; }

		[ResourceType( "vtex" ), ShowIf( nameof(TextureMode), CsgTextureMode.Triplanar )]
		public string TextureY { get; set; }

		[ResourceType( "vtex" ), ShowIf( nameof(TextureMode), CsgTextureMode.Triplanar )]
		public string TextureZ { get; set; }

		private Material _runtimeMaterial;

		[HideInEditor]
		public Material RuntimeMaterial
		{
			get => string.IsNullOrEmpty( Material ) ? null : _runtimeMaterial ??= Sandbox.Material.Load( Material );
		}
	}
}
