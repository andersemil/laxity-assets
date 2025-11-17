using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;

namespace AranciaAssets.EditorTools {

    /// <summary>
    /// Custom override of the standard UnityEventDrawer.
	/// 1) The layout of the fields is customized
	/// 2) When you drop a new target, we try to find a match for the target-type and method, rather than just reset the event
    /// </summary>
    [CustomPropertyDrawer (typeof (UnityEventBase), true)]
    public class BetterEventDrawer : UnityEventDrawer {

        private const string kNoFunctionString = "No Function";

        //Persistent Listener Paths
        const string kInstancePath = "m_Target";
        const string kInstanceTypePath = "m_TargetAssemblyTypeName";
        const string kCallStatePath = "m_CallState";
        const string kArgumentsPath = "m_Arguments";
        const string kModePath = "m_Mode";
        const string kMethodNamePath = "m_MethodName";

        //ArgumentCache paths
        const string kFloatArgument = "m_FloatArgument";
        const string kIntArgument = "m_IntArgument";
        const string kObjectArgument = "m_ObjectArgument";
        const string kStringArgument = "m_StringArgument";
        const string kBoolArgument = "m_BoolArgument";
        const string kObjectArgumentAssemblyTypeName = "m_ObjectArgumentAssemblyTypeName";

        static readonly GUIContent s_MixedValueContent = EditorGUIUtility.TrTextContent ("\u2014", "Mixed Values");

        GUIContent m_HeaderContent;

        UnityEventBase DummyEvent { get { return fiDummyEvent.GetValue (this) as UnityEventBase; } }
        object PopupList;

        static bool reflectionInitialized;
        static MethodInfo miGetEventParams;
        static MethodInfo miBuildPopupList;
        static FieldInfo fiPopupListMenu;
        static FieldInfo fiPopupListApplyAction;
        static FieldInfo fiListenersArray;
        static FieldInfo fiDummyEvent;

        /// <summary>
        /// Arguments to supply to internal method "FindMethod"
        /// </summary>
        static readonly Type [] FindMethodArguments = new Type [] { typeof (string), typeof (Type), typeof (PersistentListenerMode), typeof (Type) };

        /// <summary>
        /// Toggle menu item path
        /// </summary>
        private const string OpdMenuName = "Tools/Arancia/Better Event Layout";
        private const string OpdKey = "BetterEventDrawer";

        ReorderableList ReorderableList;

        [MenuItem (OpdMenuName)]
        public static void ToggleOpd () {
            var opp = EditorPrefs.GetBool (OpdKey, true);
            EditorPrefs.SetBool (OpdKey, !opp);
            ActiveEditorTracker.sharedTracker.ForceRebuild ();
        }

        [MenuItem (OpdMenuName, true)]
        private static bool ToggleOpdValidate () {
            Menu.SetChecked (OpdMenuName, EditorPrefs.GetBool (OpdKey, true));
            return true;
        }

        /// <summary>
		/// Setup Rects used for laying out each event listener
		/// </summary>
        Rect [] GetRowRects (Rect rect, PersistentListenerMode mode) {
            Rect [] rects = new Rect [4];
            rect.height = EditorGUIUtility.singleLineHeight;
            Rect enabledRect = rect;
            Rect functionRect = rect;
            var useLegacyLayout = !EditorPrefs.GetBool (OpdKey, true);

            if (useLegacyLayout)
                enabledRect.width *= .3f;
            else
                enabledRect.width = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            Rect goRect = rect;
            if (useLegacyLayout) {
                goRect = enabledRect;
            } else if (mode == PersistentListenerMode.Bool) {
                goRect.width -= EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            } else if (mode != PersistentListenerMode.Void && mode != PersistentListenerMode.EventDefined) {
                goRect.width *= .5f;
            }
            goRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            functionRect.xMin = enabledRect.xMax + EditorGUIUtility.standardVerticalSpacing; //+ EditorGUI.kSpacing;

            Rect argRect = functionRect;
            if (!useLegacyLayout)
                argRect.xMin = goRect.xMax + EditorGUIUtility.standardVerticalSpacing; //+ EditorGUI.kSpacing;
            argRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            rects [0] = enabledRect;
            rects [1] = useLegacyLayout ? goRect : functionRect;
            rects [2] = useLegacyLayout ? functionRect : goRect;
            rects [3] = argRect;
            return rects;
        }

