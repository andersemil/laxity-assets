using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.Events;

using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.SceneManagement;

using Unity.EditorCoroutines.Editor;

namespace AranciaAssets.EditorTools {

	public class Finder : EditorWindow {
		//Vector2 buildReportScrollPosition = Vector2.zero;
		private int LayerIndex;
		private string MethodName = string.Empty;
		private string TagName = "Untagged";
		private string TextString = string.Empty;
		private int AtlasName;

		[Serializable]
		public class Result {
			public UnityEngine.Object obj;
			public int activeEditorIndex;
			public int fileID;
			public string propertyPath;
			public string text;
		}
		private static string ResultsLabel;
		private static readonly List<Result> Results = new ();
		private Vector2 resultScrollPosition;
		private Rect ResultsScrollRect;
		private static EditorCoroutine ScrollToCoroutine;
		private static bool ResultsAreHighlighteable;
		private static string PrevFocusControl;

		static Finder instance;

		static readonly HashSet<string> ListInvokations = new ();
		static readonly HashSet<string> ListStrings = new ();

		/// <summary>
		/// Do not look for strings inside these component types
		/// </summary>
		static readonly HashSet<Type> IgnoreTypesForStringSearch = new () {
			typeof (ParticleSystem),
			typeof (UnityEngine.UI.Button),
			typeof (UnityEngine.UI.Slider),
		};

		readonly MethodInfo miEndEditingActiveTextField;
		readonly Type InspectorWindowType, PropertyEditorType;
		EditorWindow InspectorWindow;
		Rect MethodRect, TextRect;
		Texture2D SolidBlackTexture;

		/*
		/// <summary>
		/// Build cached list of all Component types in all assemblies
		/// </summary>
		List<Type> GetComponentTypes () {
			ComponentTypes = new List<Type> (1024);
			ComponentTypes.Add (typeof(GameObject));
			ComponentTypes.AddRange (AppDomain.CurrentDomain.GetAssemblies ().Where (a => !a.IsDynamic).SelectMany (a => a.GetTypes ().Where (c => c.IsSubclassOf (typeof(Component)))));
			return ComponentTypes;
		}*/

		public Finder () {
			instance = this;

			miEndEditingActiveTextField = typeof (EditorGUI).GetMethod ("EndEditingActiveTextField", BindingFlags.Static | BindingFlags.NonPublic);
			InspectorWindowType = Type.GetType ("UnityEditor.InspectorWindow,UnityEditor");
			PropertyEditorType = Type.GetType ("UnityEditor.PropertyEditor,UnityEditor");

			wantsMouseMove = true;
		}

		void OnEnable () {
			var icon = AssetDatabase.LoadAssetAtPath<Texture2D> (AssetDatabase.GUIDToAssetPath ("db4aaf330ef7e43858c9c49ff984c8c3"));
			//var textures = PlayerSettings.GetIconsForTargetGroup (BuildTargetGroup.Unknown);
			//if (textures != null && textures.Length > 0 && textures [0] != null) {
			if (icon != null) {
				//GUILayout.Label (textures[0], new GUIStyle () {alignment = TextAnchor.UpperRight});
				titleContent = new GUIContent ("Finder", icon, "Arancia Finder");
			}
			SolidBlackTexture = new Texture2D (1, 1);
			SolidBlackTexture.SetPixel (0, 0, new Color32 (0x20, 0x70, 0xc0, 0xff));
			SolidBlackTexture.Apply ();
		}

		void OnHierarchyChange () {
			ListInvokations.Clear ();
			ListStrings.Clear ();
		}

		void OnGUI () {
			Event e = Event.current;
			if (e.type == EventType.MouseDown && ComboBoxPopup.Instance != null) {
				ComboBoxPopup.Instance.Close ();
			}
			if (ComboBoxPopup.Instance == null || !ComboBoxPopup.HandleEvent (e)) {
				if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return) {
					if (ComboBoxPopup.Instance != null) {
						ComboBoxPopup.Instance.Close ();
					}
					switch (GUI.GetNameOfFocusedControl ()) {
					case "Method":
						FindInvokationsOfMethod (MethodName);
						break;
					case "Text":
						FindString (TextString);
						break;
					default:
						Debug.Log ("Unknown control " + GUI.GetNameOfFocusedControl ());
						break;
					}
				}
			}

			var newFocus = GUI.GetNameOfFocusedControl ();

			var buttonMaxWidthOption = GUILayout.MaxWidth (100f);
			var bigButtonMaxWidthOption = GUILayout.MaxWidth (170f);
			var expandWidthOption = GUILayout.ExpandWidth (true);

			EditorGUILayout.Separator ();

			EditorGUILayout.BeginHorizontal ();
			var li = EditorGUILayout.LayerField (new GUIContent ("Layer:", "Find all gameobjects in the given layer"), LayerIndex, expandWidthOption);
			if (li != LayerIndex || GUILayout.Button ("Find objects", buttonMaxWidthOption)) {
				LayerIndex = li;
				FindObjectsInLayer (LayerIndex);
			}
			EditorGUILayout.EndHorizontal ();

			EditorGUILayout.BeginHorizontal ();
			var tag = EditorGUILayout.TagField (new GUIContent ("Tag:", "Find all gameobjects in a scene with the given tag"), TagName, expandWidthOption);
			if (tag != TagName || GUILayout.Button ("Find objects", buttonMaxWidthOption)) {
				TagName = tag;
				FindObjectsWithTag (TagName);
			}
			EditorGUILayout.EndHorizontal ();

