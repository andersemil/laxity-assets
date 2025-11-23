using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

using UnityEngine.Networking;
using UnityEditor;

namespace AranciaAssets.EditorTools {

    /// <summary>
	/// Static class for creating an XML Documentation dictionary which can be used for showing tooltips and headers in editors.
	/// Based on this article https://learn.microsoft.com/en-us/archive/msdn-magazine/2019/october/csharp-accessing-xml-documentation-via-reflection
	/// </summary>
    internal static class XMLDocumentation {
        /// <summary>
		/// The actual dictionary of XML Keys to documentation string.
		/// </summary>
        internal static Dictionary<string, string> Documentation = new ();

        /// <summary>
		/// List of scanned types so we only scan once
		/// </summary>
        internal static HashSet<string> LoadedTypes = new ();

        /// <summary>
		/// Utility function for getting the directory path of an assembly
		/// </summary>
        static string GetDirectoryPath (this Assembly assembly) {
            string codeBase = assembly.CodeBase;
            var uri = new UriBuilder (codeBase);
            string path = Uri.UnescapeDataString (uri.Path);
            return Path.GetDirectoryName (path);
        }

        /// <summary>
		/// Load XML documentation for the specified type. If 'key' is found on the way, fill the documentation parameter
		/// </summary>
        static bool LoadXmlDocumentation (Type type, string key, out string documentation) {
            var assembly = type.Assembly;
            var directoryPath = assembly.GetDirectoryPath ();
            var xmlFilePath = Path.Combine (directoryPath, assembly.GetName ().Name + ".xml");
            documentation = string.Empty;
            if (!File.Exists (xmlFilePath))
                return false;
            using var xmlReader = XmlReader.Create (new StringReader (File.ReadAllText (xmlFilePath)));
            while (xmlReader.Read ()) {
                if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "member") {
                    var raw_name = xmlReader ["name"];
                    xmlReader.ReadToFollowing ("summary");
                    var str = xmlReader.ReadInnerXml ();
                    if (!string.IsNullOrWhiteSpace (str)) {
                        Documentation [raw_name] = str.Replace ("<para>", string.Empty).Replace ("</para>", string.Empty).Trim ();
                        if (key == raw_name)
                            documentation = str;
                    }
                }
            }
            return true;
        }

        /// <summary>
		/// Format key string in XML documentation
		/// </summary>
        static string XmlDocumentationKeyHelper (string typeFullNameString, string memberNameString, string argument = null) {
            string key = Regex.Replace (typeFullNameString, @"\[.*\]", string.Empty).Replace ('+', '.');
            if (memberNameString != null) {
                key += "." + memberNameString;
                if (argument != null) {
                    key += argument;
                }
            }
            return key;
        }

        /// <summary>
		/// Get XML documentation for a property
		/// </summary>
        public static string GetDocumentation (this PropertyInfo propertyInfo) {
            var declaringType = propertyInfo.DeclaringType;
            var key = "P:" + XmlDocumentationKeyHelper (declaringType.FullName, propertyInfo.Name);
            return GetDocumentation (declaringType, key);
        }

        /// <summary>
		/// Get XML documentation for a method
		/// </summary>
        public static string GetDocumentation (this MethodInfo methodInfo) {
            var declaringType = methodInfo.DeclaringType;
            var key = "M:" + XmlDocumentationKeyHelper (declaringType.FullName, methodInfo.Name);
            return GetDocumentation (declaringType, key);
        }

        /// <summary>
		/// Get XML documentation for a method with a single argument
		/// </summary>
        public static string GetDocumentation (this MethodInfo methodInfo, string argumentString) {
            var declaringType = methodInfo.DeclaringType;
            var key = "M:" + XmlDocumentationKeyHelper (declaringType.FullName, methodInfo.Name, argumentString);
            return GetDocumentation (declaringType, key);
        }

        /// <summary>
		/// Get XML documentation for a SerializedProperty
		/// </summary>
        public static string GetDocumentation (this UnityEditor.SerializedProperty property) {
            var declaringType = property.serializedObject.targetObject.GetType ();
            var propName = property.propertyPath;
            var idxOfDot = propName.IndexOf ('.');
            if (idxOfDot > 0) {
                propName = propName.Substring (0, idxOfDot);
            }
            if (propName.StartsWith ("m_")) {
                propName = char.ToLower (propName [2]) + propName [3..];
            }
            var key = XmlDocumentationKeyHelper (declaringType.FullName, propName);
            var doc = GetDocumentation (declaringType, "F:" + key);
            if (string.IsNullOrWhiteSpace (doc)) {
                doc = GetDocumentation (declaringType, "P:" + key);
            }
            return doc;
        }

