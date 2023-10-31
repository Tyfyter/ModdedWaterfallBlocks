using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Content.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Capture;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ModdedWaterfalls {
	public class ModdedWaterfalls : Mod {
		static ConstructorInfo assetCtor;
		static MethodInfo submitLoadedContent;
		internal static Dictionary<int, WaterfallBlock> blocksByStyle = new();
		internal static Dictionary<int, WaterfallItem> itemsByStyle = new();
		static MiscShaderData shader;
		public static Asset<Texture2D> blockTexture;
		public static Asset<Texture2D> itemTexture;
		public static Dictionary<CaptureBiome, string> captureBiomeNames = new();
		public override void Unload() {
			blocksByStyle = null;
			itemsByStyle = null;
			shader = null;
			blockTexture = null;
			itemTexture = null;
			captureBiomeNames = null;
		}
		public override void Load() {
			assetCtor = typeof(Asset<Texture2D>).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new Type[] { typeof(string) });
			submitLoadedContent = typeof(Asset<Texture2D>).GetMethod("SubmitLoadedContent", BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (FieldInfo field in typeof(CaptureBiome.Styles).GetFields(BindingFlags.Public | BindingFlags.Static)) {
				captureBiomeNames.Add((CaptureBiome)field.GetValue(null), field.Name);
			}
			for (int i = 0; i < TextureAssets.Liquid.Length; i++) {
				if (i is 1 or 11 or 14) continue;
				AddContent(new WaterfallBlock(i));
			}
			WaterStylesLoader stylesLoader = LoaderManager.Get<WaterStylesLoader>();
			int totalCount = (int)typeof(Loader).GetProperty("TotalCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(stylesLoader);
			for (int i = 15; i < totalCount; i++) {
				if (stylesLoader.Get(i).GetType().GetProperty("ModdedWaterfalls_HasOwnWaterfall", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool hasFall && hasFall) continue;
				AddContent(new WaterfallBlock(i));
			}
			if (Main.dedServ) return;
			shader = new MiscShaderData(new Ref<Effect>(Assets.Request<Effect>("Effects/Mapper", AssetRequestMode.ImmediateLoad).Value), "Mapper");
			GameShaders.Misc["WaterfallGenerator"] = shader;
			blockTexture = Assets.Request<Texture2D>("Waterfall_Block_Map");
			itemTexture = Assets.Request<Texture2D>("Waterfall_Item_Map");
		}
		public static Asset<Texture2D> GenerateTexture(string name, Texture2D baseTexture, int frames, Asset<Texture2D> palette) {
			Asset<Texture2D> asset = (Asset<Texture2D>)assetCtor.Invoke(new object[] { name });
			RenderTarget2D target = new(Main.instance.GraphicsDevice, baseTexture.Width, baseTexture.Height);
			shader.UseImage1(palette);
			shader.Apply(null);
			Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, shader.Shader, Matrix.Identity);
			Main.graphics.GraphicsDevice.SetRenderTarget(target);
			Main.graphics.GraphicsDevice.Clear(Color.Transparent);
			Main.spriteBatch.Draw(
				baseTexture,
				Vector2.Zero,
				null,
				Color.White,
				0,
				Vector2.Zero,
				new Vector2(Main.screenWidth / (float)baseTexture.Width, Main.screenHeight / (float)baseTexture.Height),
				SpriteEffects.None,
			0);
			Main.spriteBatch.End();
			Main.graphics.GraphicsDevice.SetRenderTarget(null);
			submitLoadedContent.Invoke(asset, new object[] { (Texture2D)target, null });
			/*using (Stream stream = File.OpenWrite("C:\\Users\\Tyfyter\\Documents\\My Games\\Terraria\\tModLoader\\ModSources\\ModdedWaterfalls\\GenTex.png")) {
				target.SaveAsPng(stream, target.Width, target.Height);
			}*/
			return asset;
		}
		public static string GetStyleName(int style) {
			if (style < 15) {
				if (CaptureBiome.BiomesByWaterStyle[style] is CaptureBiome biome) {
					return captureBiomeNames[biome];
				}
				return style.ToString();
			}
			ModWaterStyle waterStyle = LoaderManager.Get<WaterStylesLoader>().Get(style);
			return waterStyle.Mod.Name + "_" + waterStyle.Name;
		}
	}
	public class ModWaterfallSystem : ModSystem {
		public static RecipeGroup nonVanillaWaterfalls;
		public override void AddRecipeGroups() {
			RecipeGroup.RegisterGroup("NonVanillaWaterfalls",
				nonVanillaWaterfalls = new RecipeGroup(
					() => Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.WaterfallRecipeGroup").Value,
					ModdedWaterfalls.itemsByStyle.Values.First().Type
				)
			);
		}
		public override void AddRecipes() {
			Recipe.Create(ItemID.WaterfallBlock)
			.AddRecipeGroup(nonVanillaWaterfalls)
			.Register();
		}
		public override void Unload() {
			nonVanillaWaterfalls = null;
		}
	}
	[Autoload(false)]
	public class WaterfallBlock : ModTile {
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Block_Map";
		public override string Name => "Waterfall_Block_Style_" + ModdedWaterfalls.GetStyleName(style);
		readonly int style;
		public WaterfallBlock(int style) {
			this.style = style;
			ModdedWaterfalls.blocksByStyle.Add(style, this);
		}
		public override void Load() {
			Mod.AddContent(new WaterfallItem(style));
		}
		public override void SetStaticDefaults() {
			Main.tileSolid[Type] = true;
			Main.tileBlockLight[Type] = true;
			if (Main.dedServ) return;
			TextureAssets.Tile[Type].Wait();
			TextureAssets.Liquid[style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			TextureAssets.Tile[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.blockTexture.Value, 8, TextureAssets.Liquid[style]);
		}
		public override void AnimateIndividualTile(int type, int i, int j, ref int frameXOffset, ref int frameYOffset) {
			frameYOffset = 90 * Main.tileFrame[TileID.Waterfall];
		}
		/*bool lastShimmering = false;
		public override void PostDraw(int i, int j, SpriteBatch spriteBatch) {
			spriteBatch.Draw(TextureAssets.Tile[Type].Value, new Vector2(i, j) * 16 - Main.screenPosition, null, Color.White, 0, Vector2.Zero, 0.5f, SpriteEffects.None, 0);
			if (Main.LocalPlayer.shimmering) {
				if (!lastShimmering) {
					Main.NewText("yeee");
					Main.QueueMainThreadAction(GenerateTexture);
				}
				lastShimmering = true;
			} else {
				lastShimmering = false;
			}
		}*/
	}
	[Autoload(false)]
	public class WaterfallItem : ModItem {
		protected override bool CloneNewInstances => true;
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Item_Map";
		public override string Name => "Waterfall_Item_Style_" + ModdedWaterfalls.GetStyleName(style);
		public override LocalizedText DisplayName => Language.GetOrRegister(
			$"Mods.{nameof(ModdedWaterfalls)}.WaterfallItem_{ModdedWaterfalls.GetStyleName(style)}",
			() => $"{ModdedWaterfalls.GetStyleName(style)} Waterfall Block"
		);
		public override LocalizedText Tooltip => LocalizedText.Empty;
		readonly int style;
		public WaterfallItem(int style) {
			this.style = style;
			ModdedWaterfalls.itemsByStyle.Add(style, this);
		}
		public override void SetStaticDefaults() {
			if (Main.dedServ) return;
			TextureAssets.Item[Type].Wait();
			TextureAssets.Liquid[style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			TextureAssets.Item[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.itemTexture.Value, 1, TextureAssets.Liquid[style]);
		}
		public override void SetDefaults() {
			Item.DefaultToPlaceableTile(ModdedWaterfalls.blocksByStyle[style].Type);
		}
		public override void AddRecipes() {
			ModWaterfallSystem.nonVanillaWaterfalls.ValidItems.Add(Type);
			CreateRecipe()
			.AddIngredient(ItemID.WaterfallBlock)
			.AddCondition(Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.RecipeCondition"), () => Main.waterStyle == style)
			.Register();
		}
		/*bool lastShimmering = false;
		public override void HoldItem(Player player) {
			if (player.shimmering) {
				if (!lastShimmering) {
					Main.NewText("yeee");
					Main.QueueMainThreadAction(GenerateTexture);
				}
				lastShimmering = true;
			} else {
				lastShimmering = false;
			}
		}*/
	}
}