        protected override void SetupReorderableList (ReorderableList list) {
            base.SetupReorderableList (list);
            ReorderableList = list;

            if (EditorPrefs.GetBool (OpdKey, true)) {
                //Squeeze elements a little tighter together compared to normal UnityEvents
                list.elementHeight = (EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing);
            }

            if (!reflectionInitialized) {
                reflectionInitialized = true;
                var tUnityEventDrawer = typeof (UnityEventDrawer);
                miGetEventParams = tUnityEventDrawer.GetMethod ("GetEventParams", BindingFlags.NonPublic | BindingFlags.Static);
                miBuildPopupList = tUnityEventDrawer.GetMethod ("BuildPopupList", BindingFlags.Static | BindingFlags.NonPublic);
                var tPopupList = tUnityEventDrawer.GetNestedType ("PopupList", BindingFlags.NonPublic);
                fiPopupListMenu = tPopupList?.GetField ("menu");
                fiPopupListApplyAction = tPopupList?.GetField ("applyItemSelectedAction");
                fiListenersArray = tUnityEventDrawer.GetField ("m_ListenersArray", BindingFlags.NonPublic | BindingFlags.Instance);
                fiDummyEvent = tUnityEventDrawer.GetField ("m_DummyEvent", BindingFlags.NonPublic | BindingFlags.Instance);
            }
        }

        private SerializedProperty GetArgument (SerializedProperty pListener) {
            var listenerTarget = pListener.FindPropertyRelative (kInstancePath);
            var methodName = pListener.FindPropertyRelative (kMethodNamePath);
            var mode = pListener.FindPropertyRelative (kModePath);
            var arguments = pListener.FindPropertyRelative (kArgumentsPath);
			var modeEnum = (PersistentListenerMode)mode.enumValueIndex;
            //only allow argument if we have a valid target / method
            if (listenerTarget.objectReferenceValue == null || string.IsNullOrEmpty (methodName.stringValue))
                modeEnum = PersistentListenerMode.Void;
			SerializedProperty argument = modeEnum switch {
				PersistentListenerMode.Float => arguments.FindPropertyRelative (kFloatArgument),
				PersistentListenerMode.Int => arguments.FindPropertyRelative (kIntArgument),
				PersistentListenerMode.Object => arguments.FindPropertyRelative (kObjectArgument),
				PersistentListenerMode.String => arguments.FindPropertyRelative (kStringArgument),
				PersistentListenerMode.Bool => arguments.FindPropertyRelative (kBoolArgument),
				_ => arguments.FindPropertyRelative (kIntArgument),
			};
			return argument;
        }

