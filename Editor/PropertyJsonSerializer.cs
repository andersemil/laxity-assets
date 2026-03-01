using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
/*
{
"m_Target":218326,
"m_TargetAssemblyTypeName":"",
"m_MethodName":"AppleSignIn",
"m_Mode":"EventDefined",
"m_Arguments":{
  "m_ObjectArgument":0,
  "m_ObjectArgumentAssemblyTypeName":"UnityEngine.Object, UnityEngine",
  "m_IntArgument":0,
  "m_FloatArgument":0.0,
  "m_StringArgument":"",
  "m_BoolArgument":false
},
"m_CallState":"RuntimeOnly"
}
* */

public static class PropertyJsonSerializer {
    public static string SerializeProperty (SerializedProperty property) {
        // Use a Dictionary to store the key-value pairs
        var data = PropertyToDictionary (property);
        return JsonConvert.SerializeObject (data);
    }

    public static void DeserializeProperty (SerializedProperty rootProp, string json) {
        if (string.IsNullOrEmpty (json)) return;
        try {
            // Parse JSON into a dictionary
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>> (json);

            // Apply data recursively
            ApplyDictionaryToProperty (rootProp, data);

            // IMPORTANT: Save the changes to the actual object
            rootProp.serializedObject.ApplyModifiedProperties ();
        } catch (System.Exception e) {
            Debug.LogError ($"Failed to deserialize property: {e.Message}");
        }
    }

    private static Dictionary<string, object> PropertyToDictionary (SerializedProperty prop) {
        Dictionary<string, object> dict = new ();

        // Enter the property's children
        var endProperty = prop.GetEndProperty ();
        var iterator = prop.Copy ();

        // Move to the first child
        if (iterator.NextVisible (true)) {
            do {
                // Stop if we've reached the end of this property's scope
                if (SerializedProperty.EqualContents (iterator, endProperty)) break;

                dict [iterator.name] = GetPropertyValue (iterator);
            }
            while (iterator.NextVisible (false)); // false = don't dive into sub-children here (recursion handles it)
        }

        return dict;
    }

    private static object GetPropertyValue (SerializedProperty prop) {
        return prop.propertyType switch {
            SerializedPropertyType.Integer => prop.intValue,
            SerializedPropertyType.Boolean => prop.boolValue,
            SerializedPropertyType.Float => prop.floatValue,
            SerializedPropertyType.String => prop.stringValue,
            SerializedPropertyType.Color => "#" + ColorUtility.ToHtmlStringRGBA (prop.colorValue),
            SerializedPropertyType.Vector3 => prop.vector3Value,
            SerializedPropertyType.Enum => prop.enumValueIndex,
            SerializedPropertyType.Generic => PropertyToDictionary (prop),// Recursive call for nested classes
            SerializedPropertyType.ObjectReference => prop.objectReferenceValue == null ? 0 : prop.objectReferenceValue.GetInstanceID (),
            _ => prop.displayName,// Fallback
        };
    }

    private static void ApplyDictionaryToProperty (SerializedProperty prop, Dictionary<string, object> data) {
        foreach (var item in data) {
            // Find the child property by name (matches the JSON key)
            var child = prop.FindPropertyRelative (item.Key);
            if (child == null) continue;

            // 1. Handle Nested Objects (Generic)
            if (child.propertyType == SerializedPropertyType.Generic && item.Value is JObject jObj) {
                var nestedDict = jObj.ToObject<Dictionary<string, object>> ();
                ApplyDictionaryToProperty (child, nestedDict);
            }
            // 2. Handle Object References (InstanceID)
            else if (child.propertyType == SerializedPropertyType.ObjectReference) {
                int instanceID = System.Convert.ToInt32 (item.Value);

                // 0 is the "null" ID in Unity
                if (instanceID == 0) {
                    child.objectReferenceValue = null;
                } else {
                    // Convert the ID back to a live Engine Object
                    child.objectReferenceValue = EditorUtility.InstanceIDToObject (instanceID);
                }
            }
            // 3. Handle Primitives
            else {
                ApplyPrimitiveValue (child, item.Value);
            }
        }
    }

    private static void ApplyPrimitiveValue (SerializedProperty prop, object value) {
        // JSON numbers often deserialize as long/double; Convert ensures they fit the target
        switch (prop.propertyType) {
        case SerializedPropertyType.Integer:
            prop.intValue = System.Convert.ToInt32 (value);
            break;
        case SerializedPropertyType.Float:
            prop.floatValue = System.Convert.ToSingle (value);
            break;
        case SerializedPropertyType.String:
            prop.stringValue = value?.ToString ();
            break;
        case SerializedPropertyType.Boolean:
            prop.boolValue = System.Convert.ToBoolean (value);
            break;
        case SerializedPropertyType.Enum:
            prop.enumValueIndex = System.Convert.ToInt32 (value);
            break;
        default:
            // Note: If you serialized eg Vector3 as a nested object,
            // handle it in the Generic/JObject section above.
            break;
        }
    }
}
