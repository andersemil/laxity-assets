using UnityEditor;
using UnityEngine;

namespace AranciaAssets.EditorTools {

	/// <summary>
	/// BaseComponent editor. Draws a reference button (if there is a ComponentReferenceAttribute on the class) and hides the Script property
	/// </summary>
	[CustomEditor (typeof (AranciaBehaviour), editorForChildClasses: true)]
	public class AranciaBehaviourEditor : Editor {
		private bool EditingComment;
		private GUIContent EditIcon, OkIcon;
		private GUIStyle EditButtonStyle, TextAreaStyle, LabelStyle;
		private const string CommentControlName = "AranciaCommentControl";
		private static Color CommentColor = new (1f, .7f, .2f);
		private static Color CommentLineColor = new (1f, .7f, .2f, .5f);
		private string LastFocusedName;
		private bool LastEditingComment;
		private SerializedProperty CommentProp;

		private Texture2D _transparentTex;
		private Texture2D TransparentTex {
			get {
				if (_transparentTex == null) {
					_transparentTex = new Texture2D (1, 1);
					_transparentTex.SetPixel (0, 0, new Color (0, 0, 0, 0)); // Pure transparent
					_transparentTex.Apply ();
				}
				return _transparentTex;
			}
		}

		protected virtual void OnEnable () {
			EditIcon = new GUIContent (EditorGUIUtility.IconContent ("d_editicon.sml")) {
				tooltip = "Edit Comment",
			};
			OkIcon = new GUIContent (EditorGUIUtility.IconContent ("d_FilterSelectedOnly")) {
				tooltip = "OK",
			};
			//EditorApplication.update += CheckGlobalFocus;
			CommentProp = serializedObject.FindProperty ("AranciaComment");
		}

		protected virtual void OnDisable () {
			Debug.Log ("OnDisable");
			EditorApplication.update -= CheckGlobalFocus;
		}

		void CheckGlobalFocus () {
			if (EditingComment) {
				EditingComment = false;
				EditorUtility.SetDirty (target);
				Repaint ();
			}
		}

		protected void DrawReferenceButton()
		{
			if (EditIcon == null)
				return;

			if (EditButtonStyle == null) {
				//For some reason EditorStyles cannot be accessed from OnEnable
				EditButtonStyle = new GUIStyle (EditorStyles.iconButton) {
					padding = new RectOffset (0, 0, 0, 0)
				};
			}
			if (TextAreaStyle == null) {
				TextAreaStyle = new GUIStyle (EditorStyles.textArea);

				Color targetColor = CommentColor;
				TextAreaStyle.normal.textColor =
				TextAreaStyle.onNormal.textColor =
				TextAreaStyle.hover.textColor =
				TextAreaStyle.onHover.textColor =
				TextAreaStyle.focused.textColor =
				TextAreaStyle.onFocused.textColor =
				TextAreaStyle.active.textColor =
				TextAreaStyle.onActive.textColor = targetColor;

				TextAreaStyle.fontSize = 10;
				TextAreaStyle.alignment = TextAnchor.UpperLeft;
				TextAreaStyle.wordWrap = true;

				TextAreaStyle.padding = TextAreaStyle.margin = new RectOffset ();

				LabelStyle = new GUIStyle (EditorStyles.label);
				LabelStyle.normal.background =
				LabelStyle.onNormal.background =
				LabelStyle.hover.background =
				LabelStyle.onHover.background =
				LabelStyle.focused.background =
				LabelStyle.onFocused.background =
				LabelStyle.active.background =
				LabelStyle.onActive.background = TransparentTex;

				LabelStyle.normal.textColor =
				LabelStyle.onNormal.textColor =
				LabelStyle.hover.textColor =
				LabelStyle.onHover.textColor =
				LabelStyle.focused.textColor =
				LabelStyle.onFocused.textColor =
				LabelStyle.active.textColor =
				LabelStyle.onActive.textColor = targetColor;

				LabelStyle.fontSize = 10;
				LabelStyle.alignment = TextAnchor.UpperLeft;
				LabelStyle.wordWrap = true;

				LabelStyle.padding = LabelStyle.margin = new RectOffset ();
			}

			var commentString = CommentProp.stringValue;
			GUILayout.BeginHorizontal ();
			GUILayout.Space (30f);
			if (!EditingComment) {
				EditorGUILayout.LabelField (commentString, LabelStyle);
				DrawCommentLine ();
				if (GUILayout.Button (EditIcon, EditButtonStyle)) {
					EditingComment = true;
					Repaint ();
				}
				GUILayout.EndHorizontal ();
			} else {
				EditorGUI.BeginChangeCheck ();
				GUI.SetNextControlName (CommentControlName);
				int controlID = GUIUtility.GetControlID (FocusType.Keyboard);
				commentString = EditorGUILayout.TextArea (commentString, TextAreaStyle);
				var changed = EditorGUI.EndChangeCheck ();
				DrawCommentLine ();

				if (!LastEditingComment) {
					EditorGUI.FocusTextInControl (CommentControlName);
					TextEditor te = (TextEditor)GUIUtility.GetStateObject (typeof (TextEditor), controlID);
					if (te != null) {
						Debug.Log ("OnFocus");
						te.OnFocus (); // This is the "secret" call that activates keyboard input
					}
					Repaint ();
				}
				var focusedName = GUI.GetNameOfFocusedControl ();
				//Debug.Log ($"event={Event.current.type} Focused={focusedName} LastFocusedName={LastFocusedName}");
				GUI.SetNextControlName ("AranciaButton");
				if (GUILayout.Button (OkIcon, EditButtonStyle)
					/*|| (LastFocusedName == CommentControlName && focusedName != CommentControlName)*/) {
					EditingComment = false;
					Repaint ();
				}
				if (Event.current.type == EventType.Repaint) {
					LastFocusedName = focusedName;
					LastEditingComment = EditingComment;
				}
				GUILayout.EndHorizontal ();
				//GUILayout.Space (2f);

				if (changed) {
					CommentProp.stringValue = commentString;
					serializedObject.ApplyModifiedProperties ();
				}
			}
			GUILayout.Space (8f);
		}

		void DrawCommentLine () {
			var lineRect = GUILayoutUtility.GetLastRect ();
			lineRect.xMin -= 8f;
			lineRect.xMax = lineRect.xMin + 1f;
			EditorGUI.DrawRect (lineRect, CommentLineColor);
		}

		public override void OnInspectorGUI () {
			serializedObject.Update ();

			DrawReferenceButton ();

			//var instanceID = serializedObject.targetObject.GetInstanceID ();

			/*var obj = serializedObject.GetIterator ();
			if (obj.NextVisible (true)) {
				do {
					if (obj.name == "m_Script")
						continue;
					EditorGUILayout.PropertyField (obj, true);
					//Highlighter.HighlightIdentifier (GUILayoutUtility.GetLastRect (), $"{instanceID}.{obj.propertyPath}");
				} while (obj.NextVisible (false));
			}

			serializedObject.ApplyModifiedProperties ();*/
			base.OnInspectorGUI ();
		}
	}

	/*[InitializeOnLoad]
	public static class InspectorInjector {
		static InspectorInjector () {
			Editor.finishedDefaultHeaderGUI -= OnPostHeaderGUI;
			Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
		}

		static void OnPostHeaderGUI (Editor editor) {
			EditorGUILayout.HelpBox ("This is injected via finishedDefaultHeaderGUI!", MessageType.Info);

			if (GUILayout.Button ("Custom Tool Button")) {
				Debug.Log ($"Clicked on {editor.target.name}");
			}

			EditorGUILayout.Space (5);
		}
	}*/
}