        private void GetFunctionDropdownText (SerializedProperty pListener, out string functionName, out string xmlDoc) {
            xmlDoc = null;
            var listenerTarget = pListener.FindPropertyRelative (kInstancePath);
            var methodName = pListener.FindPropertyRelative (kMethodNamePath);
            var mode = (PersistentListenerMode)pListener.FindPropertyRelative (kModePath).enumValueIndex;
            var arguments = pListener.FindPropertyRelative (kArgumentsPath);
            var desiredArgTypeName = arguments.FindPropertyRelative (kObjectArgumentAssemblyTypeName).stringValue;
            var desiredType = typeof (UnityEngine.Object);
            if (!string.IsNullOrEmpty (desiredArgTypeName))
                desiredType = Type.GetType (desiredArgTypeName, false) ?? desiredType;

            var buttonLabel = new StringBuilder ();
            if (listenerTarget.objectReferenceValue == null || string.IsNullOrEmpty (methodName.stringValue)) {
                buttonLabel.Append (kNoFunctionString);
            } else if (!IsPersistantListenerValid (DummyEvent, methodName.stringValue, listenerTarget.objectReferenceValue, mode, desiredType)) {
                var instanceString = "UnknownComponent";
                var instance = listenerTarget.objectReferenceValue;
                if (instance != null)
                    instanceString = instance.GetType ().Name;

                buttonLabel.Append (string.Format ("<Missing {0}.{1}>", instanceString, methodName.stringValue));
            } else {
                var listenerTargetType = listenerTarget.objectReferenceValue.GetType ();
                try {
                    if (methodName.stringValue.StartsWith ("set_")) {
                        var pi = listenerTargetType.GetProperty (methodName.stringValue.Substring (4), BindingFlags.Instance | BindingFlags.Public);
                        if (pi != null) {
                            xmlDoc = pi.GetDocumentation ();
                        }
                    } else {
                        //var mi = listenerTargetType.GetMethod (methodName.stringValue, BindingFlags.Instance | BindingFlags.Public);
                        //var mi = listenerTargetType.GetMethod (methodName.stringValue, BindingFlags.Instance | BindingFlags.Public/*, null, new Type [] { desiredType }, null*/);
                        var mi = listenerTargetType.GetMethods (BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).FirstOrDefault (m => m.Name == methodName.stringValue);
                        if (mi != null) {
                            if (mode == PersistentListenerMode.EventDefined) {
                                var miFindMethod = DummyEvent.GetType ().GetMethod ("FindMethod", BindingFlags.Instance | BindingFlags.NonPublic, null, FindMethodArguments, null);
                                var miEventMethod = miFindMethod.Invoke (DummyEvent, new object [] { "Invoke", DummyEvent.GetType (), PersistentListenerMode.EventDefined, null }) as MethodInfo;
                                var argType = miEventMethod.GetParameters () [0].ParameterType;
                                do {
                                    xmlDoc = mi.GetDocumentation ($"({argType})");
                                    if (!string.IsNullOrWhiteSpace (xmlDoc))
                                        break;
                                    argType = argType.BaseType;
                                } while (argType != typeof (object));
                            } else {
#pragma warning disable 8524
                                var argumentString = mode switch {
                                    PersistentListenerMode.Bool => "(System.Boolean)",
                                    PersistentListenerMode.Int => "(System.Int32)",
                                    PersistentListenerMode.Float => "(System.Single)",
                                    PersistentListenerMode.String => "(System.String)",
                                    PersistentListenerMode.Object => $"({desiredType})",
                                    _ => string.Empty,
#pragma warning restore
                                };
                                xmlDoc = mi.GetDocumentation (argumentString);
                            }
                        } else {
                            //Debug.Log ("Did not find MethodInfo for " + methodName.stringValue);
                        }
                    }
                } catch (AmbiguousMatchException) {
                    //For now we don't care about this. We will figure out a better way to match the methods later
                    Debug.Log ("Ambiguous match for " + methodName.stringValue);
                }

                buttonLabel.Append (listenerTargetType.Name);

                if (!string.IsNullOrEmpty (methodName.stringValue)) {
                    buttonLabel.Append (".");
                    if (methodName.stringValue.StartsWith ("set_"))
                        buttonLabel.Append (methodName.stringValue.Substring (4));
                    else
                        buttonLabel.Append (methodName.stringValue);
                }
            }

            functionName = buttonLabel.ToString ();
        }

        /// <summary>
        /// Attempt to find new target object to match the original type
        /// </summary>
        protected void FindMethod (SerializedProperty listenerTarget, string assemblyTypeName, SerializedProperty methodName) {
            var target = listenerTarget.objectReferenceValue;
            if (target != null) {
                var desiredType = Type.GetType (assemblyTypeName, false);
                if (desiredType != null && desiredType != typeof (GameObject) && desiredType != typeof (UnityEngine.Object)) {
                    if (target is GameObject go) {
                        target = go.GetComponent (desiredType);
                    } else if (target is Component c) {
                        target = c.GetComponent (desiredType);
                    }
                }
            }
            if (target != null) {
                listenerTarget.objectReferenceValue = target;
            } else {
                methodName.stringValue = null;
            }
        }

        Rect TotalRect = new ();
        void GrowTotalRect (Rect other) {
            if (TotalRect.xMin > other.xMin) TotalRect.xMin = other.xMin;
            if (TotalRect.xMax < other.xMax) TotalRect.xMax = other.xMax;
            if (TotalRect.yMin > other.yMin) TotalRect.yMin = other.yMin;
            if (TotalRect.yMax < other.yMax) TotalRect.yMax = other.yMax;
        }

