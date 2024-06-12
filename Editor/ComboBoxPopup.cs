using System.Reflection;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace AranciaAssets.EditorTools {

	public class ComboBoxPopup : EditorWindow {

		public static ComboBoxPopup Instance;

		IEnumerable<string> Values;
		static int CurrentIndex = -1;
		static GUIStyle Style, HoverStyle;
		static float LineHeight;
		static Texture2D SolidTexture;

		/// <summary>
		/// Copy of internal enum ShowMode https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/ContainerWindow.bindings.cs
		/// </summary>
		internal enum ShowMode {
			// Show as a normal window with max, min & close buttons.
			NormalWindow = 0,
			// Used for a popup menu. On mac this means light shadow and no titlebar.
			PopupMenu = 1,
			// Utility window - floats above the app. Disappears when app loses focus.
			Utility = 2,
			// Window has no shadow or decorations. Used internally for dragging stuff around.
			NoShadow = 3,
			// The Unity main window. On mac, this is the same as NormalWindow, except window doesn't have a close button.
			MainWindow = 4,
			// Aux windows. The ones that close the moment you move the mouse out of them.
			AuxWindow = 5,
			// Like PopupMenu, but without keyboard focus
			Tooltip = 6,
			// Modal Utility window
			ModalUtility = 7
		}

		public ComboBoxPopup () {
			Instance = this;
		}

		public delegate void Callback (string value);
		Callback onSelect;

		public static void Show (IEnumerable<string> values, Rect guiRect, Callback callback) {
			if (values.Count () == 0)
				return;
			if (Instance == null) {
				SolidTexture = new Texture2D (1, 1);
				SolidTexture.SetPixel (0, 0, Color.gray);
				SolidTexture.Apply ();

				Style = new GUIStyle (GUI.skin.label);
				HoverStyle = new GUIStyle (GUI.skin.label);
				HoverStyle.normal.background = SolidTexture;
				HoverStyle.fontStyle = FontStyle.Bold;

				var givefocus = false;
				Instance = CreateInstance<ComboBoxPopup> ();
				var miShowWithMode = typeof (EditorWindow).GetMethod ("ShowPopupWithMode", BindingFlags.Instance | BindingFlags.NonPublic);
				miShowWithMode.Invoke (Instance, new object [] { (int)ShowMode.PopupMenu, givefocus });
			} else {
				Instance.Repaint ();
			}
			LineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing - 1f;
			var screenRect = GUIUtility.GUIToScreenRect (guiRect);
			screenRect.y += EditorGUIUtility.singleLineHeight;
			screenRect.height = LineHeight * Mathf.Min (values.Count (), 10);
			Instance.position = screenRect;
			Instance.Values = values;
			Instance.onSelect = callback;
			Instance.wantsMouseMove = Instance.wantsMouseEnterLeaveWindow = true;

			CurrentIndex = 0;
		}


		public static void Hide () {
			if (Instance != null) {
				Instance.Close ();
				Instance = null;
			}
		}

		private void OnGUI () {
			if (Values == null || Values.Count () == 0) {
				Close ();
				return;
			}
			var numLines = Mathf.Min (Values.Count (), 10);

			var evt = Event.current;
			if (evt.type == EventType.MouseMove) {
				var ci = (int)(evt.mousePosition.y / LineHeight);
				if (CurrentIndex != ci) {
					CurrentIndex = ci;
					Repaint ();
				}
			} else if (evt.type == EventType.MouseDown) {
				CurrentIndex = (int)(evt.mousePosition.y / LineHeight);
				SelectCurrent ();
				return;
			} else if (evt.type == EventType.KeyDown) {
				if (evt.keyCode == KeyCode.DownArrow && CurrentIndex < Values.Count () - 1) CurrentIndex++;
				else if (evt.keyCode == KeyCode.UpArrow && CurrentIndex > 0) CurrentIndex--;
			}

			var i = 0;
			foreach (var value in Values) {
				GUILayout.Label (value, i == CurrentIndex ? HoverStyle : Style);
				if (++i == numLines)
					break;
			}
		}

		public void MoveUp () {
			if (CurrentIndex != 0) {
				CurrentIndex--;
				Repaint ();
			}
		}

		public void MoveDown () {
			var numLines = Mathf.Min (Values.Count (), 10);
			if (CurrentIndex < numLines - 1) {
				CurrentIndex++;
				Repaint ();
			}
		}

		public void SelectCurrent () {
			onSelect.Invoke (Values.ElementAt (CurrentIndex));
			Close ();
		}

		public static bool HandleEvent (Event e) {
			if (!(e.type == EventType.KeyDown && (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.Escape)))
				return false;
			var kc = e.keyCode;
			e.Use ();
			switch (kc) {
			case KeyCode.UpArrow:
				Instance.MoveUp ();
				break;
			case KeyCode.DownArrow:
				Instance.MoveDown ();
				break;
			case KeyCode.Escape:
				Instance.Close ();
				break;
			case KeyCode.Return:
				Instance.SelectCurrent ();
				break;
			}
			return true;
		}
	}
}