			EditorGUILayout.BeginHorizontal ();
			EditorGUILayout.PrefixLabel (new GUIContent ("Method:", "Whole or partial method and class name to find references to"));
			GUI.SetNextControlName ("Method");
			EditorGUI.BeginChangeCheck ();
			MethodName = EditorGUILayout.TextField (MethodName, expandWidthOption).Trim ();
			if (Event.current.type == EventType.Repaint) {
				MethodRect = GUILayoutUtility.GetLastRect ();
			}
			if (EditorGUI.EndChangeCheck () || (e.type == EventType.MouseDown && newFocus == "Method" && PrevFocusControl != newFocus)) {
				if (ListInvokations.Count == 0) {
					FindAllInvokations ();
				}
				IEnumerable<string> matches = null;
				if (MethodName.Trim ().Length > 1) {
					var str = MethodName.Trim ();
					var indexOfDot = str.LastIndexOf ('.');
					if (indexOfDot > 0) {
						var compName = str.Substring (0, indexOfDot + 1);
						var methMatch = str.Substring (indexOfDot + 1);
						matches = ListInvokations.Where (i => i.StartsWith (compName) && i.Substring (indexOfDot + 1).Contains (methMatch, StringComparison.InvariantCultureIgnoreCase));
					} else {
						matches = ListInvokations.Where (i => i.Contains (str, StringComparison.InvariantCultureIgnoreCase));
					}
				} else {
					matches = ListInvokations.Take (8);
				}
				if (matches != null && matches.Count () != 0) {
					ComboBoxPopup.Show (matches, MethodRect, (value) => {
						MethodName = value;
						miEndEditingActiveTextField.Invoke (null, null);
						FindInvokationsOfMethod (MethodName);
						EditorGUI.FocusTextInControl ("Method");
						SendEvent (new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow });
					});
				} else if (ComboBoxPopup.Instance != null) {
					ComboBoxPopup.Instance.Close ();
				}
			}
			using (new EditorGUI.DisabledScope (string.IsNullOrWhiteSpace (MethodName))) {
				if (GUILayout.Button ("Find references", buttonMaxWidthOption)) {
					FindInvokationsOfMethod (MethodName);
				}
			}
			EditorGUILayout.EndHorizontal ();

#if !UNITY_2022_1_OR_NEWER
			EditorGUILayout.BeginHorizontal ();
			var atlasLabelContent = new GUIContent ("Sprite Atlas:", "Find all sprites with the selected legacy packing tag");
			if (Packer.atlasNames.Length == 0) {
				EditorGUILayout.PrefixLabel (atlasLabelContent);
				if (GUILayout.Button ("Rebuild Legacy Sprite Atlas", bigButtonMaxWidthOption)) {
#pragma warning disable 0618
					Packer.RebuildAtlasCacheIfNeeded (EditorUserBuildSettings.activeBuildTarget, true);
#pragma warning restore
				}
			} else {
				var selectedAtlas = EditorGUILayout.Popup (atlasLabelContent, AtlasName, Packer.atlasNames, expandWidthOption);
				if (selectedAtlas != AtlasName || GUILayout.Button ("Find sprites", buttonMaxWidthOption)) {
					AtlasName = selectedAtlas;
					FindTextureAssetsInLegacyAtlas (Packer.atlasNames [selectedAtlas]);
				}
			}
			EditorGUILayout.EndHorizontal ();
#endif
			EditorGUILayout.BeginHorizontal ();
			EditorGUILayout.PrefixLabel (new GUIContent ("String:", "Whole or partial string to search for"));
			GUI.SetNextControlName ("Text");
			EditorGUI.BeginChangeCheck ();
			TextString = EditorGUILayout.TextField (TextString, expandWidthOption);
			if (Event.current.type == EventType.Repaint) {
				TextRect = GUILayoutUtility.GetLastRect ();
			}
			if (EditorGUI.EndChangeCheck () || (e.type == EventType.MouseDown && newFocus == "Text" && PrevFocusControl != newFocus)) {
				if (ListStrings.Count == 0) {
					FindAllStrings ();
				}
				IEnumerable<string> matches = null;
				if (TextString.Trim ().Length > 1) {
					var str = TextString.Trim ();
					matches = ListStrings.Where (i => i.Contains (str, StringComparison.InvariantCultureIgnoreCase));
				} else {
					matches = ListStrings.Take (8);
				}
				if (matches != null && matches.Count () != 0) {
					ComboBoxPopup.Show (matches, TextRect, (value) => {
						TextString = value;
						miEndEditingActiveTextField.Invoke (null, null);
						FindString (TextString);
						EditorGUI.FocusTextInControl ("Text");
						SendEvent (new Event { type = EventType.KeyDown, keyCode = KeyCode.RightArrow });
					});
				} else if (ComboBoxPopup.Instance != null) {
					ComboBoxPopup.Instance.Close ();
				}
			}
			using (new EditorGUI.DisabledScope (string.IsNullOrEmpty (TextString))) {
				if (GUILayout.Button ("Find string", buttonMaxWidthOption)) {
					FindString (TextString);
				}
			}
			EditorGUILayout.EndHorizontal ();

			EditorGUILayout.BeginHorizontal ();
			EditorGUILayout.PrefixLabel (new GUIContent ("Missing references:", "Find all missing references in open scenes"));
			if (GUILayout.Button (new GUIContent ("Find missing references", "Find all missing references in open scenes"), bigButtonMaxWidthOption)) {
				FindMissingReferences ();
			}
			EditorGUILayout.EndHorizontal ();

			EditorGUILayout.BeginHorizontal ();
			EditorGUILayout.PrefixLabel (new GUIContent ("Missing scripts:", "Find all missing scripts in open scenes"));
			if (GUILayout.Button (new GUIContent ("Find missing scripts", "Find all missing scripts in open scenes"), bigButtonMaxWidthOption)) {
				FindMissingScripts ();
			}
			EditorGUILayout.EndHorizontal ();

			EditorGUILayout.Separator ();

			var buttonHoverStyle = new GUIStyle (GUI.skin.button);
			buttonHoverStyle.alignment = TextAnchor.MiddleLeft;
			buttonHoverStyle.onHover.background = SolidBlackTexture;
			buttonHoverStyle.hover.textColor = Color.cyan;
			buttonHoverStyle.richText = true;