        protected void OnAddListenerTargets (UnityEngine.Object [] targets) {
            var list = ReorderableList;
            var index = list.count;
            var listenersArray = list.serializedProperty;
            if (listenersArray.hasMultipleDifferentValues) {
                //The Serialization system applies the Serialized data of one object to all other objects in the selection.
                //We handle this by creating a SerializedObject for each object and applying the modifications individually
                foreach (var targetObject in listenersArray.serializedObject.targetObjects) {
                    using var temSerializedObject = new SerializedObject (targetObject);
                    var listenerArrayProperty = temSerializedObject.FindProperty (listenersArray.propertyPath);
                    listenerArrayProperty.arraySize += targets.Length;
                    temSerializedObject.ApplyModifiedProperties ();
                }
                listenersArray.serializedObject.SetIsDifferentCacheDirty ();
                listenersArray.serializedObject.Update ();
                list.index = list.serializedProperty.arraySize - targets.Length;
            } else {
                for (int i = 0; i < targets.Length; i++) {
                    ReorderableList.defaultBehaviours.DoAddButton (list);
                }
            }

            foreach (var target in targets) {
                SetupListenerForTarget (index, target);
                index++;
            }
        }

        void SetupListenerForTarget (int index, UnityEngine.Object target) {
            var listenersArray = ReorderableList.serializedProperty;
            var pListener = listenersArray.GetArrayElementAtIndex (index);
            var callState = pListener.FindPropertyRelative (kCallStatePath);
            var listenerTarget = pListener.FindPropertyRelative (kInstancePath);
            var methodName = pListener.FindPropertyRelative (kMethodNamePath);
            var mode = pListener.FindPropertyRelative (kModePath);
            var arguments = pListener.FindPropertyRelative (kArgumentsPath);

            callState.enumValueIndex = (int)UnityEventCallState.RuntimeOnly;
            listenerTarget.objectReferenceValue = target;
            methodName.stringValue = null;
            mode.enumValueIndex = (int)PersistentListenerMode.Void;
            arguments.FindPropertyRelative (kFloatArgument).floatValue = 0;
            arguments.FindPropertyRelative (kIntArgument).intValue = 0;
            arguments.FindPropertyRelative (kObjectArgument).objectReferenceValue = null;
            arguments.FindPropertyRelative (kStringArgument).stringValue = null;
            arguments.FindPropertyRelative (kObjectArgumentAssemblyTypeName).stringValue = null;
        }

        public override void OnGUI (Rect position, SerializedProperty property, GUIContent label) {
            TotalRect = position;
            var headerContent = label.text;
            base.OnGUI (position, property, label);
            if (m_HeaderContent == null) {
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D> (AssetDatabase.GUIDToAssetPath ("db4aaf330ef7e43858c9c49ff984c8c3"));
                var tooltip = label.tooltip;
                if (string.IsNullOrWhiteSpace (tooltip)) {
                    tooltip = property.GetDocumentation ();
                }
                m_HeaderContent = new GUIContent (headerContent + miGetEventParams.Invoke (this, new object [] { DummyEvent }), icon, tooltip);
            }

            var evt = Event.current;
            if ((evt.type == EventType.DragPerform || evt.type == EventType.DragUpdated) && TotalRect.Contains (evt.mousePosition)) {
                var sceneObjects = DragAndDrop.objectReferences.Where (t => t is GameObject go && !AssetDatabase.Contains (go));
                if (sceneObjects.Count () != 0) {
                    if (evt.type == EventType.DragPerform) {
                        OnAddListenerTargets (sceneObjects.ToArray ());
                        DragAndDrop.AcceptDrag ();
                        evt.Use ();
                    }
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                } else {
                    DragAndDrop.visualMode = DragAndDropVisualMode.None;
                }
            } else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Delete) {
                ReorderableList.defaultBehaviours.DoRemoveButton (ReorderableList);
            }
        }

        protected override void DrawEventHeader (Rect headerRect) {
            if (m_HeaderContent != null) {
                headerRect.height = EditorGUIUtility.singleLineHeight;
                GrowTotalRect (headerRect);
                GUI.Label (headerRect, m_HeaderContent);
            }
        }

