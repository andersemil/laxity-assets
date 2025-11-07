# Arancia Editor Assets
## Deep-copy a folder, re-mapping all references to assets!
Select a folder in the Project window and hit CTRL+Shift+D (or right-click and select Ariancia/Deep Copy menu item), you will be prompted for a simple find-and-replace on asset names, and hit OK. A new folder will be created with all the same assets, but with references to other assets inside the new folder, instead of the standard behaviour of retaining references to the original folder.

## Add tooltips to your UnityEvents! They will display correctly in the Inspector
```
    [Tooltip ("Invoked with the world speed of the transform (units per second)")]
    UnityEvent OnUpdateSpeed;
```
<img width="306" alt="Screenshot 2024-02-27 at 16 00 49" src="https://github.com/andersemil/laxity-assets/assets/13269688/f6effe56-7925-4b97-b9e7-af12308673f9">


## See XML comments or online documentation as tooltips on listeners directly in the Inspector
![Screenshot 2023-10-27 at 16 04 37](https://github.com/andersemil/laxity-assets/assets/13269688/5aa56023-262d-43ae-ad71-bb36a9d066df)

Works for Unity packages as well as for custom scripts and plugins in the project!

## Display XML comments as tooltips in Inspector
If properties do not have a TooltipAttribute, but an XML comment or online documentation, that will be displayed as a tooltip. Works for any Unity package and custom scripts and plugins in the project!

## Match invokation method when dropping new listener target
When you drop a new target object on a UnityEvent listener, we try to match the Component and method previously invoked. Normal UnityEventDrawer will just reset the listener. This makes it much easier to quickly replace listener targets.

## Drag and drop new listener targets directly on an event
You can select multiple GameObjects in a loaded scene and drag them on an event-- a listener will be created for each of the objects ready for you to select a method to invoke on each.

## Find strings or method invokations anywhere in the open scenes by typing a few characters and choosing from a cool dropdown menu!
<img width="901" alt="Screenshot 2024-02-27 at 16 08 21" src="https://github.com/andersemil/laxity-assets/assets/13269688/043c837a-0dff-4d3b-85f4-610beae08640">
<img width="900" alt="Screenshot 2024-02-27 at 16 08 47" src="https://github.com/andersemil/laxity-assets/assets/13269688/e611df5d-e2e3-4314-b7b3-983046f18f0c">

The property will be highlighted exactly where the string or method invokation occurs! This works for all properties in all standard and custom inspectors!
<img width="1188" alt="Screenshot 2024-02-27 at 16 06 47" src="https://github.com/andersemil/laxity-assets/assets/13269688/b31f2abf-81c8-4a9a-bd42-a89193b1ef4b">


## Find references to any asset or object
Right-clicking on any asset in the Project Window or the Scene Hierarchy and selecting 'Arancia / Find references' will open the Finder window and list all references to it in the open scenes. If there is only one, it will immediately be highlighted on it's exact location in the Inspector.


## Find missing references and missing scripts in open scenes
Just click the button in the Finder window and all missing references or scripts will be listed. If a script has gone missing since the plugin was first installed, you can see the fully qualified name of the Component in the inspector.


## Find all objects in a specific layer or with a specific tag
Simply choose the layer or tag from the dropdown in the Finder window and you will instantly get a list of all objects in the selected layer or with the specified tag.


## Get list of all sprites included in a legacy sprite atlas (pre-Unity 2022)
If you import the tools in a Unity version before 2022, you will have the option to find all sprite assets in the project included in a specific atlas.


## Quickly create new SpriteAtlas from context menu (new Unity asset type)
Simply select all the sprites you would like to include in a SpriteAtlas asset, right-click and select Create SpriteAtlas. The new asset is automatically created and you can give it a name.