			Result SelectedResult = null;
			if (!string.IsNullOrWhiteSpace (ResultsLabel)) {
				EditorGUILayout.LabelField (ResultsLabel);
			}
			var cnt = Results.Count;
			if (cnt != 0) {
				ResultsScrollRect = EditorGUILayout.BeginVertical ();
				resultScrollPosition = EditorGUILayout.BeginScrollView (resultScrollPosition, false, false, GUILayout.ExpandHeight (true));
				for (int i = 0; i < Results.Count; i++) {
					if (GUILayout.Button (Results [i].text, buttonHoverStyle)) {
						SelectedResult = Results [i];
					}
				}
				GUI.backgroundColor = Color.white;
				GUILayout.FlexibleSpace ();
				EditorGUILayout.EndScrollView ();
				EditorGUILayout.EndVertical ();
				GUI.Box (ResultsScrollRect, GUIContent.none);
			}

			PrevFocusControl = newFocus;

			if (SelectedResult != null) {
				ShowResult (SelectedResult);
			}
		}

		/// <summary>
		/// Change selection to the given object. If obj is a component, expand it in the inspector window and scroll to it.
		/// </summary>
		private void ShowResult (Result result) {
			if (PrefabUtility.IsPartOfPrefabAsset (result.obj)) {
				AssetDatabase.OpenAsset (AssetDatabase.LoadAssetAtPath<GameObject> (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot (result.obj)));
			}
			Selection.activeObject = result.obj;
			var component = result.obj as Component;
			if (component != null || result.activeEditorIndex != default) {
				if (component != null) {
					UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded (component, true);
				}
				ActiveEditorTracker.sharedTracker.ForceRebuild ();

				//Scroll to component editor. This is not possible through the public API so we use reflection to dig through the internals
				InspectorWindow = GetWindow (InspectorWindowType);
				typeof (EditorWindow).GetMethod ("RepaintImmediately", BindingFlags.NonPublic | BindingFlags.Instance).Invoke (InspectorWindow, null);

				var tr = (ActiveEditorTracker)PropertyEditorType.GetProperty ("tracker")?.GetValue (InspectorWindow);
				if (tr != null) {
					var editors = tr.activeEditors;
					for (int i = 0; i < editors.Length; i++) {
						if ((result.activeEditorIndex != default && i == result.activeEditorIndex)
							|| (result.activeEditorIndex == default && editors [i].targets.Any (t => t == result.obj))) {

							// if property is a member of an array, expand it
							var idxOfArray = !string.IsNullOrEmpty (result.propertyPath) ? result.propertyPath.LastIndexOf (']') : -1;
							if (idxOfArray > 0) {
								var propPath = result.propertyPath.Substring (0, idxOfArray + 1);
								var arrayPath = result.propertyPath.Substring (0, propPath.IndexOf (".Array"));
								var prop = editors [i].serializedObject.FindProperty (arrayPath);
								prop.isExpanded = true;
								prop = editors [i].serializedObject.FindProperty (propPath);
								// if the array element has children then expand it
								if (prop.hasChildren)
									prop.isExpanded = true;
								editors [i].Repaint ();
							}

							var root = InspectorWindow.rootVisualElement.Q (className: "unity-inspector-editors-list");
							if (root != null && root.childCount > i) {
								var child = root [i] [1];
								if (ScrollToCoroutine != null) {
									EditorCoroutineUtility.StopCoroutine (ScrollToCoroutine);
								}
								var highlightIdentifier = /*ResultsAreHighlighteable && */!string.IsNullOrWhiteSpace (result.propertyPath) ? $"{result.fileID}.{result.propertyPath}" : null;
								ResultsAreHighlighteable = true;
								ScrollToCoroutine = EditorCoroutineUtility.StartCoroutine (E_ScrollTo (child, highlightIdentifier), this);
							}
							break;
						}
					}
				}
			}
		}

		/// <summary>
		/// The ScrollTo method usually takes two tries for some odd reason, probably dependant on some repainting.
		/// </summary>
		IEnumerator E_ScrollTo (VisualElement visualElement, string highlightIdentifier) {
			Highlighter.Stop ();
			var inspectorScrollView = (ScrollView)PropertyEditorType.GetField ("m_ScrollView", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue (InspectorWindow);
			yield return null;
			inspectorScrollView?.ScrollTo (visualElement);
			yield return null;
			inspectorScrollView?.ScrollTo (visualElement);

			if (!string.IsNullOrWhiteSpace (highlightIdentifier)) {
				Highlighter.Highlight ("Inspector", highlightIdentifier, HighlightSearchMode.Identifier);
				yield return new WaitForSecondsRealtime (3f);
				Highlighter.Stop ();
			}
		}

		[MenuItem (itemName: "Tools/Arancia Finder %g", isValidateFunction: false, priority: 0)]
		static void Init () {
			instance = GetWindow<Finder> ("Arancia Finder", true);
			instance.Focus ();
		}

		[MenuItem (itemName: "GameObject/Arancia/Find references", isValidateFunction: true, priority: 0)]
		[MenuItem (itemName: "Assets/Arancia/Find references", isValidateFunction: true, priority: 0)]
		static bool ValidateFindReferencesToSelectedObject () {
			return Selection.activeObject != null && !(Selection.activeObject is DefaultAsset);
		}

		[MenuItem (itemName: "GameObject/Arancia/Find references", isValidateFunction: false, priority: 0)]
		[MenuItem (itemName: "Assets/Arancia/Find references", isValidateFunction: false, priority: 0)]
		static void FindReferencesToSelectedObject () {
			var activeObject = Selection.activeObject;
			activeObject = (activeObject is Component c) ? c.gameObject : activeObject;
			Init ();
			Results.Clear ();
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			var allComponents = allObjects.Where (o => o.scene.isLoaded).SelectMany (o => o.GetComponents<Component> ());
			foreach (var component in allComponents) {
				if (component == null)
					continue;
				var go = component.gameObject;
				var so = new SerializedObject (component);
				var sp = so.GetIterator ();
				SerializedProperty pCalls;
				if (!sp.NextVisible (true))
					continue;
				bool enterChildren;
				do {
					enterChildren = true;
					if (sp.propertyType == SerializedPropertyType.Generic && (pCalls = sp.FindPropertyRelative (BetterEventDrawer.kCallsPath)) != null) {
						var len = pCalls.arraySize;
						for (int i = 0; i < len; i++) {
							//Check if object is used as a target
							var pCall = pCalls.GetArrayElementAtIndex (i);
							var oa = pCall.FindPropertyRelative ("m_Target").objectReferenceValue;
							if (oa != null) {
								var parentObject = (oa is Component co) ? co.gameObject : oa;
								if (parentObject == activeObject)
									AddResult (component, pCall.propertyPath, NicePropertyPath (sp));
							}
							//Check if object is used as an argument
							oa = pCall.FindPropertyRelative ("m_Arguments.m_ObjectArgument").objectReferenceValue;
							if (oa != null) {
								var parentObject = (oa is Component co) ? co.gameObject : oa;
								if (parentObject == activeObject)
									AddResult (component, pCall.propertyPath, NicePropertyPath (sp));
							}
						}
						enterChildren = false;
					} else if (sp.propertyType == SerializedPropertyType.ObjectReference) {
						//Check if object is used as a reference
						var oa = sp.objectReferenceValue;
						var parentObject = (oa is Component co) ? co.gameObject : oa;
						if (parentObject == activeObject)
							AddResult (component, sp.propertyPath, NicePropertyPath (sp));
					}
				} while (sp.NextVisible (enterChildren));
			}
			if (activeObject is GameObject ago) {
				ResultsLabel = $"Found {Results.Count} references to {ago.GetScenePath ()}.";
			} else {
				ResultsLabel = $"Found {Results.Count} references to {AssetDatabase.GetAssetPath (activeObject)}.";
			}
			ResultsAreHighlighteable = activeObject is GameObject || activeObject is Transform;
			if (Results.Count == 1) {
				instance.ShowResult (Results [0]);
			}
			instance.Repaint ();
		}

		static string NicePropertyPath (SerializedProperty sp) {
			var pp = sp.propertyPath;
			return pp.Replace (".Array.data[", "[");
		}

		void FindObjectsInLayer (int layerIndex) {
			Results.Clear ();
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			foreach (var obj in allObjects) {
				if (obj.layer != layerIndex)
					continue;
				var parent = obj.transform.parent;
				if (parent == null || parent.gameObject.layer != obj.layer)
					AddResult (obj);
			}
			ResultsLabel = $"Found {Results.Count} GameObjects in {LayerMask.LayerToName (layerIndex)} layer.";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}

		void FindObjectsWithTag (string tag) {
			Results.Clear ();
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			foreach (var obj in allObjects) {
				if (obj.scene.isLoaded && obj.CompareTag (tag)) {
					AddResult (obj);
				}
			}
			ResultsLabel = $"Found {Results.Count} GameObjects with {tag} tag.";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}

		void FindString (string textString) {
			Results.Clear ();
			if (string.IsNullOrEmpty (textString))
				return;
			var allGOs = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			var allComponents = allGOs.Where (o => o.scene.isLoaded).SelectMany (o => o.GetComponents<Component> ());
			foreach (var component in allComponents) {
				if (component == null || IgnoreTypesForStringSearch.Contains (component.GetType ()))
					continue;
				var so = new SerializedObject (component);
				var sp = so.GetIterator ();
				SerializedProperty pCalls;
				if (!sp.NextVisible (true))
					continue;
				bool enterChildren;
				do {
					enterChildren = true;
					if (sp.propertyType == SerializedPropertyType.Generic && (pCalls = sp.FindPropertyRelative (BetterEventDrawer.kCallsPath)) != null) {
						var len = pCalls.arraySize;
						for (int i = 0; i < len; i++) {
							var pCall = pCalls.GetArrayElementAtIndex (i);
							var sv = pCall.FindPropertyRelative ("m_Arguments.m_StringArgument").stringValue;
							if (sv.Replace ('\n', ' ').Contains (textString, StringComparison.InvariantCultureIgnoreCase))
								AddResult (component, pCall.propertyPath, NicePropertyPath (sp));
						}
						enterChildren = false;
					} else if (sp.propertyType == SerializedPropertyType.String
					 && sp.stringValue.Replace ('\n', ' ').Contains (textString, StringComparison.InvariantCultureIgnoreCase)) {
						AddResult (component, sp.propertyPath, NicePropertyPath (sp));
					}
				} while (sp.NextVisible (enterChildren));
			}
			ResultsLabel = $"Found {Results.Count} instances of the string '{textString.Replace ('\n', ' ').Truncate (32)}'";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}

		/// <summary>
		/// Build HashSet of all serialized strings in the loaded scenes for the ComboBox
		/// </summary>
		void FindAllStrings () {
			var allGOs = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			var allComponents = allGOs.Where (o => o.scene.isLoaded).SelectMany (o => o.GetComponents<Component> ());
			ListStrings.Clear ();
			foreach (var component in allComponents) {
				if (component == null || IgnoreTypesForStringSearch.Contains (component.GetType ()))
					continue;
				var so = new SerializedObject (component);
				var sp = so.GetIterator ();
				SerializedProperty pCalls;
				if (!sp.NextVisible (true))
					continue;
				bool enterChildren;
				do {
					enterChildren = true;
					if (sp.propertyType == SerializedPropertyType.Generic && (pCalls = sp.FindPropertyRelative (BetterEventDrawer.kCallsPath)) != null) {
						var len = pCalls.arraySize;
						for (int i = 0; i < len; i++) {
							var sv = pCalls.GetArrayElementAtIndex (i).FindPropertyRelative ("m_Arguments.m_StringArgument").stringValue;
							if (!string.IsNullOrWhiteSpace (sv))
								ListStrings.Add (sv.Replace ('\n', ' '));
						}
						enterChildren = false;
					} else if (sp.propertyType == SerializedPropertyType.String) {
						var sv = sp.stringValue;
						if (!string.IsNullOrWhiteSpace (sv))
							ListStrings.Add (sv.Replace ('\n', ' '));
					}
				} while (sp.NextVisible (enterChildren));
			}
		}

		void FindInvokationsOfMethod (string partialMethodName) {
			Results.Clear ();
			if (string.IsNullOrWhiteSpace (partialMethodName))
				return;
			var lioDot = partialMethodName.LastIndexOf ('.');
			string methodMatch, classMatch;
			if (lioDot > 0) {
				methodMatch = partialMethodName.Substring (lioDot + 1);
				classMatch = partialMethodName.Substring (0, lioDot);
			} else {
				methodMatch = classMatch = partialMethodName;
			}
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			var allComponents = allObjects.Where (o => o.scene.isLoaded).SelectMany (o => o.GetComponents<Component> ());
			foreach (var component in allComponents) {
				if (component == null)
					continue;
				var componentType = component.GetType ();
				var fields = componentType.GetFields (BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				foreach (var field in fields) {
					if (field.GetValue (component) is UnityEventBase unityEvent) {
						FindMatchingInvokation (component, field.Name, unityEvent, classMatch, methodMatch);
					} else if (field.GetValue (component) is List<UnityEngine.EventSystems.EventTrigger.Entry> eventTriggers) {
						var i = 0;
						foreach (var et in eventTriggers) {
							FindMatchingInvokation (component, $"m_Delegates.Array.data[{i}].callback", et.callback, classMatch, methodMatch);
							i++;
						}
					}
				}
			}
			ResultsLabel = $"Found {Results.Count} event listeners matching '{partialMethodName}'";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}

		/// <summary>
		/// Build HashSet of all event listeners in the loaded scenes for the ComboBox
		/// </summary>
		void FindAllInvokations () {
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			var allComponents = allObjects.Where (o => o.scene.isLoaded).SelectMany (o => o.GetComponents<Component> ());
			ListInvokations.Clear ();
			foreach (var component in allComponents) {
				if (component == null)
					continue;
				var componentType = component.GetType ();
				var fields = componentType.GetFields (BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				foreach (var field in fields) {
					if (field.GetValue (component) is UnityEventBase unityEvent) {
						AddInvokations (ListInvokations, unityEvent);
					} else if (field.GetValue (component) is List<UnityEngine.EventSystems.EventTrigger.Entry> eventTriggers) {
						foreach (var et in eventTriggers) {
							AddInvokations (ListInvokations, et.callback);
						}
					}
				}
			}
		}

		void AddInvokations (HashSet<string> Invokations, UnityEventBase unityEvent) {
			for (int k = 0; k < unityEvent.GetPersistentEventCount (); k++) {
				var methodName = unityEvent.GetPersistentMethodName (k);
				var classPath = unityEvent.GetPersistentTarget (k).GetType ().Name;
				Invokations.Add ($"{classPath}.{methodName}");
			}
		}

		void FindMatchingInvokation (Component component, string fieldName, UnityEventBase unityEvent, string classMatch, string methodMatch) {
			for (int k = 0; k < unityEvent.GetPersistentEventCount (); k++) {
				var methodName = unityEvent.GetPersistentMethodName (k);
				var classPath = unityEvent.GetPersistentTarget (k).GetType ().Name;
				var hasClassMatch = classPath.IndexOf (classMatch, StringComparison.InvariantCultureIgnoreCase) >= 0;
				var hasMethodMatch = methodName.IndexOf (methodMatch, StringComparison.InvariantCultureIgnoreCase) >= 0;
				if ((classMatch != methodMatch && hasClassMatch && hasMethodMatch)
				 || (classMatch == methodMatch && (hasClassMatch || hasMethodMatch))) {
					var path = $"{fieldName}.m_PersistentCalls.m_Calls.Array.data[{k}]";
					AddResult (component, path, fieldName);
					break;
				}
			}
		}

		static void AddResult (Component component, string PropertyPath, string FieldName, string Suffix = null) {
			Results.Add (new Result { obj = component, fileID = component.GetInstanceID (), propertyPath = PropertyPath, text = $"{(PrefabUtility.IsPartOfPrefabAsset (component) ? "[PREFAB] " : string.Empty)}{component.gameObject.GetScenePath ()} <color=#44ffcc>{component.GetType ().Name}</color>.<color=#ffffcc>{FieldName}</color>{Suffix ?? string.Empty}" });
		}

		static void AddResult (GameObject GameObject, string PropertyPath = null, string Suffix = null) {
			Results.Add (new Result { obj = GameObject, propertyPath = PropertyPath, text = $"{(PrefabUtility.IsPartOfPrefabAsset (GameObject) ? "[PREFAB] " : string.Empty)}{GameObject.GetScenePath ()}{Suffix ?? string.Empty}" });
		}

		static void AddMissingScriptResult (GameObject GameObject, int componentIndex, int componentFileID, string Suffix = null) {
			Results.Add (new Result { obj = GameObject, activeEditorIndex = componentIndex, fileID = componentFileID, propertyPath = "m_Script", text = $"{(PrefabUtility.IsPartOfPrefabAsset (GameObject) ? "[PREFAB] " : string.Empty)}{GameObject.GetScenePath ()}{Suffix ?? string.Empty}" });
		}

#if !UNITY_2022_1_OR_NEWER
		/// <summary>
		/// Locates all textures packed as sprites in an atlas of a given name.
		/// </summary>
		void FindTextureAssetsInLegacyAtlas (string atlasName) {
			Results.Clear ();
			var indexOfParenth = atlasName.IndexOf ('(');
			var packingTag = indexOfParenth > 0 ? atlasName.Substring (0, indexOfParenth - 1) : atlasName;
			//var textures = Packer.GetTexturesForAtlas (packingTag);
			var assetPaths = AssetDatabase.FindAssets ("t:Texture2D").Select (guid => AssetDatabase.GUIDToAssetPath (guid));
			foreach (var assetPath in assetPaths) {
				var ti = AssetImporter.GetAtPath (assetPath) as TextureImporter;
				if (ti != null && ti.textureType == TextureImporterType.Sprite && ti.spritePackingTag == packingTag) {
					Results.Add (new Result { obj = AssetDatabase.LoadMainAssetAtPath (assetPath), text = assetPath });
				}
			}
			ResultsLabel = $"Found {Results.Count} sprite textures with the packing tag '{packingTag}'";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}
#endif

		/*
		UnityEditor.BuildPlayerWindow
		UnityEditor.ConsoleWindow
		UnityEditor.ObjectSelector
		UnityEditor.ProjectBrowser
		UnityEditor.SceneHierarchySortingWindow
		UnityEditor.SceneHierarchyWindow
		UnityEditor.InspectorWindow
		UnityEditor.PreviewWindow
		UnityEditor.PlayModeView
		UnityEditor.SearchableEditorWindow
		UnityEditor.LightingExplorerWindow
		UnityEditor.LightingWindow
		UnityEditor.LightmapPreviewWindow
		UnityEditor.SceneView,
		UnityEditor.SettingsWindow,
		UnityEditor.ProjectSettingsWindow,
		UnityEditor.PreferenceSettingsWindow,
		UnityEditor.SpriteUtilityWindow,
		*/

		void FindMissingReferences () {
			/*var type = GetType ("UnityEditor.InspectorWindow");
			var inspectorWindow = GetWindow (type);
			inspectorWindow.*/

			Results.Clear ();
			var list = new List<GameObject> ();
			var listComp = new List<Component> ();
			for (int i = 0; i < EditorSceneManager.sceneCount; i++) {
				var scene = EditorSceneManager.GetSceneAt (i);
				if (!scene.isLoaded)
					continue;
				scene.GetRootGameObjects (list);
				foreach (var go in list) {
					go.GetComponentsInChildren (true, listComp);
					foreach (var component in listComp) {
						if (component == null) {
							Debug.LogError ("Missing script found on: " + component.GetScenePath (), component);
						} else {
							var so = new SerializedObject (component);
							var sp = so.GetIterator ();

							while (sp.NextVisible (true)) {
								if (sp.propertyType != SerializedPropertyType.ObjectReference) {
									continue;
								}

								if (sp.objectReferenceValue == null && sp.objectReferenceInstanceIDValue != 0) {
									AddResult (component, sp.propertyPath, NicePropertyPath (sp));
								}
							}
						}
					}
				}
			}
			ResultsLabel = $"Found {Results.Count} missing references in loaded scenes.";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}

		const string kEditorClassIdentifier = "m_EditorClassIdentifier:";
		const string kName = "m_Name:";

		void FindMissingScripts () {
			/*var type = GetType ("UnityEditor.InspectorWindow");
			var inspectorWindow = GetWindow (type);
			inspectorWindow.*/
			var inspectorModeInfo = typeof (SerializedObject).GetProperty ("inspectorMode", BindingFlags.NonPublic | BindingFlags.Instance);

			Results.Clear ();
			var allObjects = Resources.FindObjectsOfTypeAll (typeof (GameObject)) as GameObject [];
			foreach (var go in allObjects) {
				if (!go.scene.isLoaded)
					continue;
				var components = go.GetComponents<Component> ();
				var i = 0;
				foreach (var component in components) {
					if (component == null) {
						var serializedObject = new SerializedObject (go);
						inspectorModeInfo.SetValue (serializedObject, InspectorMode.Debug, null);
						var gameObjectFileID = serializedObject.FindProperty ("m_LocalIdentfierInFile").intValue;

						var componentsProperty = serializedObject.FindProperty ("m_Component");
						var cIterator = componentsProperty.GetArrayElementAtIndex (i);
						cIterator.Next (true);
						cIterator.Next (true);
						var instanceID = cIterator.intValue;

						if (EditorSettings.serializationMode != SerializationMode.ForceText) {
							AddMissingScriptResult (go, i + 1, instanceID);
							continue;
						} 

						var sceneLines = System.IO.File.ReadAllLines (go.scene.path);
						var goFound = false;
						var componentListFound = false;
						var c = 0;
						var fileIDString = gameObjectFileID.ToString ();
						var scriptFileID = string.Empty;
						foreach (var line in sceneLines) {
							if (!goFound) {
								if (line.StartsWith ("--- !u!") && line.EndsWith (fileIDString)) {
									goFound = true;
								}
							} else {
								if (!componentListFound) {
									componentListFound = line == "  m_Component:";
								} else {
									if (c == i) {
										var m = Regex.Match (line, "  - component: {fileID: (\\d+)}");
										scriptFileID = m.Groups [1].Value;
										break;
									}
									c++;
								}
							}
						}
						if (!string.IsNullOrWhiteSpace (scriptFileID)) {
							var scriptFound = false;
							foreach (var line in sceneLines) {
								if (!scriptFound) {
									if (line.StartsWith ("--- !u!") && line.EndsWith (scriptFileID)) {
										scriptFound = true;
									}
								} else {
									var trimLine = line.Trim ();
									if (trimLine.StartsWith (kEditorClassIdentifier) || trimLine.StartsWith (kName)){
										AddMissingScriptResult (go, i + 1, instanceID, line.Substring (line.IndexOf (':') + 1));
										break;
									}
								}
							}
						}
					}
					i++;
				}
			}
			ResultsLabel = $"Found {Results.Count} missing scripts in loaded scenes.";
			if (Results.Count == 1)
				ShowResult (Results [0]);
		}
	}

	static class ExtensionMethods {
		static void AppendParentName (ref StringBuilder sb, Transform t, char separator) {
			if (t.parent != null) {
				AppendParentName (ref sb, t.parent, separator);
			}
			sb.Append (separator);
			sb.Append (t.name);
		}

		/// <summary>
		/// Builds a 'path' to the specified GameObject in the scene hierarchy. Useful for logging/debugging
		/// </summary>
		public static string GetScenePath (this GameObject obj, char separator = '/') {
			var sb = new StringBuilder ();
			sb.Append (obj.scene.name);
			AppendParentName (ref sb, obj.transform, separator);
			return sb.ToString ();
		}

		/// <summary>
		/// Builds a 'path' to the specified Component's GameObject in the scene hierarchy. Useful for logging/debugging
		/// </summary>
		public static string GetScenePath (this Component comp, char separator = '/') {
			var sb = new StringBuilder ();
			sb.Append (comp.gameObject.scene.name);
			AppendParentName (ref sb, comp.transform, separator);
			return sb.ToString ();
		}

		/// <summary>
		/// Cap string length and if capped, add a postfix (defaults to ellipsis character)
		/// </summary>
		public static string Truncate (this string s, int maxLength, string postfix = "…") {
			if (s.Length <= maxLength) return s;
			return s.Substring (0, maxLength) + postfix;
		}

		/// <summary>
		/// Calculate the difference between 2 strings using the Damerau-Levenshtein distance algorithm
		/// </summary>
		public static int Levenshtein (this string source1, string source2) {  //O(n*m)
			var source1Length = source1.Length;
			var source2Length = source2.Length;

			var matrix = new int [source1Length + 1, source2Length + 1];

			// First calculation, if one entry is empty return full length
			if (source1Length == 0)
				return source2Length;

			if (source2Length == 0)
				return source1Length;

			// Initialization of matrix with row size source1Length and columns size source2Length
			for (var i = 0; i <= source1Length; matrix [i, 0] = i++) ;
			for (var j = 0; j <= source2Length; matrix [0, j] = j++) ;

			// Calculate row and column distances
			for (var i = 1; i <= source1Length; i++) {
				for (var j = 1; j <= source2Length; j++) {
					var cost = (source2 [j - 1] == source1 [i - 1]) ? 0 : 1;

					matrix [i, j] = Math.Min (
						Math.Min (matrix [i - 1, j] + 1, matrix [i, j - 1] + 1),
						matrix [i - 1, j - 1] + cost);

					//Damerau transposition
					if (source1 [i] == source2 [j - 1] && source1 [i - 1] == source2 [j])
						matrix [i, j] = Math.Min (matrix [i, j], matrix [i - 2, j - 2] + 1);
				}
			}
			return matrix [source1Length, source2Length];
		}
	}

	/// <summary>
	/// Custom Editor for properties. This enables us to display reference tooltips
	/// </summary>
	[CustomPropertyDrawer (typeof (bool), true)]
	[CustomPropertyDrawer (typeof (float), true)]
	[CustomPropertyDrawer (typeof (Vector3), true)]
	[CustomPropertyDrawer (typeof (Vector2), true)]
	[CustomPropertyDrawer (typeof (int), true)]
	[CustomPropertyDrawer (typeof (Enum), true)]
	[CustomPropertyDrawer (typeof (LayerMask), true)]
	public class CustomPropertyEditor : PropertyDrawer {
		public override void OnGUI (Rect position, SerializedProperty property, GUIContent label) {
			if (string.IsNullOrWhiteSpace (label.tooltip)) {
				label.tooltip = property.GetDocumentation ();
			}
			EditorGUI.PropertyField (position, property, label);
		}
	}

	/// <summary>
	/// Custom Editor for object properties. This enables us to highlight object search results (tags, layers etc)
	/// </summary>
	[CustomPropertyDrawer (typeof (UnityEngine.Object), true)]
	public class ObjectPropertyEditor : PropertyDrawer {
		public override void OnGUI (Rect position, SerializedProperty property, GUIContent label) {
			var identifier = $"{property.serializedObject.targetObject.GetInstanceID ()}.{property.propertyPath}";
			Highlighter.HighlightIdentifier (position, identifier);
			if (string.IsNullOrWhiteSpace (label.tooltip)) {
				label.tooltip = property.GetDocumentation ();
			}
			EditorGUI.PropertyField (position, property, label);
		}
	}

	/// <summary>
	/// Custom Editor for string properties. This enables us to highlight string search results
	/// </summary>
	[CustomPropertyDrawer (typeof (string), true)]
	public class StringPropertyEditor : PropertyDrawer {
		public override void OnGUI (Rect position, SerializedProperty property, GUIContent label) {
			var identifier = $"{property.serializedObject.targetObject.GetInstanceID ()}.{property.propertyPath}";
			Highlighter.HighlightIdentifier (position, identifier);
			if (string.IsNullOrWhiteSpace (label.tooltip)) {
				label.tooltip = property.GetDocumentation ();
			}
			EditorGUI.PropertyField (position, property, label);
		}
	}

	/// <summary>
	/// Custom Editor for the Text Component. This enables us to highlight string search results
	/// </summary>
	[CustomEditor (typeof (Text), true)]
	[CanEditMultipleObjects]
	public class CustomTextEditor : UnityEditor.UI.TextEditor {
		SerializedProperty m_Text;
		SerializedProperty m_FontData;

		protected override void OnEnable () {
			base.OnEnable ();
			m_Text = serializedObject.FindProperty ("m_Text");
			m_FontData = serializedObject.FindProperty ("m_FontData");
		}

		public override void OnInspectorGUI () {
			serializedObject.Update ();

			EditorGUILayout.PropertyField (m_Text);
			var identifier = $"{serializedObject.targetObject.GetInstanceID ()}.{m_Text.propertyPath}";
			Highlighter.HighlightIdentifier (GUILayoutUtility.GetLastRect (), identifier);

			EditorGUILayout.PropertyField (m_FontData);

			AppearanceControlsGUI ();
			RaycastControlsGUI ();
			serializedObject.ApplyModifiedProperties ();
		}
	}

	/*[CustomEditor (typeof (TMPro.TMP_Text), true)]
	[CanEditMultipleObjects]
	/// <summary>
	/// Custom Editor for the TMP_Text Component.
	/// </summary>
	public class CustomTextMeshProEditor : UnityEditor.UI.TextEditor {
		SerializedProperty m_Text;
		SerializedProperty m_FontData;

		protected override void OnEnable () {
			base.OnEnable ();
			m_Text = serializedObject.FindProperty ("m_Text");
			m_FontData = serializedObject.FindProperty ("m_FontData");
		}

		public override void OnInspectorGUI () {
			serializedObject.Update ();

			EditorGUILayout.PropertyField (m_Text);
			var identifier = $"{serializedObject.targetObject.GetInstanceID ()}.{m_Text.propertyPath}";
			Highlighter.HighlightIdentifier (GUILayoutUtility.GetLastRect (), identifier);

			EditorGUILayout.PropertyField (m_FontData);

			AppearanceControlsGUI ();
			RaycastControlsGUI ();
			serializedObject.ApplyModifiedProperties ();
		}
	}*/

	internal class WordIndex {
		private readonly Dictionary<string, HashSet<string>> IndexDictionary = new ();

		public WordIndex (IEnumerable<string> inputStrings) {
			BuildIndex (inputStrings);
		}

		/// <summary>
		/// Split each input string into words and build a dictionary [word]=[inputStrings containing that word]
		/// </summary>
		private void BuildIndex (IEnumerable<string> inputStrings) {
			var sb = new StringBuilder ();
			foreach (var str in inputStrings) {
				sb.Clear ();
				foreach (char c in str) {
					if (char.IsLetterOrDigit (c)) {
						sb.Append (char.ToLowerInvariant (c));
						continue;
					}
					if (sb.Length > 0) {
						var s = sb.ToString ();
						sb.Clear ();
						if (!IndexDictionary.TryGetValue (s, out HashSet<string> hs))
							hs = new ();
						hs.Add (str);
						IndexDictionary [s] = hs;
					}
				}
			}
		}

		internal class KeyWeightSet {
			public int Weight;
			public KeyValuePair<string, HashSet<string>> KeyValuePair;
		}

		/// <summary>
		/// Return an enumeration of source strings ordered by levenshtein distance to key words
		/// </summary>
		public IEnumerable<string> Lookup (string inputString, int minResults = 10) {
			var keyWeights = IndexDictionary.Select (kvp => new KeyWeightSet { KeyValuePair = kvp, Weight = 0 });
			var resultsHashSet = new HashSet<string> ();
			var results = new List<string> (minResults);
			var sb = new StringBuilder ();
			foreach (char c in inputString) {
				if (char.IsLetterOrDigit (c)) {
					sb.Append (c);
					continue;
				}
				if (sb.Length > 0) {
					var s = sb.ToString ();
					foreach (var k in keyWeights) {
						k.Weight += k.KeyValuePair.Key.Levenshtein (s);
					}
				}
			}

			keyWeights = keyWeights.OrderBy (k => k.Weight);
			foreach (var k in keyWeights) {
				var kvp = k.KeyValuePair;
				foreach (var v in kvp.Value) {
					if (resultsHashSet.Add (v)) {
						results.Add (v);
					}
				}
			}

			return results;
		}
	}

	/// <summary>
	/// Custom AssetModificationProcessor. This enables us to fill out the m_EditorClassIdentifier and m_Name properties on MonoBehaviours so we can later track missing scripts
	/// </summary>
	[InitializeOnLoad]
	internal class MyAssetModificationProcessor : AssetModificationProcessor {
		static MyAssetModificationProcessor () {
			EditorSceneManager.sceneOpened -= OnSceneOpened;
			EditorSceneManager.sceneOpened += OnSceneOpened;
		}

		static void OnSceneOpened (UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode) {
			ProcessAllLoadedMonoBehaviours ();
		}

		static void ProcessAllLoadedMonoBehaviours () {
			var mbs = UnityEngine.Object.FindObjectsOfType<MonoBehaviour> (true);
			foreach (var mb in mbs) {
				if (mb == null)
					continue;
				var type = mb.GetType ();
				var so = new SerializedObject (mb);
				var editorClassIdentifier = $"{type.Assembly.GetName ().Name}::{type.FullName}";
				so.FindProperty ("m_Name").stringValue = type.Name;
				so.FindProperty ("m_EditorClassIdentifier").stringValue = editorClassIdentifier;
				so.ApplyModifiedPropertiesWithoutUndo ();
			}
		}

		public static string [] OnWillSaveAssets (string [] paths) {
			if (paths.Any (p => p.EndsWith (".unity"))) {
				ProcessAllLoadedMonoBehaviours ();
			}
			return paths;
		}
	}

	/// <summary>
	/// Custom MonoBehaviour editor. This enables us to print the last known Type, but only if the script has gone missing AFTER this plugin was added
	/// </summary>
	[CustomEditor (typeof (MonoBehaviour), true)]
	class MissingMonoBehaviourEditor : Editor {
		string Identifier;
		bool IsMissingScript;
		string TypeString;

		protected virtual void OnEnable () {
			var scriptProperty = serializedObject.FindProperty ("m_Script");
			var targetScript = scriptProperty?.objectReferenceValue as MonoScript;
			IsMissingScript = (targetScript == null);

			var fileID = target.GetInstanceID ();
			Identifier = $"{fileID}.m_Script";

			TypeString = serializedObject.FindProperty ("m_EditorClassIdentifier")?.stringValue ?? serializedObject.FindProperty ("m_Name")?.stringValue;
		}

		public override void OnInspectorGUI () {
			if (IsMissingScript) {
				GUILayout.BeginVertical ();

				using (new EditorGUI.DisabledScope (true)) {
					EditorGUILayout.TextField ("Type: ", !string.IsNullOrWhiteSpace (TypeString) ? TypeString : "N/A");
				}

				var text = L10n.Tr ("The associated script can not be loaded.\nPlease fix any compile errors\nand assign a valid script.");
				EditorGUILayout.HelpBox (text, MessageType.Warning, true);

				GUILayout.EndVertical ();
				Highlighter.HighlightIdentifier (GUILayoutUtility.GetLastRect (), Identifier);
			} else {
				base.OnInspectorGUI ();
			}
		}
	}
}
