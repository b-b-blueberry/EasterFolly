using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SpaceCore.Events;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Object = StardewValley.Object;

namespace EasterFolly
{
	// TODO: RELEASE: Add update keys

	public class ModEntry : Mod, IAssetEditor
	{
		internal static ModEntry Instance;
		internal Config Config;

		internal ITranslationHelper i18n => Helper.Translation;
		internal static IJsonAssetsApi JsonAssets;

		internal static readonly string EasterPackPath = Path.Combine("assets", "EasterPack");
		internal const string EasterEggItem = "Chocolate Egg";
		internal const string EasterBasketItem = "Egg Basket";
		internal const string ChocolateBarItem = "Chocolate Bar";
		internal static readonly string[] UntrashableItems = {
			EasterBasketItem,
			EasterEggItem
		};
		internal static KeyValuePair<string, string> TempGiftTasteDialogue;

		public override void Entry(IModHelper helper)
		{
			Config = helper.ReadConfig<Config>();

			helper.Content.AssetEditors.Add(this);

			helper.Events.Input.ButtonPressed += Input_ButtonPressed;
			helper.Events.GameLoop.GameLaunched += GameLoop_GameLaunched;
			helper.Events.GameLoop.DayStarted += GameLoop_DayStarted;
			SpaceEvents.BeforeGiftGiven += SpaceEventsOnBeforeGiftGiven;
		}

		private void Input_ButtonPressed(object sender, ButtonPressedEventArgs e)
		{
			if (PlayerAgencyLostCheck())
				return;

			// When holding an untrashable item, check if cursor is on trashCan or outside of the menu, then block it
			if ((Game1.activeClickableMenu is GameMenu || Game1.activeClickableMenu is ItemGrabMenu)
				&& UntrashableItems.Contains(Game1.player.CursorSlotItem?.Name))
			{
				if (Game1.activeClickableMenu != null
					&& ((Game1.activeClickableMenu is CraftingPage craftingMenu
							&& craftingMenu.trashCan.containsPoint((int)e.Cursor.ScreenPixels.X,
								 (int)e.Cursor.ScreenPixels.Y)
							|| (Game1.activeClickableMenu is InventoryPage inventoryMenu
								 && inventoryMenu.trashCan.containsPoint((int)e.Cursor.ScreenPixels.X,
									 (int)e.Cursor.ScreenPixels.Y)))
						|| !Game1.activeClickableMenu.isWithinBounds((int)e.Cursor.ScreenPixels.X,
							(int)e.Cursor.ScreenPixels.Y)))
				{
					Log.D($"Caught untrashable item ({Game1.player.CursorSlotItem?.Name ?? "null"})");
					Helper.Input.Suppress(e.Button);
				}
			}
		}

		private void LoadJsonAssetsObjects()
		{
			JsonAssets = Helper.ModRegistry.GetApi<IJsonAssetsApi>("spacechase0.JsonAssets");
			if (JsonAssets == null)
			{
				Log.E("Can't access the Json Assets API. Is the mod installed correctly?");
				return;
			}
			JsonAssets.LoadAssets(Path.Combine(Helper.DirectoryPath, EasterPackPath));
		}

		private void GameLoop_GameLaunched(object sender, GameLaunchedEventArgs e)
		{
			LoadJsonAssetsObjects();
		}

		private void GameLoop_DayStarted(object sender, DayStartedEventArgs e)
		{
			// Purge old easter eggs when Summer begins
			if (Game1.dayOfMonth == 1 && Game1.currentSeason == "summer")
			{
				const string itemToPurge = EasterEggItem;
				var itemToAddId = JsonAssets.GetObjectId(ChocolateBarItem);
				foreach (var chest in Game1.locations.SelectMany(
					l => l.Objects.SelectMany(dict => dict.Values.Where(
						o => o is Chest c && c.items.Any(i => i.Name == itemToPurge)))).Cast<Chest>())
				{
					var stack = 0;
					foreach (var item in chest.items.Where(i => i.Name == itemToPurge))
					{
						// TODO: TEST: Easter egg expiration on Summer 1
						stack += item.Stack;
						chest.items[chest.items.IndexOf(item)] = null;
					}
					if (itemToAddId > 0)
						chest.items.Add(new Object(itemToAddId, stack));
				}
			}
		}

