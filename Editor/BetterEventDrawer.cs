using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Pool;

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
        internal const string kInstancePath = "m_Target";
        internal const string kInstanceTypePath = "m_TargetAssemblyTypeName";
        internal const string kCallStatePath = "m_CallState";
        internal const string kArgumentsPath = "m_Arguments";
        internal const string kModePath = "m_Mode";
        internal const string kMethodNamePath = "m_MethodName";
        internal const string kCallsPath = "m_PersistentCalls.m_Calls";

        //ArgumentCache paths
        internal const string kFloatArgument = "m_FloatArgument";
        internal const string kIntArgument = "m_IntArgument";
        internal const string kObjectArgument = "m_ObjectArgument";
        internal const string kStringArgument = "m_StringArgument";
        internal const string kBoolArgument = "m_BoolArgument";
        internal const string kObjectArgumentAssemblyTypeName = "m_ObjectArgumentAssemblyTypeName";

        private static readonly GUIContent s_MixedValueContent = EditorGUIUtility.TrTextContent ("\u2014", "Mixed Values");

        GUIContent m_HeaderContent;

        FieldInfo fiListenersArray;

        UnityEventBase m_DummyEvent {
            get {
                var tUnityEventDrawer = typeof (UnityEventDrawer);
                return tUnityEventDrawer.GetField ("m_DummyEvent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue (this) as UnityEventBase;
            }
        }

        /// <summary>
		/// Internal Unity method for building a string description of the parameters for a UnityEvent, eg ' (GameObject)'
		/// </summary>
        MethodInfo miGetEventParams;

        string _eventMethodParameterTypes;
        string EventMethodParameterTypes {
            get {
                if (string.IsNullOrEmpty (_eventMethodParameterTypes)) {
                    var miFindMethod = m_DummyEvent.GetType ().GetMethod ("FindMethod", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type [] { typeof (string), typeof (Type), typeof (PersistentListenerMode), typeof (Type) }, null);
                    var miEventMethod = miFindMethod.Invoke (m_DummyEvent, new object [] { "Invoke", m_DummyEvent.GetType (), PersistentListenerMode.EventDefined, null }) as MethodInfo;
                    var typeFullNames = miEventMethod.GetParameters ().Select (x => x.ParameterType.FullName);
                    _eventMethodParameterTypes = "(" + string.Join (',', typeFullNames) + ")";
                    //Debug.Log ("EventMethodParameterTypes=" + _eventMethodParameterTypes);
                }
                return _eventMethodParameterTypes;
            }
        }

        static MethodInfo miGetFormattedMethodName;

        /// <summary>
        /// Toggle menu item path
        /// </summary>
        private const string OpdMenuName = "Arancia/Better Event Layout";
        private const string OpdKey = "BetterEventDrawer";

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

            if (EditorPrefs.GetBool (OpdKey, true)) {
                //Squeeze elements a little tighter together compared to normal UnityEvents
                list.elementHeight = (EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing);
            }

            var tUnityEventDrawer = typeof (UnityEventDrawer);
            miGetEventParams = tUnityEventDrawer.GetMethod ("GetEventParams", BindingFlags.NonPublic | BindingFlags.Static);

            if (miGetFormattedMethodName == null) {
                miGetFormattedMethodName = tUnityEventDrawer.GetMethod ("GetFormattedMethodName", BindingFlags.Static | BindingFlags.NonPublic);
            }

            fiListenersArray = tUnityEventDrawer.GetField ("m_ListenersArray", BindingFlags.NonPublic | BindingFlags.Instance);
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
            } else if (!IsPersistantListenerValid (m_DummyEvent, methodName.stringValue, listenerTarget.objectReferenceValue, mode, desiredType)) {
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
#pragma warning disable 8524
                            var argumentString = mode switch {
                                PersistentListenerMode.Bool => "(System.Boolean)",
                                PersistentListenerMode.Int => "(System.Int32)",
                                PersistentListenerMode.Float => "(System.Single)",
                                PersistentListenerMode.String => "(System.String)",
                                PersistentListenerMode.Object => $"({desiredType})",
                                PersistentListenerMode.EventDefined => EventMethodParameterTypes,
                                PersistentListenerMode.Void => string.Empty,
#pragma warning restore
                            };
                            xmlDoc = mi.GetDocumentation (argumentString);
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
                if (desiredType != null && desiredType != typeof (GameObject)) {
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

        //static Texture2D texturePanel;

        public override void OnGUI (Rect position, SerializedProperty property, GUIContent label) {
            base.OnGUI (position, property, label);
            if (m_HeaderContent == null) {
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D> (AssetDatabase.GUIDToAssetPath ("db4aaf330ef7e43858c9c49ff984c8c3"));
                m_HeaderContent = new GUIContent (label.text + miGetEventParams.Invoke (this, new object [] { m_DummyEvent }), icon, label.tooltip);
            }
        }

        protected override void DrawEventHeader (Rect headerRect) {
            if (m_HeaderContent != null) {
                headerRect.height = EditorGUIUtility.singleLineHeight;
                GUI.Label (headerRect, m_HeaderContent);
            }
        }

        protected override void DrawEvent (Rect rect, int index, bool isActive, bool isFocused) {
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
                    if (EditorGUI.DropdownButton (functionRect, functionContent, FocusType.Passive, EditorStyles.popup)) {
                        var genericMenu = BuildPopupList (listenerTarget.objectReferenceValue, m_DummyEvent, pListener);
                        genericMenu.DropDown (functionRect);
                    }
                }
                EditorGUI.EndProperty ();
            }
            GUI.backgroundColor = c;
        }


        struct ValidMethodMap {
            public UnityEngine.Object target;
            public MethodInfo methodInfo;
            public PersistentListenerMode mode;
        }

        static readonly Type BaseComponentType = typeof (Component);
        static readonly Type MonoBehaviourType = typeof (MonoBehaviour);

        /// <summary>
		/// Filter out methods from base classes to clear up the mud.
		/// </summary>
        static IEnumerable<ValidMethodMap> CalculateMethodMap (UnityEngine.Object target, Type [] t, bool allowSubclasses) {
            var validMethods = new List<ValidMethodMap> ();
            if (target == null || t == null)
                return validMethods;

            // find the methods on the behaviour that match the signature
            Type componentType = target.GetType ();
            var componentMethods = componentType.GetMethods ().Where (x => /*!x.IsStatic && */!x.IsSpecialName).ToList ();

            var wantedProperties = componentType.GetProperties ().AsEnumerable ();
            wantedProperties = wantedProperties.Where (x => x.DeclaringType != BaseComponentType && x.GetCustomAttributes (typeof (ObsoleteAttribute), true).Length == 0 && x.GetSetMethod () != null);
            componentMethods.AddRange (wantedProperties.Select (x => x.GetSetMethod ()));

            foreach (var componentMethod in componentMethods) {
                //Debug.Log ("Method: " + componentMethod);
                // if the argument length is not the same, no match
                var componentParameters = componentMethod.GetParameters ();
                if (componentParameters.Length != t.Length)
                    continue;

                // Don't show obsolete methods.
                if (componentMethod.GetCustomAttributes (typeof (ObsoleteAttribute), true).Length > 0)
                    continue;

                // DOn't show methods with a return type other than void
                if (componentMethod.ReturnType != typeof (void))
                    continue;

                // if the argument types do not match, no match
                bool parametersMatch = true;
                for (int i = 0; i < t.Length; i++) {
                    if (allowSubclasses && t [i].IsAssignableFrom (componentParameters [i].ParameterType))
                        parametersMatch = true;
                    else if (!componentParameters [i].ParameterType.IsAssignableFrom (t [i]))
                        parametersMatch = false;
                }

                // valid method
                if (parametersMatch) {
                    var vmm = new ValidMethodMap {
                        target = target,
                        methodInfo = componentMethod
                    };
                    validMethods.Add (vmm);
                }
            }
            return validMethods;
        }


        /// <summary>
		/// Create popup menu with methods that can be invoked from the event
		/// </summary>
        internal static GenericMenu BuildPopupList (UnityEngine.Object target, UnityEventBase dummyEvent, SerializedProperty listener) {
            //special case for components... we want all the game objects targets there!
            var targetToUse = target;
            if (targetToUse is Component)
                targetToUse = (target as Component).gameObject;

            // find the current event target...
            var methodName = listener.FindPropertyRelative (kMethodNamePath);

            var menu = new GenericMenu ();
            menu.AddItem (new GUIContent (kNoFunctionString),
                string.IsNullOrEmpty (methodName.stringValue),
                ClearEventFunction,
                new UnityEventFunction (listener, null, null, PersistentListenerMode.EventDefined));

            if (targetToUse == null)
                return menu;

            menu.AddSeparator (string.Empty);

            // figure out the signature of this delegate...
            // The property at this stage points to the 'container' and has the field name
            var delegateType = dummyEvent.GetType ();

            // check out the signature of invoke as this is the callback!
            var miDelegateMethod = delegateType.GetMethod ("Invoke");
            var delegateArgumentsTypes = miDelegateMethod.GetParameters ().Select (x => x.ParameterType).ToArray ();

            var duplicateNames = DictionaryPool<string, int>.Get ();
            var duplicateFullNames = DictionaryPool<string, int>.Get ();

            GeneratePopUpForType (menu, targetToUse, targetToUse.GetType ().Name, listener, delegateArgumentsTypes);
            duplicateNames [targetToUse.GetType ().Name] = 0;
            if (targetToUse is GameObject) {
                Component [] comps = (targetToUse as GameObject).GetComponents<Component> ();

                // Collect all the names and record how many times the same name is used.
                foreach (var comp in comps) {
                    if (comp == null)
                        continue;

                    if (duplicateNames.TryGetValue (comp.GetType ().Name, out int duplicateIndex))
                        duplicateIndex++;
                    duplicateNames [comp.GetType ().Name] = duplicateIndex;
                }

                foreach (var comp in comps) {
                    if (comp == null)
                        continue;

                    var compType = comp.GetType ();
                    string targetName = compType.Name;
                    int duplicateIndex = 0;

                    // Is this name used multiple times? If so then use the full name plus an index if there are also duplicates of this. (case 1309997)
                    if (duplicateNames [compType.Name] > 0) {
                        if (duplicateFullNames.TryGetValue (compType.FullName, out duplicateIndex))
                            targetName = $"{compType.FullName} ({duplicateIndex})";
                        else
                            targetName = compType.FullName;
                    }
                    GeneratePopUpForType (menu, comp, targetName, listener, delegateArgumentsTypes);
                    duplicateFullNames [compType.FullName] = duplicateIndex + 1;
                }

                DictionaryPool<string, int>.Release (duplicateNames);
                DictionaryPool<string, int>.Release (duplicateFullNames);
            }
            return menu;
        }

        private static void GeneratePopUpForType (GenericMenu menu, UnityEngine.Object target, string targetName, SerializedProperty listener, Type [] delegateArgumentsTypes) {
            var methods = new List<ValidMethodMap> ();
            bool didAddDynamic = false;

            // skip 'void' event defined on the GUI as we have a void prebuilt type!
            if (delegateArgumentsTypes.Length != 0) {
                GetMethodsForTargetAndMode (target, delegateArgumentsTypes, methods, PersistentListenerMode.EventDefined);
                if (methods.Count > 0) {
                    menu.AddDisabledItem (new GUIContent (targetName + "/Dynamic " + string.Join (", ", delegateArgumentsTypes.Select (e => GetTypeName (e)).ToArray ())));
                    AddMethodsToMenu (menu, listener, methods, targetName);
                    didAddDynamic = true;
                }
            }

            methods.Clear ();
            GetMethodsForTargetAndMode (target, new [] { typeof (float) }, methods, PersistentListenerMode.Float);
            GetMethodsForTargetAndMode (target, new [] { typeof (int) }, methods, PersistentListenerMode.Int);
            GetMethodsForTargetAndMode (target, new [] { typeof (string) }, methods, PersistentListenerMode.String);
            GetMethodsForTargetAndMode (target, new [] { typeof (bool) }, methods, PersistentListenerMode.Bool);
            GetMethodsForTargetAndMode (target, new [] { typeof (UnityEngine.Object) }, methods, PersistentListenerMode.Object);
            GetMethodsForTargetAndMode (target, new Type [] { }, methods, PersistentListenerMode.Void);
            if (methods.Count > 0) {
                if (didAddDynamic)
                    // AddSeperator doesn't seem to work for sub-menus, so we have to use this workaround instead of a proper separator for now.
                    menu.AddItem (new GUIContent (targetName + "/ "), false, null);
                if (delegateArgumentsTypes.Length != 0)
                    menu.AddDisabledItem (new GUIContent (targetName + "/Static Parameters"));
                AddMethodsToMenu (menu, listener, methods, targetName);
            }
        }

        private static void AddMethodsToMenu (GenericMenu menu, SerializedProperty listener, List<ValidMethodMap> methods, string targetName) {
            // Note: sorting by a bool in OrderBy doesn't seem to work for some reason, so using numbers explicitly.
            var orderedMethods = methods.OrderBy (e => e.methodInfo.Name.StartsWith ("set_") ? 0 : 1).ThenBy (e => e.methodInfo.Name);
            foreach (var validMethod in orderedMethods)
                AddFunctionsForScript (menu, listener, validMethod, targetName);
        }

        private static void GetMethodsForTargetAndMode (UnityEngine.Object target, Type [] delegateArgumentsTypes, List<ValidMethodMap> methods, PersistentListenerMode mode) {
            var newMethods = CalculateMethodMap (target, delegateArgumentsTypes, mode == PersistentListenerMode.Object);
            foreach (var m in newMethods) {
                var method = m;
                method.mode = mode;
                methods.Add (method);
            }
        }

        static void AddFunctionsForScript (GenericMenu menu, SerializedProperty listener, ValidMethodMap method, string targetName) {
            PersistentListenerMode mode = method.mode;

            // find the current event target...
            var listenerTarget = listener.FindPropertyRelative (kInstancePath).objectReferenceValue;
            var methodName = listener.FindPropertyRelative (kMethodNamePath).stringValue;
            var setMode = (PersistentListenerMode)listener.FindPropertyRelative (kModePath).enumValueIndex;
            var typeName = listener.FindPropertyRelative (kArgumentsPath).FindPropertyRelative (kObjectArgumentAssemblyTypeName);

            var args = new StringBuilder ();
            var count = method.methodInfo.GetParameters ().Length;
            for (int index = 0; index < count; index++) {
                var methodArg = method.methodInfo.GetParameters () [index];
                args.Append (string.Format ("{0}", GetTypeName (methodArg.ParameterType)));

                if (index < count - 1)
                    args.Append (", ");
            }

            var isCurrentlySet = listenerTarget == method.target
                && methodName == method.methodInfo.Name
                && mode == setMode;

            if (isCurrentlySet && mode == PersistentListenerMode.Object && method.methodInfo.GetParameters ().Length == 1) {
                isCurrentlySet &= (method.methodInfo.GetParameters () [0].ParameterType.AssemblyQualifiedName == typeName.stringValue);
            }

            string path = miGetFormattedMethodName.Invoke (null, new object [] { targetName, method.methodInfo.Name, args.ToString (), mode == PersistentListenerMode.EventDefined }) as string;
            menu.AddItem (new GUIContent (path),
                isCurrentlySet,
                SetEventFunction,
                new UnityEventFunction (listener, method.target, method.methodInfo, mode));
        }

        static string GetTypeName (Type t) {
            if (t == typeof (int))
                return "int";
            if (t == typeof (float))
                return "float";
            if (t == typeof (string))
                return "string";
            if (t == typeof (bool))
                return "bool";
            return t.Name;
        }

        static void SetEventFunction (object source) {
            ((UnityEventFunction)source).Assign ();
        }

        static void ClearEventFunction (object source) {
            ((UnityEventFunction)source).Clear ();
        }

        struct UnityEventFunction {
            readonly SerializedProperty m_Listener;
            readonly UnityEngine.Object m_Target;
            readonly MethodInfo m_Method;
            readonly PersistentListenerMode m_Mode;

            public UnityEventFunction (SerializedProperty listener, UnityEngine.Object target, MethodInfo method, PersistentListenerMode mode) {
                m_Listener = listener;
                m_Target = target;
                m_Method = method;
                m_Mode = mode;
            }

            public void Assign () {
                // find the current event target...
                var listenerTarget = m_Listener.FindPropertyRelative (kInstancePath);
                var listenerTargetType = m_Listener.FindPropertyRelative (kInstanceTypePath);
                var methodName = m_Listener.FindPropertyRelative (kMethodNamePath);
                var mode = m_Listener.FindPropertyRelative (kModePath);
                var arguments = m_Listener.FindPropertyRelative (kArgumentsPath);

                listenerTarget.objectReferenceValue = m_Target;
                listenerTargetType.stringValue = m_Method.DeclaringType.AssemblyQualifiedName;
                methodName.stringValue = m_Method.Name;
                mode.enumValueIndex = (int)m_Mode;

                if (m_Mode == PersistentListenerMode.Object) {
                    var fullArgumentType = arguments.FindPropertyRelative (kObjectArgumentAssemblyTypeName);
                    var argParams = m_Method.GetParameters ();
                    var tUnityObject = typeof (UnityEngine.Object);
                    if (argParams.Length == 1 && tUnityObject.IsAssignableFrom (argParams [0].ParameterType))
                        fullArgumentType.stringValue = argParams [0].ParameterType.AssemblyQualifiedName;
                    else
                        fullArgumentType.stringValue = tUnityObject.AssemblyQualifiedName;
                }

                ValidateObjectParamater (arguments, m_Mode);

                m_Listener.serializedObject.ApplyModifiedProperties ();
            }

            private void ValidateObjectParamater (SerializedProperty arguments, PersistentListenerMode mode) {
                var fullArgumentType = arguments.FindPropertyRelative (kObjectArgumentAssemblyTypeName);
                var argument = arguments.FindPropertyRelative (kObjectArgument);
                var argumentObj = argument.objectReferenceValue;

                if (mode != PersistentListenerMode.Object) {
                    fullArgumentType.stringValue = typeof (UnityEngine.Object).AssemblyQualifiedName;
                    argument.objectReferenceValue = null;
                    return;
                }

                if (argumentObj == null)
                    return;

                var t = Type.GetType (fullArgumentType.stringValue, false);
                if (!typeof (UnityEngine.Object).IsAssignableFrom (t) || !t.IsInstanceOfType (argumentObj))
                    argument.objectReferenceValue = null;
            }

            public void Clear () {
                // find the current event target...
                var methodName = m_Listener.FindPropertyRelative (kMethodNamePath);
                methodName.stringValue = null;

                var mode = m_Listener.FindPropertyRelative (kModePath);
                mode.enumValueIndex = (int)PersistentListenerMode.Void;

                m_Listener.serializedObject.ApplyModifiedProperties ();
            }
        }
    }

}
