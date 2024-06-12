using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;
using UnityEditor.U2D;
using UnityEngine.U2D;

namespace AranciaAssets.EditorTools {

	internal static class SpriteAtlasCreator {

		const string MenuItemName = "Assets/Arancia/Create SpriteAtlas";
		/// <summary>
		/// Add a menu item for creating a new SpriteAtlas asset in the project based on one or many sprite textures
		/// </summary>
		[MenuItem (itemName: MenuItemName, isValidateFunction: false, priority: 1)]
		static void PackSpriteAtlas () {
			if (EditorSettings.spritePackerMode == SpritePackerMode.Disabled) {
				var sel = EditorUtility.DisplayDialogComplex ("Sprite Atlas Creator", "Sprite packing is disabled. Do you want to enable it now?", "Enable always", "No thanks", "Enable buildtime");
				if (sel != 1) {
					EditorSettings.spritePackerMode = sel == 0 ? SpritePackerMode.AlwaysOnAtlas : SpritePackerMode.BuildTimeOnlyAtlas;
				}
			}

			var guids = Selection.assetGUIDs;
			if (guids.Length > 0) {
				var sa = new SpriteAtlas ();
				var textures = new List<Object> ();
				foreach (var guid in guids) {
					var assetPath = AssetDatabase.GUIDToAssetPath (guid);
					textures.Add (AssetDatabase.LoadMainAssetAtPath (assetPath));
				}
				sa.Add (textures.ToArray ());
				var path = Path.Combine (Path.GetDirectoryName (AssetDatabase.GUIDToAssetPath (guids [0])), "New SpriteAtlas.spriteatlas");
				ProjectWindowUtil.CreateAsset (sa, path);
			}
		}

		/// <summary>
		/// Only enable menu item if valid sprite textures are selected
		/// </summary>
		[MenuItem (itemName: MenuItemName, isValidateFunction: true, priority: 1)]
		static bool ValidatePackSpriteAtlas () {
			var guids = Selection.assetGUIDs;
			if (guids.Length == 0)
				return false;
			foreach (var guid in guids) {
				var assetPath = AssetDatabase.GUIDToAssetPath (guid);
				var textureImporter = AssetImporter.GetAtPath (assetPath) as TextureImporter;
				if (textureImporter == null || textureImporter.textureType != TextureImporterType.Sprite) {
					return false;
				}
			}
			return true;
		}
	}

}