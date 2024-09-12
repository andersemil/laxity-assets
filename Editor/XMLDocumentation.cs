using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine.Networking;

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
		/// List of scanned types so we don't do it over and over again
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
		/// Attempt to find documentation for the given member
		/// </summary>
        static string GetDocumentation (Type declaringType, string key) {
            if (Documentation.TryGetValue (key, out string documentation))
                return documentation;
            if (LoadXmlDocumentation (declaringType, key, out documentation))
                return documentation;
            GenerateDocumentationForType (declaringType);
            Documentation.TryGetValue (key, out documentation);
            return documentation;
        }

        /// <summary>
		/// Regex to match any using statement
		/// </summary>
        static readonly Regex UsingRegex = new ("using\\s+(\\S+);", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any namespace definition
		/// </summary>
        static readonly Regex NamespaceRegex = new ("namespace\\s+(\\S+)\\s*{", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any class definition
		/// </summary>
        static readonly Regex ClassRegex = new ("(?:public|private|protected|internal)\\s+?((?:partial)\\s+?)?class\\s+(\\S+)\\s+(?::.*?)?{", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Regex to match any documented property, field or method
		/// </summary>
        static readonly Regex DocRegex = new ("\\/\\/\\/ <summary>(.*?)\\/\\/\\/ <\\/summary>.*?(?:(public|private|protected|internal)\\s+)?(?:(?:static\\s+)?\\S+\\s+(\\S+)\\s+\\(\\s*(?:(\\S+)\\s+\\S+)?\\s*\\)|(\\S+)\\s*?;|\\S+\\s+(\\S+)\\s+?{.*?})", RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Regex CommentNewlineRegex = new ("\\s+\\/\\/\\/ ", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
		/// Paths of all scripts in the AssetDatabase, including packages, populated in the LoadScriptPaths method
		/// </summary>
        static IEnumerable<string> ScriptPaths;

        [UnityEditor.InitializeOnLoadMethod]
        //[UnityEditor.Callbacks.DidReloadScripts]
        static void InitOnLoad () {
            /*var globalCacheFilename = Path.Combine (UnityEngine.Application.temporaryCachePath, "globalcache");
            if (File.Exists (globalCacheFilename)) {
                LoadXMLCache (globalCacheFilename);
            }*/
            var scriptGuids = UnityEditor.AssetDatabase.FindAssets ("t:Script");
            ScriptPaths = scriptGuids.Select (guid => UnityEditor.AssetDatabase.GUIDToAssetPath (guid)).Where (path => !path.Contains ("Editor/") && !path.EndsWith (".dll"));
        }

        /// <summary>
		/// Add documentation manually for specific type. Necessary since Unity 2021.3 and earlier (at least) does not generate XML documentation when compiling scripts
		/// </summary>
        static void GenerateDocumentationForType (Type type) {
            var typeFullName = type.FullName;
            if (typeFullName.StartsWith ("UnityEngine.")) {
                if (
                    type.Namespace == "UnityEngine.UI"
                 //|| type.Namespace == "UnityEngine.Experimental.Rendering.Universal"
                 //|| type.Namespace == "UnityEngine.Rendering.Universal"
                 ) {
                    //Scrape core package documentation
                    ScrapeUnityDocumentation ($"https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/{typeFullName}.html", typeFullName, UnityPackageDocMemberRegex);
                } else {
                    //Scrape built-in package documentation
                    UnityEngine.Debug.Log ($"GenerateDocumentationForType {typeFullName}");
                    var typeName = type.FullName.Replace ("UnityEngine.", string.Empty);
                    var docVersionString = UnityEngine.Application.unityVersion.Substring (0, UnityEngine.Application.unityVersion.LastIndexOf ('.'));
                    ScrapeUnityDocumentation ($"https://docs.unity3d.com/{docVersionString}/Documentation/ScriptReference/{typeName}.html", typeFullName, UnityEngineDocMemberRegex);
                }
                return;
            }

            if (typeFullName.StartsWith ("System."))
                return;
            if (!LoadedTypes.Add (typeFullName))
                return;

            var naivePath = ScriptPaths.FirstOrDefault (path => Path.GetFileName (path) == $"{type.Name}.cs");
            if (naivePath != null && ScanForDocumentation (naivePath, type))
                return;

            UnityEngine.Debug.Log ($"XMLDocumentation: Naive path for {typeFullName} not found, scanning whole directory ...");
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

            var className = ClassRegex.Matches (srcFile).Select (mi => mi.Groups [1].Value).FirstOrDefault (s => s == type.Name);
            if (string.IsNullOrWhiteSpace (className))
                return false;

            var nameSpaceAndClass = !string.IsNullOrWhiteSpace (nameSpace) ? $"{nameSpace}.{className}" : className;
            if (type.FullName != nameSpaceAndClass) {
                UnityEngine.Debug.LogError ($"Namespace/Class mismatch {type.FullName} <> {nameSpaceAndClass}");
                return false;
            }
            nameSpaceAndClass += ".";

            //Debug.Log ($"{type.FullName} found in {filename}, scanning for documentation");
            string key;
            foreach (Match mi in DocRegex.Matches (srcFile)) {
                //Ignore non-public documented members
                if (!mi.Groups [2].Success || mi.Groups [2].Value != "public")
                    continue;
                var xmlComment = CommentNewlineRegex.Replace (mi.Groups [1].Value, " ").Trim ();
                if (mi.Groups [3].Success) {
                    key = $"M:{nameSpaceAndClass}{mi.Groups [3].Value}";
                    if (mi.Groups [4].Success)
                        key += $"({ClassifyType (nameSpaceAndClass, usingNamespaces, mi.Groups [4].Value)})";
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

        static readonly Regex UnityPackageDocMemberRegex = new ("<h4 .+?data-uid=\"UnityEngine\\..+?\\..+?\\.(.+?)\">.+?<div class=\".*? summary.*?\">(.*?)<\\/div>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex UnityDocSectionRegex = new ("<h3.+?>(.+?)<\\/h3>", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex UnityEngineDocMemberRegex = new ("<td class=\"lbl\">.*?<a href=\".*?\">(.+?)<\\/a>.*?<td class=\"desc\">(.*?)<\\/td>", RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Dictionary<string, UnityWebRequestAsyncOperation> AsyncDownloads = new ();

		/// <summary>
		/// Scrape online documentation for description of members of a specific class.
		/// As this method is called frequently when UI is repainted, we can create async ops and monitor them so we don't block the main/UI thread
		/// </summary>
		static bool ScrapeUnityDocumentation (string url, string nameSpaceAndClass, Regex memberRegex) {
            if (LoadDocumentationCache (url))
                return true;

            if (!AsyncDownloads.TryGetValue (url, out UnityWebRequestAsyncOperation asyncOp)) {
                UnityEngine.Debug.Log ($"Scraping doc for {nameSpaceAndClass} from {url}");
                var req = UnityWebRequest.Get (url);
                asyncOp = req.SendWebRequest ();
                AsyncDownloads.Add (url, asyncOp);
            }
            if (!asyncOp.isDone)
                return true;
            AsyncDownloads.Remove (url);

            if (asyncOp.webRequest.result != UnityWebRequest.Result.Success) {
                UnityEngine.Debug.LogError ($"Error while scraping doc at {url} => {asyncOp.webRequest.error}");
                asyncOp.webRequest.Dispose ();
                return false;
            }
            var doc = asyncOp.webRequest.downloadHandler.text;
            asyncOp.webRequest.Dispose ();

            Match prevMatch = null;
            string propertiesSection = null;
            string fieldsSection = null;
            string methodsSection = null;
            foreach (Match mi in UnityDocSectionRegex.Matches (doc)) {
                if (mi.Success) {
                    if (prevMatch != null) {
                        var sectionStartIndex = prevMatch.Index + prevMatch.Length;
                        var section = doc.Substring (sectionStartIndex, mi.Index - sectionStartIndex);
                        var sectionHeader = prevMatch.Groups [1].Value.Trim ();
                        //UnityEngine.Debug.Log ($"{sectionHeader} section: {sectionStartIndex} - {mi.Index}");
                        switch (sectionHeader) {
                        case "Methods":
                        case "Public Methods":
                            methodsSection = section;
                            break;
                        case "Fields":
                            fieldsSection = section;
                            break;
                        case "Properties":
                            propertiesSection = section;
                            break;
                        }
                    }
                    prevMatch = mi;
                }
            }

            var d = new Dictionary<string, string> ();

            if (!string.IsNullOrWhiteSpace (methodsSection)) {
                ScrapeUnityDocSection (d, $"M:{nameSpaceAndClass}", methodsSection, memberRegex);
            }
            if (!string.IsNullOrWhiteSpace (fieldsSection)) {
                ScrapeUnityDocSection (d, $"F:{nameSpaceAndClass}", fieldsSection, memberRegex);
            }
            if (!string.IsNullOrWhiteSpace (propertiesSection)) {
                ScrapeUnityDocSection (d, $"P:{nameSpaceAndClass}", propertiesSection, memberRegex);
            }

            UpdateDocumentationCache (url, d);

            var en = d.GetEnumerator ();
            while (en.MoveNext ())
                Documentation.Add (en.Current.Key, en.Current.Value);

            return true;
        }

        /// <summary>
		/// Scrape a section of online documentation for members and their description
		/// </summary>
        static void ScrapeUnityDocSection (IDictionary<string, string> d, string prefix, string docSection, Regex memberRegex) {
            foreach (Match mi in memberRegex.Matches (docSection)) {
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

        static string ComputeMD5Hash (string s) {
            using var md5 = System.Security.Cryptography.MD5.Create ();
            var hash = md5.ComputeHash (System.Text.Encoding.UTF8.GetBytes (s));
            var sb = new System.Text.StringBuilder ();
            for (var i = 0; i < hash.Length; ++i) {
                sb.Append (hash [i].ToString ("x2"));
            }
            return sb.ToString ();
        }

        static void UpdateDocumentationCache (string url, IDictionary<string, string> dict) {
            var filename = Path.Combine (UnityEngine.Application.temporaryCachePath, ComputeMD5Hash (url));
            using var fs = new FileStream (filename, FileMode.Create);
            using var sw = new StreamWriter (fs);
            sw.Write ("<?xml version=\"1.0\"?>\n<doc>\n\t<members>\n");
            var en = dict.GetEnumerator ();
            while (en.MoveNext ()) {
                sw.Write ("\t\t<member name=\"" + en.Current.Key);
                sw.Write ("\">\n\t\t\t<summary>" + System.Security.SecurityElement.Escape (en.Current.Value));
                sw.Write ("</summary>\n\t\t</member>\n");
            }
            sw.Write ("\t</members>\n</doc>\n");

            UnityEngine.Debug.Log ($"Cached doc for {url} in {filename}");
        }

        static bool LoadDocumentationCache (string url) {
            var filename = Path.Combine (UnityEngine.Application.temporaryCachePath, ComputeMD5Hash (url));
            if (!File.Exists (filename))
                return false;

            UnityEngine.Debug.Log ($"Loading cached {url} from {filename}");

            LoadXMLCache (filename);
            return true;
        }

        static void LoadXMLCache (string filename) {
            using var xmlReader = XmlReader.Create (new StreamReader (new FileStream (filename, FileMode.Open)));
            while (xmlReader.Read ()) {
                if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "member") {
                    var raw_name = xmlReader ["name"];
                    xmlReader.ReadToFollowing ("summary");
                    var str = xmlReader.ReadInnerXml ();
                    if (!string.IsNullOrWhiteSpace (str)) {
                        Documentation [raw_name] = str;
                        //UnityEngine.Debug.Log ($"FROM CACHE: {raw_name} => {str}");
                    }
                }
            }
        }
    }
}
