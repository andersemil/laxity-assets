using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AranciaAssets.EditorTools {
    public class DeepCopy {
		const string Title = "Deep Copy";
		const string ToolsMenuItemName = "Tools/Arancia/Deep Copy #%d";
		const string AssetMenuItemName = "Assets/Arancia/Deep Copy";

		[MenuItem (itemName: ToolsMenuItemName, isValidateFunction: true, priority: 0)]
		[MenuItem (itemName: AssetMenuItemName, isValidateFunction: true, priority: 1)]
		static bool ValidateDeepCopyFolder () {
			var guids = Selection.assetGUIDs;
			if (guids.Length != 1)
				return false;
			var assetPath = AssetDatabase.GUIDToAssetPath (guids [0]);
			return AssetDatabase.IsValidFolder (assetPath);
		}

		[MenuItem (itemName: ToolsMenuItemName, isValidateFunction: false, priority: 0)]
		[MenuItem (itemName: AssetMenuItemName, isValidateFunction: false, priority: 1)]
		static void DeepCopyFolder () {
			if (EditorSettings.serializationMode != SerializationMode.ForceText) {
				Debug.LogError ("Deep copy failed. You need to set Serialization Mode in Project Settings to \"Force Text\". You can change it back after deep-copying if you want.");
				SettingsService.OpenProjectSettings ();
				return;
			}
			var guids = Selection.assetGUIDs;
			var findValue = Path.GetFileName (AssetDatabase.GUIDToAssetPath (guids [0]));
			var replaceValue = findValue;
			SimpleReplaceDialog.InputDialog (Title, findValue, replaceValue, (find, replace) => {
				if (!string.IsNullOrEmpty (find)) {
					DeepCopyFolder (find, replace);
				}
			});
		}

		static void DeepCopyFolder (string findValue, string replaceValue) {
			var guidDict = new Dictionary<string, GUID> ();
			var guids = Selection.assetGUIDs;

			var shouldReplace = findValue != replaceValue;

			var sourceFolderPath = AssetDatabase.GUIDToAssetPath (guids [0]);
			var parentDirPath = Path.GetDirectoryName (sourceFolderPath);

			var newFolderName = Path.GetFileName (sourceFolderPath);
			var _origfoldername = newFolderName;
			if (shouldReplace) {
				newFolderName = newFolderName.Replace (findValue, replaceValue);
			}
			if (newFolderName == _origfoldername) {
				newFolderName += " dcopy";
			}
			var dstFolderPath = Path.Combine (parentDirPath, newFolderName);

			var assetGuids = AssetDatabase.FindAssets ("", new string [1] { sourceFolderPath });
			var numRefsScanned = 0;
			var numRefsReplaced = 0;

			try {
				var p = 0f;
				var dp = 1f / assetGuids.Length;
				foreach (var guid in assetGuids) {
					EditorUtility.DisplayProgressBar (Title, "Copying assets ...", p);
					p += dp;
					var srcPath = AssetDatabase.GUIDToAssetPath (guid);
					if ((File.GetAttributes (srcPath) & FileAttributes.Directory) != 0)
						continue;
					var dstPath = Path.Combine (dstFolderPath, Path.GetRelativePath (sourceFolderPath, srcPath));
					var dstDir = Path.GetDirectoryName (dstPath);
					RecursiveCreateAssetFolder (dstDir);
					if (shouldReplace) {
						dstPath = Path.Combine (dstDir, Path.GetFileName (dstPath).Replace (findValue, replaceValue));
					}
					if (AssetDatabase.CopyAsset (srcPath, dstPath)) {
						guidDict.Add (guid.ToString (), AssetDatabase.GUIDFromAssetPath (dstPath));
					} else {
						Debug.LogError ($"CopyAsset failed for {srcPath}");
					}
				}

				Debug.Log ($"Duplicated {assetGuids.Length} assets, replacing duplicated object references:");

				p = 0f;
				foreach (var guid in guidDict.Values) {
					EditorUtility.DisplayProgressBar (Title, "Scanning references ...", p);
					p += dp;
					var path = AssetDatabase.GUIDToAssetPath (guid);
					var relPath = Path.GetRelativePath (sourceFolderPath, path);

					var ext = Path.GetExtension (relPath);
					var isYaml =
						ext == ".unity"
					 || ext == ".prefab"
					 || ext == ".asset"
					 || ext == ".mat"
					 || ext == ".anim"
					 || ext == ".controller"
					 || ext == ".playable";

					var isModel =
						ext == ".fbx"
					 || ext == ".3ds"
					 || ext == ".obj"
					 || ext == ".dxf"
					 || ext == ".dae";
					if (isModel) {
						path += ".meta";
						relPath += ".meta";
					}

					if (isYaml || isModel) {
						var lines = File.ReadAllLines (path);
						if (lines.Length > 1 && (isModel || lines [0].StartsWith ("%YAML "))) {
							var changed = false;
							var objName = string.Empty;
							for (int i = 1; i < lines.Length; i++) {
								var li = lines [i];
								/*if (li.StartsWith ("--- !u!")) {
									objName = string.Empty;
								} else if (li.StartsWith ("  m_Name: ")) {
									objName = li [10..];
								}*/
								var idxOfGuid = li.IndexOf ("guid: ");
								if (idxOfGuid != -1) {
									numRefsScanned++;
									var refGuid = li.Substring (idxOfGuid + 6, 32);
									if (guidDict.TryGetValue (refGuid, out GUID dstGuid)) {
										lines [i] = li.Replace (refGuid, dstGuid.ToString ());
										numRefsReplaced++;
										changed = true;
										Debug.Log ($"Replaced reference to {Path.GetRelativePath (sourceFolderPath, AssetDatabase.GUIDToAssetPath (refGuid))} in {relPath} line {i}");
									}
								} else if (shouldReplace && li.Trim () == "propertyPath: m_Name") {
									li = lines [++i];
									var idxOfValue = li.IndexOf ("value: ");
									if (idxOfValue != -1) {
										var _c = idxOfValue + 7;
										var _origName = li.Substring (_c);
										var _newName = _origName.Replace (findValue, replaceValue);
										lines [i] = li.Substring (0, _c) + _newName;
										Debug.Log ($"Replaced name {_origName} with {_newName} in {relPath} line {i}");
									}
								}
							}
							if (changed) {
								File.WriteAllLines (path, lines);
							}
						} else {
							Debug.LogWarning ("File did not parse as yaml: " + path);
						}
					}
				}
			} catch (System.Exception ex) {
				Debug.LogError (ex.ToString ());
			}

			EditorUtility.ClearProgressBar ();
			Debug.Log ($"Replaced {numRefsReplaced} references to original objects in a total of {numRefsScanned} object references in duplicated folder");
			AssetDatabase.ImportAsset (dstFolderPath, ImportAssetOptions.ImportRecursive);
		}

		static void RecursiveCreateAssetFolder (string path) {
			var parentFolder = Path.GetDirectoryName (path);
			if (!Directory.Exists (parentFolder)) {
				RecursiveCreateAssetFolder (parentFolder);
			}
			if (!Directory.Exists (path)) {
				AssetDatabase.CreateFolder (parentFolder, Path.GetFileName (path));
			}
		}


		class SimpleReplaceDialog : EditorWindow {
			string find, replace;
			string submitButtonText = "OK";
			string cancelButtonText = "Cancel";
            System.Action<string, string> callback;

			void OnGUI () {
				Event e = Event.current;
				find = EditorGUILayout.TextField ("Find:", find);
				replace = EditorGUILayout.TextField ("Replace:", replace);
				EditorGUILayout.BeginHorizontal ();
				var submit = GUILayout.Button (submitButtonText);
				var cancel = GUILayout.Button (cancelButtonText);
				EditorGUILayout.EndHorizontal ();
				if (submit || (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return)) {
					Close ();
					callback.Invoke (find, replace);
				} else if (cancel) {
					Close ();
					callback.Invoke (null, null);
				}
			}

			public static void InputDialog (string title, string defaultFindValue, string defaultReplaceValue, System.Action<string, string> callback, string submitButtonText = null, string cancelButtonText = null) {
				var window = CreateInstance<SimpleReplaceDialog> ();
				window.titleContent = new GUIContent (title);
				window.callback = callback;
				window.find = defaultFindValue;
				window.replace = defaultReplaceValue;
				if (submitButtonText != null) {
					window.submitButtonText = submitButtonText;
				}
				if (cancelButtonText != null) {
					window.cancelButtonText = cancelButtonText;
				}
				window.ShowUtility ();
				var w = 400;
				var h = 100;
				var mainRect = EditorGUIUtility.GetMainWindowPosition ();
				window.position = new Rect (mainRect.center.x - w / 2, mainRect.center.y - h / 2, w, h);
			}
		}
	}
}