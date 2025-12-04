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
using Terraria.GameContent.Creative;
using Terraria.Graphics.Capture;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using static Terraria.Graphics.Capture.IL_CaptureBiome.Sets;

namespace ModdedWaterfalls {
	public class ModdedWaterfalls : Mod {
		static ConstructorInfo assetCtor;
		static MethodInfo submitLoadedContent;
		static MiscShaderData shader;
		public static Asset<Texture2D> blockTexture;
		public static Asset<Texture2D> itemTexture;

		public static Asset<Texture2D> wallTexture;
		public static Asset<Texture2D> wallItemTexture;
		public static Dictionary<CaptureBiome, string> captureBiomeNames = new();
		static int styleCount = 15;
		static List<ModWaterStyle> styleInstancesForServers = new(new ModWaterStyle[15]);
		public MethodInfo setStyleMod = typeof(ModType).GetProperty("Mod").SetMethod;
		static Queue<(ModWaterStyle, Mod)> queuedWaterStyles = new();
		public override void Unload() {
			blockTexture = null;
			itemTexture = null;

			wallTexture = null;
			wallItemTexture = null;

			shader = null;
			captureBiomeNames = null;
			styleCount = 0;
			queuedWaterStyles = new();
		}
		public override object Call(params object[] args) {
			queuedWaterStyles.Enqueue(((ModWaterStyle)args[1], (Mod)args[0]));
			return null;
		}
		public override void Load() {
			assetCtor = typeof(Asset<Texture2D>).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(string)]);
			submitLoadedContent = typeof(Asset<Texture2D>).GetMethod("SubmitLoadedContent", BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (FieldInfo field in typeof(CaptureBiome.Styles).GetFields(BindingFlags.Public | BindingFlags.Static)) {
				captureBiomeNames.Add((CaptureBiome)field.GetValue(null), field.Name);
			}
			for (int i = 0; i < 15; i++) {
				if (i is 1 or 11 or 14) continue;
				AddContent(new WaterfallBlock(i, GetStyleName(i)));
				styleCount++;
				static string GetStyleName(int style) {
					if (CaptureBiome.BiomesByWaterStyle[style] is CaptureBiome biome) {
						return captureBiomeNames[biome];
					}
					return style.ToString();
				}
			}
			if (Main.dedServ) {
				foreach (Mod mod in ModLoader.Mods) {
					if (mod.Code is null || !mod.ContentAutoloadingEnabled) continue;
					LoaderUtils.ForEachAndAggregateExceptions(
					(from t in AssemblyManager.GetLoadableTypes(mod.Code)
					where !t.IsAbstract && !t.ContainsGenericParameters
					where t.IsAssignableTo(typeof(ModWaterStyle))
					where t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) != null
					where AutoloadAttribute.GetValue(t).Value
					select t).OrderBy(type => type.FullName, StringComparer.InvariantCulture), t => {
						if (t.GetProperty("ModdedWaterfalls_HasOwnWaterfall", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool hasFall && hasFall) return;
						ModWaterStyle style = (ModWaterStyle)Activator.CreateInstance(t, nonPublic: true);
						setStyleMod.Invoke(style, [mod]);
						styleInstancesForServers.Add(style);
						AddContent(new WaterfallBlock(styleCount, $"{style.Mod.Name}_{style.Name}"));
						styleCount++;
					});
				}
			} else {
				WaterStylesLoader stylesLoader = LoaderManager.Get<WaterStylesLoader>();
				int totalCount = (int)typeof(Loader).GetProperty("TotalCount", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(stylesLoader);
				for (int i = 15; i < totalCount; i++) {
					if (stylesLoader.Get(i) is not ModWaterStyle style) continue;
					if (!AutoloadAttribute.GetValue(style.GetType()).Value) continue;
					if (stylesLoader.Get(i).GetType().GetProperty("ModdedWaterfalls_HasOwnWaterfall", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is bool hasFall && hasFall) continue;
					AddContent(new WaterfallBlock(i, $"{style.Mod.Name}_{style.Name}"));
					styleCount++;
				}
				shader = new MiscShaderData(Assets.Request<Effect>("Effects/Mapper", AssetRequestMode.ImmediateLoad), "Mapper");
				GameShaders.Misc["WaterfallGenerator"] = shader;
				blockTexture = Assets.Request<Texture2D>("Waterfall_Block_Map");
				itemTexture = Assets.Request<Texture2D>("Waterfall_Item_Map");
				wallTexture = Assets.Request<Texture2D>("Waterfall_Wall_Map");
				wallItemTexture = Assets.Request<Texture2D>("Waterfall_Wall_Item_Map");
			}

			foreach ((ModWaterStyle style, Mod mod) in queuedWaterStyles) {
				setStyleMod.Invoke(style, [mod]);
				styleInstancesForServers.Add(style);
				AddContent(new WaterfallBlock(styleCount, $"{style.Mod.Name}_{style.Name}"));
				styleCount++;
			}
			queuedWaterStyles = null;
			Logger.Info("Waterfall style count: " + styleCount);
		}
		public static Asset<Texture2D> GenerateTexture(string name, Texture2D baseTexture, Asset<Texture2D> palette) {
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
			submitLoadedContent.Invoke(asset, [(Texture2D)target, null]);
			return asset;
		}
		public static string GetStyleNameForTranslation(int style) {
			ModWaterStyle waterStyle = Main.dedServ ? styleInstancesForServers[style] : LoaderManager.Get<WaterStylesLoader>().Get(style);
			return waterStyle.PrettyPrintName().Replace("_ ", " ").Replace(" Water Style", "");
		}
	}
	public class ModWaterfallSystem : ModSystem {
		static RecipeGroup nonVanillaWaterfalls;
		static RecipeGroup nonVanillaWaterWalls;
		public override void AddRecipeGroups() {
			RecipeGroup.RegisterGroup("NonVanillaWaterfalls",
				nonVanillaWaterfalls = new RecipeGroup(
					() => Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.WaterfallRecipeGroup").Value,
					ModContent.GetContent<WaterfallItem>().Select(i => i.Type).ToArray()
				)
			);
			RecipeGroup.RegisterGroup("NonVanillaWaterWalls",
				nonVanillaWaterWalls = new RecipeGroup(
					() => Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.WaterWallRecipeGroup").Value,
					ModContent.GetContent<WaterfallWallItem>().Select(i => i.Type).ToArray()
				)
			);
		}
		public override void AddRecipes() {
			Recipe.Create(ItemID.WaterfallBlock)
			.AddRecipeGroup(nonVanillaWaterfalls)
			.Register();
			Recipe.Create(ItemID.WaterfallWall)
			.AddRecipeGroup(nonVanillaWaterWalls)
			.Register();
		}
		public override void Unload() {
			nonVanillaWaterfalls = null;
			nonVanillaWaterWalls = null;
		}
	}
	[Autoload(false)]
	public class WaterfallBlock : ModTile {
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Block_Map";
		public override string Name => "Waterfall_Block_Style_" + name;
		public readonly int style;
		public readonly string name;
		public WaterfallItem Item { get; private set; }
		public WaterfallWall Wall { get; private set; }
		public WaterfallBlock(int style, string name) {
			this.style = style;
			this.name = name;
		}
		public override void Load() {
			Mod.AddContent(Item = new(this));
			Mod.AddContent(Wall = new(this));
		}
		public override void SetStaticDefaults() {
			Main.tileSolid[Type] = true;
			Main.tileBlockLight[Type] = true;
			HitSound = SoundID.Shatter;
			if (Main.dedServ) return;
			TextureAssets.Liquid[style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			ModdedWaterfalls.blockTexture.Wait();
			TextureAssets.Tile[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.blockTexture.Value, TextureAssets.Liquid[style]);
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
	public class WaterfallItem(WaterfallBlock style) : ModItem {
		protected override bool CloneNewInstances => true;
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Item_Map";
		public override string Name => "Waterfall_Item_Style_" + style.name;
		public override LocalizedText DisplayName => Language.GetOrRegister(
			$"Mods.{nameof(ModdedWaterfalls)}.WaterfallItem_{style.name}",
			() => $"{ModdedWaterfalls.GetStyleNameForTranslation(style.style)} Waterfall Block"
		);
		public override LocalizedText Tooltip => LocalizedText.Empty;
		readonly WaterfallBlock style = style;

		public override void SetStaticDefaults() {
			if (Main.dedServ) return;
			TextureAssets.Liquid[style.style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			ModdedWaterfalls.itemTexture.Wait();
			TextureAssets.Item[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.itemTexture.Value, TextureAssets.Liquid[style.style]);
		}
		public override void SetDefaults() {
			Item.DefaultToPlaceableTile(style.Type);
		}
		public override void AddRecipes() {
			CreateRecipe()
			.AddIngredient(ItemID.WaterfallBlock)
			.AddCondition(Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.RecipeCondition"), () => Main.waterStyle == style.style)
			.Register();
		}
		public override void OnResearched(bool fullyResearched) {
			if (CreativeItemSacrificesCatalog.Instance.SacrificeCountNeededByItemId.TryGetValue(ItemID.WaterfallBlock, out int needed)) {
				ItemsSacrificedUnlocksTracker sacrificeTracker = Main.LocalPlayer.creativeTracker.ItemSacrifices;
				if (sacrificeTracker.GetSacrificeCount(ItemID.WaterfallBlock) < needed) {
					sacrificeTracker.SetSacrificeCountDirectly(ContentSamples.ItemPersistentIdsByNetIds[Item.netID], 0);
					sacrificeTracker.RegisterItemSacrifice(ItemID.WaterfallBlock, 1);
				}
			}
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
	[Autoload(false)]
	public class WaterfallWall(WaterfallBlock style) : ModWall {
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Wall_Map";
		public override string Name => "Waterfall_Wall_Style_" + style.name;
		readonly WaterfallBlock style = style;

		public override void Load() {
			Mod.AddContent(new WaterfallWallItem(style));
		}
		public override void SetStaticDefaults() {
			WallID.Sets.BlendType[Type] = Type;
			Main.wallBlend[Type] = Type;
			Main.wallHouse[Type] = true;
			HitSound = SoundID.Shatter;
			if (Main.dedServ) return;
			TextureAssets.Liquid[style.style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			ModdedWaterfalls.wallTexture.Wait();
			TextureAssets.Wall[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.wallTexture.Value, TextureAssets.Liquid[style.style]);
		}
		public override void AnimateWall(ref byte frame, ref byte frameCounter) {
			frame = Main.wallFrame[WallID.Waterfall];
		}
	}
	[Autoload(false)]
	public class WaterfallWallItem(WaterfallBlock style) : ModItem {
		protected override bool CloneNewInstances => true;
		public override string Texture => nameof(ModdedWaterfalls) + "/Waterfall_Wall_Item_Map";
		public override string Name => "Waterfall_Wall_Item_Style_" + style.name;
		public override LocalizedText DisplayName => Language.GetOrRegister(
			$"Mods.{nameof(ModdedWaterfalls)}.WaterfallWallItem_{style.name}",
			() => $"{ModdedWaterfalls.GetStyleNameForTranslation(style.style)} Waterfall Wall"
		);
		public override LocalizedText Tooltip => LocalizedText.Empty;
		readonly WaterfallBlock style = style;

		public override void SetStaticDefaults() {
			if (Main.dedServ) return;
			TextureAssets.Liquid[style.style].Wait();
			Main.QueueMainThreadAction(GenerateTexture);
		}
		void GenerateTexture() {
			ModdedWaterfalls.wallItemTexture.Wait();
			TextureAssets.Item[Type] = ModdedWaterfalls.GenerateTexture(Name, ModdedWaterfalls.wallItemTexture.Value, TextureAssets.Liquid[style.style]);
		}
		public override void SetDefaults() {
			Item.DefaultToPlaceableWall(style.Wall.Type);
		}
		public override void AddRecipes() {
			CreateRecipe()
			.AddIngredient(ItemID.WaterfallWall)
			.AddCondition(Language.GetOrRegister($"Mods.{nameof(ModdedWaterfalls)}.RecipeCondition"), () => Main.waterStyle == style.style)
			.Register();

			CreateRecipe(4)
			.AddIngredient(style.Item.Type)
			.Register();

			Recipe.Create(style.Item.Type)
			.AddIngredient(Type, 4)
			.Register();
		}
		public override void OnResearched(bool fullyResearched) {
			if (CreativeItemSacrificesCatalog.Instance.SacrificeCountNeededByItemId.TryGetValue(ItemID.WaterfallWall, out int needed)) {
				ItemsSacrificedUnlocksTracker sacrificeTracker = Main.LocalPlayer.creativeTracker.ItemSacrifices;
				if (sacrificeTracker.GetSacrificeCount(ItemID.WaterfallWall) < needed) {
					sacrificeTracker.SetSacrificeCountDirectly(ContentSamples.ItemPersistentIdsByNetIds[Item.netID], 0);
					sacrificeTracker.RegisterItemSacrifice(ItemID.WaterfallWall, 1);
				}
			}
		}
	}
}