        /// <summary>
		/// Attempt to find documentation for the given member
		/// </summary>
        static string GetDocumentation (Type declaringType, string key) {
            if (Documentation.TryGetValue (key, out string documentation))
                return documentation;
            if (!LoadedTypes.Contains (declaringType.FullName)) {
                if (LoadXmlDocumentation (declaringType, key, out documentation))
                    return documentation;
                //UnityEngine.Debug.Log ($"Documentation not found, generating: {key}");
                GenerateDocumentationForType (declaringType);
                Documentation.TryGetValue (key, out documentation);
            }
            return documentation;
        }

        /// <summary>
		/// Regex to match any using statement
		/// </summary>
        static readonly Regex UsingRegex = new (@"using\s+(\S+);", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any namespace definition
		/// </summary>
        static readonly Regex NamespaceRegex = new (@"namespace\s+(\S+)\s*{", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any class definition
		/// </summary>
        static readonly Regex ClassRegex = new (@"(?:public|private|protected|internal)\s+?((?:partial|sealed)\s+?)?class\s+(\S+)\s+(?::.*?)?{", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any documented property, field or method
		/// </summary>
        static readonly Regex DocRegex = new (@"\/\/\/ <summary>((?:\n\s*?\/\/\/[^\n]*?)*?)\s*?\/\/\/ <\/summary>\s*?(?:\[[^\]]+?\]\s*?)*?\s*?(?:\s+(public|private|internal|protected)\s+)?(?:static\s+)?\S+\s+?(?:(\S+)\s*?\((?:\s*?([\w\.]+)\s+)?[^,]*?\)\s*?{.*?}|(\S+)\s*?;|(\S+)\s*?{.*?})", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Regex to filter newline structure of XML comment
        /// </summary>
        static readonly Regex CommentNewlineRegex = new (@"\s+\/\/\/ ", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Paths of all scripts in the AssetDatabase, including packages, populated in the LoadScriptPaths method
		/// </summary>
        static IEnumerable<string> ScriptPaths;

        [InitializeOnLoadMethod]
        //[UnityEditor.Callbacks.DidReloadScripts]
        static void InitOnLoad () {
            /*var globalCacheFilename = Path.Combine (UnityEngine.Application.temporaryCachePath, "globalcache");
            if (File.Exists (globalCacheFilename)) {
                LoadXMLCache (globalCacheFilename);
            }*/
            var scriptGuids = AssetDatabase.FindAssets ("t:Script");
            ScriptPaths = scriptGuids.Select (guid => AssetDatabase.GUIDToAssetPath (guid)).Where (path => !path.Contains ("Editor/") && !path.EndsWith (".dll"));
            Directory.CreateDirectory (CachePath);
        }

        /// <summary>
		/// Add documentation manually for specific type. Necessary since Unity 2021.3 and earlier (at least) does not generate XML documentation when compiling scripts
		/// </summary>
        static void GenerateDocumentationForType (Type type) {
            var typeFullName = type.FullName;
            if (typeFullName.StartsWith ("UnityEngine.")) {
                if (type.Namespace == "UnityEngine.EventSystems"
                 //|| type.Namespace == "UnityEngine.Experimental.Rendering.Universal"
                 //|| type.Namespace == "UnityEngine.Rendering.Universal"
                 ) {
                    ScrapeUnityDocumentation ($"https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/{typeFullName}.html", typeFullName, UnityPackageDocMemberRegex);
                } else if (type.Namespace == "UnityEngine.InputSystem.UI") {
                    ScrapeUnityDocumentation ($"https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/api/{typeFullName}.html", typeFullName, UnityPackageDocMemberRegex);
                } else if (type.Namespace == "UnityEngine.Timeline") {
                    ScrapeUnityDocumentation($"https://docs.unity3d.com/Packages/com.unity.timeline@1.8/api/{typeFullName}.html", typeFullName, UnityPackageDocMemberRegex);
                } else {
                    //Scrape built-in package documentation
                    //UnityEngine.Debug.Log ($"GenerateDocumentationForType {typeFullName}");
                    var typeName = type.FullName.Replace ("UnityEngine.", string.Empty);
                    var docVersionString = typeName.StartsWith ("UI.") ? "2019.1" : UnityEngine.Application.unityVersion.Substring (0, UnityEngine.Application.unityVersion.LastIndexOf ('.'));
                    ScrapeUnityDocumentation ($"https://docs.unity3d.com/{docVersionString}/Documentation/ScriptReference/{typeName}.html", typeFullName, UnityEngineDocMemberRegex);
                }
                return;
            }

            if (typeFullName.StartsWith ("System."))
                return;
            if (!LoadedTypes.Add (typeFullName))
                return;

            var naivePath = ScriptPaths.FirstOrDefault (path => Path.GetFileName (path) == $"{type.Name}.cs");
            if (naivePath != null) {
                if (ScanForDocumentation (naivePath, type))
                    return;
                UnityEngine.Debug.Log ($"XMLDocumentation: Definition of {typeFullName} not found in {naivePath}, scanning whole directory ...");
            } else {
                UnityEngine.Debug.Log ($"XMLDocumentation: Naive search on filename {type.Name}.cs not found, scanning all scripts ...");
            }

            foreach (var filename in ScriptPaths) {
                if (ScanForDocumentation (filename, type))
                    break;
            }
        }

        /// <summary>
		/// Scans a given script file for a specific type and all its documented properties, fields and methods.
		/// </summary>
        static bool ScanForDocumentation (string filename, Type type) {
            var srcFile = File.ReadAllText (filename);

            var nameSpace = string.Empty;
            foreach (Match mi in NamespaceRegex.Matches (srcFile)) {
                if (mi.Success && mi.Groups [1].Value == type.Namespace) {
                    nameSpace = mi.Groups [1].Value;
                    break;
                }
            }

            var usingNamespaces = UsingRegex.Matches (srcFile).Select (mi => mi.Groups [1].Value);

            var className = ClassRegex.Matches (srcFile).Select (mi => mi.Groups [2].Value).FirstOrDefault (s => s == type.Name);
            if (string.IsNullOrWhiteSpace (className)) {
                return false;
            }

            var nameSpaceAndClass = !string.IsNullOrWhiteSpace (nameSpace) ? $"{nameSpace}.{className}" : className;
            if (type.FullName != nameSpaceAndClass) {
                UnityEngine.Debug.LogError ($"Namespace/Class mismatch {type.FullName} <> {nameSpaceAndClass}");
                return false;
            }
            nameSpaceAndClass += ".";

            //Debug.Log ($"{type.FullName} found in {filename}, scanning for documentation");
            string key;
            foreach (Match mi in DocRegex.Matches (srcFile)) {
                var xmlComment = CommentNewlineRegex.Replace (mi.Groups [1].Value, " ").Trim ();
                if (mi.Groups [3].Success) {
                    //Ignore non-public documented methods
                    if (!mi.Groups [2].Success || mi.Groups [2].Value != "public")
                        continue;
                    key = $"M:{nameSpaceAndClass}{mi.Groups [3].Value}";
                    if (mi.Groups [4].Success) {
                        //Ignore extension methods
                        if (mi.Groups [4].Value == "this")
                            continue;
                        key += $"({ClassifyType (nameSpaceAndClass, usingNamespaces, mi.Groups [4].Value)})";
                    }
                    //UnityEngine.Debug.Log ($"{key} => {xmlComment}");
                    Documentation.Add (key, xmlComment);
                } else if (mi.Groups [5].Success) {
                    key = $"F:{nameSpaceAndClass}{mi.Groups [5].Value}";
                    //UnityEngine.Debug.Log ($"{key} => {xmlComment}");
                    Documentation.Add (key, xmlComment);
                } else if (mi.Groups [6].Success) {
                    key = $"P:{nameSpaceAndClass}{mi.Groups [6].Value}";
                    //UnityEngine.Debug.Log ($"{key} => {xmlComment}");
                    Documentation.Add (key, xmlComment);
                }
            }
            return true;
        }

        /// <summary>
		/// Create fully qualified Type names from the ones used in the source code
		/// </summary>
        static string ClassifyType (string nameSpaceAndClass, IEnumerable<string> usingNamespaces, string typeName) {
            switch (typeName) {
            case "bool":
                return "System.Boolean";
            case "int":
                return "System.Int32";
            case "long":
                return "System.Int64";
            case "float":
                return "System.Single";
            case "double":
                return "System.Double";
            case "string":
                return "System.String";
            default:
                var idxOfLt = typeName.LastIndexOf ('<');
                if (idxOfLt > 0) {
                    //Type is a generic, discard type parameters
                    typeName = typeName.Substring (0, idxOfLt);
                }
                var splitTypeNames = typeName.Split ('.');
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies ()) {
                    var t = asm.GetType (typeName);
                    if (t != null)
                        return t.FullName;
                    t = asm.GetType (splitTypeNames [0]);
                    if (t == null) {
                        foreach (var uns in usingNamespaces) {
                            var n = $"{uns}.{splitTypeNames [0]}";
                            t = asm.GetType (n);
                            if (t != null) {
                                //UnityEngine.Debug.Log ($"ClassifyType: Found type {n} in {asm.FullName}");
                                break;
                            }
                        }
                    }
                    if (t != null) {
                        for (var i = 1; i < splitTypeNames.Length; i++) {
                            t = t.GetNestedType (splitTypeNames [i]);
                            if (t == null)
                                break;  //TODO maybe restart namespace search if nested type not found?
                        }
                        return t.FullName;
                    }
                }
                var bestGuess = $"{nameSpaceAndClass.Substring (0, nameSpaceAndClass.Length - 1)}+{typeName}";
                UnityEngine.Debug.Log ($"XMLDocumentation: {typeName} not found in assemblies, returning {bestGuess}");
                return bestGuess;
            }
        }

        //
		// --- Online doc scraping section ---
        //

        /// <summary>
        /// Regex to match member descriptions in Unity online Package documentation
        /// </summary>
        static readonly Regex UnityPackageDocMemberRegex = new (@"<h4 .+?data-uid=""UnityEngine\..+?\..+?\.(.+?)"">.+?<div class="".*? summary.*?"">(.*?)<\/div>", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Regex to match sections in Unity online documentation (methods, fields and properties)
        /// </summary>
        static readonly Regex UnityDocSectionRegex = new (@"<h\d(?:\sid=""\w+?"")?>\s*?(?:(Properties|Public Methods|Methods|Fields))\s*?<\/h\d>", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Regex to match member descriptions in Unity online Scripting API documentation
        /// </summary>
        static readonly Regex UnityEngineDocMemberRegex = new (@"<td class=""lbl"">.*?<a href="".*?"">(.+?)<\/a>.*?<td class=""desc"">(.*?)<\/td>", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Dictionary of currently downloading URLs (url => asyncOp)
        /// </summary>
        static readonly Dictionary<string, UnityWebRequestAsyncOperation> AsyncDownloads = new ();

		/// <summary>
		/// Scrape online documentation for description of members of a specific class.
		/// As this method is called frequently when UI is repainted, we can create async ops and monitor them so we don't block the main/UI thread.
        /// Returns <c>true</c> if the documentation is or may become available from the specified url.
		/// </summary>
		static bool ScrapeUnityDocumentation (string url, string nameSpaceAndClass, Regex memberRegex) {
            if (LoadDocumentationCache (url)) {
                LoadedTypes.Add (nameSpaceAndClass);
                return true;
            }

            if (!AsyncDownloads.TryGetValue (url, out UnityWebRequestAsyncOperation asyncOp)) {
                //UnityEngine.Debug.Log ($"Scraping doc for {nameSpaceAndClass} from {url}");
                var req = UnityWebRequest.Get (url);
                asyncOp = req.SendWebRequest ();
                AsyncDownloads.Add (url, asyncOp);
            }
            if (!asyncOp.isDone)
                return true;
            AsyncDownloads.Remove (url);

            if (asyncOp.webRequest.result != UnityWebRequest.Result.Success) {
                var result = asyncOp.webRequest.result != UnityWebRequest.Result.ProtocolError;
                if (!result) {
                    UnityEngine.Debug.LogError ($"Error while scraping doc at {url} => {asyncOp.webRequest.error}");
                    LoadedTypes.Add (nameSpaceAndClass);
                }
                asyncOp.webRequest.Dispose ();
                return result;
            }
            var doc = asyncOp.webRequest.downloadHandler.text;
            asyncOp.webRequest.Dispose ();

            var d = new Dictionary<string, string> ();

            Match prevMatch = null;
            foreach (Match mi in UnityDocSectionRegex.Matches (doc)) {
                if (mi.Success) {
                    if (prevMatch != null) {
                        var sectionStartIndex = prevMatch.Index + prevMatch.Length;
                        var section = doc [sectionStartIndex..mi.Index];
                        var sectionHeader = prevMatch.Groups [1].Value.Trim ();
                        //UnityEngine.Debug.Log ($"{sectionHeader} section: {sectionStartIndex} - {mi.Index}");
                        ScrapeUnityDocSection (d, nameSpaceAndClass, sectionHeader, section, memberRegex);
                    }
                    prevMatch = mi;
                }
            }

            if (prevMatch != null) {
                var sectionStartIndex = prevMatch.Index + prevMatch.Length;
                var section = doc [sectionStartIndex..];
                var sectionHeader = prevMatch.Groups [1].Value.Trim ();
                //UnityEngine.Debug.Log ($"{sectionHeader} section: {sectionStartIndex} - {mi.Index}");
                ScrapeUnityDocSection (d, nameSpaceAndClass, sectionHeader, section, memberRegex);
            }

            UpdateDocumentationCache (url, d);

            var en = d.GetEnumerator ();
            while (en.MoveNext ())
                Documentation [en.Current.Key] = en.Current.Value;

            LoadedTypes.Add (nameSpaceAndClass);
            return true;
        }

        /// <summary>
		/// Scrape a section of online documentation for members and their description
		/// </summary>
        static void ScrapeUnityDocSection (IDictionary<string, string> d, string nameSpaceAndClass, string sectionHeader, string section, Regex memberRegex) {
            var _pre = sectionHeader switch {
                "Methods" => "M:",
                "Public Methods" => "M:",
                "Fields" => "F:",
                "Properties" => "P:",
                _ => "???",
            };
            var prefix = _pre + nameSpaceAndClass;

            foreach (Match mi in memberRegex.Matches (section)) {
                if (mi.Success) {
                    var key = $"{prefix}.{mi.Groups [1].Value}";
                    var xmlComment = mi.Groups [2].Value.Trim ().Replace ("<p>", string.Empty).Replace ("</p>", string.Empty);
                    if (string.IsNullOrWhiteSpace (xmlComment) || d.ContainsKey (key))
                        continue;
                    //UnityEngine.Debug.Log ($"{key} => {xmlComment}");
                    d.Add (key, xmlComment);
                }
            }
        }

        static readonly System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create ();

        /// <summary>
        /// Calculate MD5 checksum of supplied string
        /// </summary>
        static string ComputeMD5Hash (string s) {
            var hash = md5.ComputeHash (System.Text.Encoding.UTF8.GetBytes (s));
            var sb = new System.Text.StringBuilder ();
            for (var i = 0; i < hash.Length; ++i) {
                sb.Append (hash [i].ToString ("x2"));
            }
            return sb.ToString ();
        }

        /// <summary>
        /// Update cached xml documentation file
        /// </summary>
        static void UpdateDocumentationCache (string url, IDictionary<string, string> dict) {
            var filename = GenerateCacheName (url);
            using var fs = new FileStream (filename, FileMode.Create);
            using var sw = new StreamWriter (fs);
            sw.Write ($"<?xml version=\"1.0\"?>\n<doc>\n\t<!-- scraped from {url} -->\n\t<members>\n");
            var en = dict.GetEnumerator ();
            while (en.MoveNext ()) {
                sw.Write ("\t\t<member name=\"" + en.Current.Key);
                sw.Write ("\">\n\t\t\t<summary>" + System.Security.SecurityElement.Escape (en.Current.Value));
                sw.Write ("</summary>\n\t\t</member>\n");
            }
            sw.Write ("\t</members>\n</doc>\n");

            //UnityEngine.Debug.Log ($"Cached doc for {url} in {filename}");
        }

        /// <summary>
        /// Attempt to load cached version of url and read the parsed xml documentation from it. Returns true if succesful, false otherwise.
        /// </summary>
        static bool LoadDocumentationCache (string url) {
            var filename = GenerateCacheName (url);
            if (!File.Exists (filename))
                return false;

            //UnityEngine.Debug.Log ($"Loading cached {url} from {filename}");

            LoadXMLCache (filename);
            return true;
        }

        /// <summary>
        /// Parses xml documentation file and add/replace to documentation dictionary
        /// </summary>
        static void LoadXMLCache (string filename) {
            using var streamReader = new StreamReader (new FileStream (filename, FileMode.Open));
            using var xmlReader = XmlReader.Create (streamReader);
            while (xmlReader.Read ()) {
                if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "member") {
                    var key = xmlReader ["name"];
                    xmlReader.ReadToFollowing ("summary");
                    var val = xmlReader.ReadInnerXml ();
                    if (!string.IsNullOrWhiteSpace (val)) {
                        Documentation [key] = val;
                        //UnityEngine.Debug.Log ($"FROM CACHE: {raw_name} => {str}");
                    }
                }
            }
        }

        static string CachePath { get { return Path.Combine (UnityEngine.Application.temporaryCachePath, "aranciaCache"); } }

        static string GenerateCacheName (string url) {
            return Path.Combine (CachePath, ComputeMD5Hash (url) + ".xml");
        }

        [MenuItem ("Tools/Arancia/Clear cache")]
        static void ClearCache () {
            Directory.Delete (CachePath, recursive: true);
            Documentation.Clear ();
            LoadedTypes.Clear ();
            Directory.CreateDirectory (CachePath);
        }
    }
}