        protected override void DrawEvent (Rect rect, int index, bool isActive, bool isFocused) {
            GrowTotalRect (rect);

            var pListener = ((SerializedProperty)fiListenersArray.GetValue (this)).GetArrayElementAtIndex (index);
            Highlighter.HighlightIdentifier (rect, $"{pListener.serializedObject.targetObject.GetInstanceID ()}.{pListener.propertyPath}");

            var mode = (PersistentListenerMode)pListener.FindPropertyRelative (kModePath).enumValueIndex;

            rect.y++;
            var subRects = GetRowRects (rect, mode);
            var enabledRect = subRects [0];
            var goRect = subRects [1];
            var functionRect = subRects [2];
            var argRect = subRects [3];

            // find the current event target...
            var callState = pListener.FindPropertyRelative (kCallStatePath);
            var arguments = pListener.FindPropertyRelative (kArgumentsPath);
            var listenerTarget = pListener.FindPropertyRelative (kInstancePath);
            var methodName = pListener.FindPropertyRelative (kMethodNamePath);

            var c = GUI.backgroundColor;
            GUI.backgroundColor = Color.white;

            EditorGUI.PropertyField (enabledRect, callState, GUIContent.none);

            EditorGUI.BeginChangeCheck ();
            {
                GUI.Box (goRect, GUIContent.none);
                var targetAssemblyTypeName = pListener.FindPropertyRelative (kInstanceTypePath).stringValue;

                EditorGUI.PropertyField (goRect, listenerTarget, GUIContent.none);
                if (EditorGUI.EndChangeCheck ())
                    FindMethod (listenerTarget, targetAssemblyTypeName, methodName);
            }

            var argument = GetArgument (pListener);
            //only allow argument if we have a valid target / method
            if (listenerTarget.objectReferenceValue == null || string.IsNullOrEmpty (methodName.stringValue))
                mode = PersistentListenerMode.Void;

            var desiredArgTypeName = arguments.FindPropertyRelative (kObjectArgumentAssemblyTypeName).stringValue;
            var desiredType = typeof (UnityEngine.Object);
            if (!string.IsNullOrEmpty (desiredArgTypeName))
                desiredType = Type.GetType (desiredArgTypeName, false) ?? desiredType;

            GUIContent functionContent;
            if (EditorGUI.showMixedValue) {
                functionContent = s_MixedValueContent;
            } else {
                GetFunctionDropdownText (pListener, out string buttonLabel, out string xmlDoc);
                functionContent = new GUIContent (buttonLabel, buttonLabel);
                if (!string.IsNullOrWhiteSpace (xmlDoc)) {
                    functionContent.tooltip = xmlDoc;
                }
            }

            var hasArgument = mode != PersistentListenerMode.Void && mode != PersistentListenerMode.EventDefined;

            if (EditorPrefs.GetBool (OpdKey, true)) {
                if (!hasArgument)
                    functionRect.width = rect.width;
                else if (mode == PersistentListenerMode.Bool)
                    functionRect.width = rect.width - argRect.width;
                else
                    functionRect.width = Mathf.Min (rect.width * (hasArgument ? .7f : 1f), EditorStyles.popup.CalcSize (functionContent).x);
                argRect.xMin = rect.xMin + functionRect.width + EditorGUIUtility.standardVerticalSpacing;
            }

            if (mode == PersistentListenerMode.Object) {
                EditorGUI.BeginChangeCheck ();
                var result = EditorGUI.ObjectField (argRect, GUIContent.none, argument.objectReferenceValue, desiredType, true);
                if (EditorGUI.EndChangeCheck ())
                    argument.objectReferenceValue = result;
            } else if (hasArgument)
                EditorGUI.PropertyField (argRect, argument, GUIContent.none);

            using (new EditorGUI.DisabledScope (listenerTarget.objectReferenceValue == null)) {
                EditorGUI.BeginProperty (functionRect, GUIContent.none, methodName);
                {
                    var eventType = Event.current.type;
                    if (EditorGUI.DropdownButton (functionRect, functionContent, FocusType.Passive, EditorStyles.popup)) {
                        var list = miBuildPopupList.Invoke (this, new object [] { listenerTarget.objectReferenceValue, DummyEvent, pListener });
                        GenericMenu menu;
                        if (fiPopupListMenu != null) {
                            PopupList = list;
                            menu = (GenericMenu)fiPopupListMenu.GetValue (PopupList);
                        } else {
                            menu = (GenericMenu)list;
                        }
                        menu.DropDown (functionRect);
                    }
                    if (PopupList != null && eventType != EventType.Layout) {
                        var action = fiPopupListApplyAction.GetValue (PopupList);
                        if (action != null) {
                            ((Action)action).Invoke ();
                            fiPopupListApplyAction.SetValue (PopupList, null);
                        }
                    }
                }
                EditorGUI.EndProperty ();
            }
            GUI.backgroundColor = c;
        }
    }

}