		private void Event_UndoGiftChanges(object sender, UpdateTickedEventArgs e)
		{
			// Reset unique easter gift dialogue after it's invoked
			Helper.Events.GameLoop.UpdateTicked -= Event_UndoGiftChanges;
			Game1.NPCGiftTastes[TempGiftTasteDialogue.Key] = TempGiftTasteDialogue.Value;
			Log.D($"Reverted gift taste dialogue to {TempGiftTasteDialogue.Value}");
			TempGiftTasteDialogue = new KeyValuePair<string, string>();
		}

		private void SpaceEventsOnBeforeGiftGiven(object sender, EventArgsBeforeReceiveObject e)
		{
			// Ignore gifts that aren't going to be accepted
			if (!e.Npc.canReceiveThisItemAsGift(e.Gift)
				|| !Game1.player.friendshipData.ContainsKey(e.Npc.Name)
				|| Game1.player.friendshipData[e.Npc.Name].GiftsThisWeek > 1
				|| Game1.player.friendshipData[e.Npc.Name].GiftsToday > 0)
			{
				return;
			}

			// Patch in unique gift dialogue for easter egg deliveries
			if (e.Gift.Name != EasterEggItem && e.Gift.Name != EasterBasketItem)
				return;
			if (e.Gift.Name == EasterBasketItem)
				++Game1.player.CurrentItem.Stack;

			TempGiftTasteDialogue = new KeyValuePair<string, string>(e.Npc.Name, Game1.NPCGiftTastes[e.Npc.Name]);
			var str = i18n.Get($"talk.egg_gift.{e.Npc.Name.ToLower()}");
			if (!str.HasValue())
				throw new KeyNotFoundException();
			Game1.NPCGiftTastes[e.Npc.Name] = UpdateEntry(
				Game1.NPCGiftTastes[e.Npc.Name], new[] { (string)str }, false, false, 2);

			// Remove the patch on the next tick, after the unique gift dialogue has been loaded and drawn
			Helper.Events.GameLoop.UpdateTicked += Event_UndoGiftChanges;
		}

		/// <summary>
		/// Checks whether the player has agency during gameplay, cutscenes, and while in menus.
		/// </summary>
		public bool PlayerAgencyLostCheck()
		{
			// HOUSE RULES
			return !Game1.game1.IsActive // No alt-tabbed game state
				   || Game1.eventUp && !Game1.currentLocation.currentEvent.playerControlSequence // No event cutscenes
				   || Game1.nameSelectUp || Game1.IsChatting || Game1.dialogueTyping || Game1.dialogueUp
				   || Game1.keyboardDispatcher.Subscriber != null // No text inputs
				   || Game1.player.UsingTool || Game1.pickingTool || Game1.numberOfSelectedItems != -1 // No tools in use
				   || Game1.fadeToBlack; // None of that
		}

		/// <summary>
		/// Updates multi-field entries separated by some delimiter, appending or replacing select fields.
		/// </summary>
		/// <returns>The old entry, with fields added from the new entry, reformed into a string of the delimited fields.</returns>
		public static string UpdateEntry(string oldEntry, string[] newEntry, bool append = false, bool replace = false,
			int startIndex = 0, char delimiter = '/')
		{
			var fields = oldEntry.Split(delimiter);
			if (replace)
				fields = newEntry;
			else for (var i = 0; i < newEntry.Length; ++i)
					if (newEntry[i] != null)
						fields[startIndex + i] = append ? $"{fields[startIndex + i]} {newEntry[i]}" : newEntry[i];
			return fields.Aggregate((entry, field) => $"{entry}{delimiter}{field}").Remove(0, 0);
		}

		public bool CanEdit<T>(IAssetInfo asset)
		{
			return Game1.player != null && JsonAssets != null && asset.AssetNameEquals(@"Maps/springobjects");
		}

		public void Edit<T>(IAssetData asset)
		{
			// Patch in easter egg basket icon
			if (asset.AssetNameEquals(@"Maps/springobjects"))
			{
				int index;
				Rectangle sourceArea, destArea;
				Texture2D sourceImage;
				var destImage = asset.AsImage();

				index = JsonAssets.GetObjectId(EasterBasketItem);
				if (index > 0)
				{
					sourceImage = Game1.content.Load<Texture2D>("Maps/Festivals");
					sourceArea = new Rectangle(32, 16, 16, 16);
					destArea = Game1.getSourceRectForStandardTileSheet(destImage.Data, index, 16, 16);
					destImage.PatchImage(sourceImage, sourceArea, destArea, PatchMode.Replace);
				}

				asset.ReplaceWith(destImage.Data);
			}
		}
	}
}
