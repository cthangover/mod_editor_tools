using System;
using System.Collections.Generic;
using System.IO;
using Cthangover.Core.Mods;
using Cthangover.Core.Scenes;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Controller for the scene builder tool. Composes a preview from a base
    /// scene (a native Godot scene from <c>res://scenes</c>, or an empty root)
    /// and a mod-provided <c>.tscn</c> scene layered on top of it via the
    /// runtime tscn pipeline (<see cref="TscnSceneResolver"/>). Builds an
    /// interactive node hierarchy tree, tracks selection for the highlight
    /// overlay, and reads/writes the raw tscn source so the tool can edit and
    /// save mod scenes in place. Mod scene listing is sourced from
    /// <see cref="TscnSceneResolver.Collect"/> (deduplicated by mod load order),
    /// while base scenes come from <see cref="ModScenes.CollectTscnFiles"/>.
    /// </summary>
    public class SceneBuilderController
    {
        private const string ResScenesPrefix = "res://scenes/";

        private Node _loadedRoot;
        private Node _modRoot;
        private ColorRect _highlight;
        private readonly Dictionary<Node, TreeItem> _nodeToItem = new();
        private readonly Dictionary<TreeItem, Node> _itemToNode = new();

        /// <summary>
        /// Returns the native base scenes available under <c>res://scenes</c> as
        /// (Display, Path) tuples. Display is the path relative to
        /// <c>res://scenes/</c> so nested scenes remain distinguishable.
        /// </summary>
        public List<(string Display, string Path)> GetBaseSceneList()
        {
            var result = new List<(string, string)>();
            foreach (var (_, path) in ModScenes.CollectTscnFiles())
            {
                var display = path;
                if (display.StartsWith(ResScenesPrefix, StringComparison.Ordinal))
                    display = display.Substring(ResScenesPrefix.Length);
                result.Add((display, path));
            }
            return result;
        }

        /// <summary>
        /// Returns every mod-provided tscn scene as (ModId, Name, RelativePath)
        /// tuples, deduplicated by mod load order. RelativePath is the key used
        /// by <see cref="TscnSceneResolver.LoadAndBuild"/> (e.g. "scenes/foo.tscn").
        /// </summary>
        public List<(string ModId, string Name, string RelativePath)> GetModSceneIndex()
        {
            var result = new List<(string, string, string)>();
            var resolver = ModManager.Instance.GetResolver<TscnSceneResolver>();
            var index = resolver?.Collect();
            if (index == null)
                return result;

            foreach (var kvp in index)
            {
                var relativePath = kvp.Key;
                var modId = kvp.Value.ModId;
                result.Add((modId, Path.GetFileName(relativePath), relativePath));
            }

            result.Sort((a, b) =>
            {
                var modCompare = string.Compare(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase);
                return modCompare != 0 ? modCompare : string.Compare(a.Item3, b.Item3, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>
        /// Loads a native base scene into <paramref name="viewport"/> as the root.
        /// Replaces any previously loaded scene and attaches the selection
        /// highlight overlay.
        /// </summary>
        public Node LoadBaseScene(string path, SubViewport viewport)
        {
            UnloadScene();

            var packed = GD.Load<PackedScene>(path);
            if (packed == null)
                return null;

            var instance = packed.Instantiate();
            viewport.AddChild(instance);
            AttachHighlight(instance);

            _loadedRoot = instance;
            return instance;
        }

        /// <summary>
        /// Loads an empty full-rect <see cref="Control"/> as the preview root,
        /// used when no base scene is selected so mod scenes still have a parent.
        /// </summary>
        public Node LoadEmptyScene(SubViewport viewport)
        {
            UnloadScene();

            var root = new Control { Name = "EmptyScene" };
            root.AnchorRight = 1f;
            root.AnchorBottom = 1f;
            viewport.AddChild(root);
            AttachHighlight(root);

            _loadedRoot = root;
            return root;
        }

        /// <summary>
        /// Builds a mod scene via the runtime tscn pipeline and adds it as a child
        /// of the current base root. Returns the built node, or null if there is
        /// no base root or the scene could not be resolved.
        /// </summary>
        public Node LoadModScene(string relativePath)
        {
            UnloadModScene();
            if (_loadedRoot == null)
                return null;

            var node = TscnScenes.LoadAndBuild(relativePath);
            if (node == null)
                return null;

            _loadedRoot.AddChild(node);
            _modRoot = node;
            return node;
        }

        /// <summary>Reads the raw tscn source of a mod scene through its file provider.</summary>
        public string ReadSceneText(string modId, string relativePath)
        {
            return ModManager.Instance.ReadFileText(modId, relativePath);
        }

        /// <summary>
        /// Writes the edited tscn source back to the mod on disk. Returns false
        /// when the mod has no filesystem backing (e.g. zip mods).
        /// </summary>
        public bool SaveSceneText(string modId, string relativePath, string text)
        {
            var absolutePath = ModManager.Instance.GetFileSystemPath(modId, relativePath);
            if (string.IsNullOrEmpty(absolutePath))
                return false;

            File.WriteAllText(absolutePath, text);
            return true;
        }

        /// <summary>
        /// Invalidates the resolver cache for one scene so a subsequent reload
        /// re-parses the edited file instead of returning the cached build.
        /// </summary>
        public void InvalidateScene(string relativePath)
        {
            ModManager.Instance.GetResolver<TscnSceneResolver>()?.Invalidate(relativePath);
        }

        /// <summary>Root node of the loaded mod scene; null when no mod scene is loaded.</summary>
        public Node GetModRoot()
        {
            return _modRoot;
        }

        /// <summary>
        /// Creates a live instance of the given Godot class under the parent with
        /// a sibling-unique name and returns it, or null when ClassDB cannot
        /// instantiate the type. The caller is responsible for the matching tscn
        /// write-back (and for rolling the live node back if that fails).
        /// </summary>
        public Node CreateUniqueChild(Node parentNode, string typeName)
        {
            if (parentNode == null || string.IsNullOrEmpty(typeName))
                return null;

            var instance = ClassDB.Instantiate(typeName).As<Node>();
            if (instance == null)
                return null;

            var name = typeName;
            var suffix = 2;
            while (parentNode.HasNode(name))
                name = typeName + suffix++;

            instance.Name = name;
            parentNode.AddChild(instance);
            return instance;
        }

        /// <summary>
        /// Detaches the node from the preview tree immediately (so hierarchy
        /// rebuilds right away don't show a ghost) and queues it for deletion.
        /// Returns false for nodes outside the mod subtree or the mod root itself.
        /// </summary>
        public bool DeleteModNode(Node node)
        {
            if (!IsEditableModNode(node) || node == _modRoot)
                return false;

            var parent = node.GetParent();
            parent?.RemoveChild(node);
            node.QueueFree();
            return true;
        }

        /// <summary>Frees the loaded scene root (and its mod scene child) and clears mappings.</summary>
        public void UnloadScene()
        {
            _nodeToItem.Clear();
            _itemToNode.Clear();
            _highlight = null;
            _modRoot = null;

            if (_loadedRoot != null && GodotObject.IsInstanceValid(_loadedRoot))
                _loadedRoot.QueueFree();

            _loadedRoot = null;
        }

        private void UnloadModScene()
        {
            if (_modRoot == null)
                return;

            if (_loadedRoot != null && GodotObject.IsInstanceValid(_loadedRoot) && _modRoot.GetParent() == _loadedRoot)
                _loadedRoot.RemoveChild(_modRoot);

            if (GodotObject.IsInstanceValid(_modRoot))
                _modRoot.QueueFree();

            _modRoot = null;
        }

        private void AttachHighlight(Node root)
        {
            _highlight = new ColorRect();
            _highlight.Color = new Color(1f, 0.7f, 0f, 0.35f);
            _highlight.Visible = false;
            _highlight.MouseFilter = Control.MouseFilterEnum.Ignore;
            _highlight.ZIndex = 4096;
            root.AddChild(_highlight);
            _highlight.Owner = root;
        }

        /// <summary>
        /// Recursively walks the loaded scene tree and populates the given
        /// <see cref="Tree"/> control with nodes labelled <c>Name (Type)</c>.
        /// Skips the internal highlight rect.
        /// </summary>
        public void BuildHierarchy(Tree tree)
        {
            tree.Clear();
            _nodeToItem.Clear();
            _itemToNode.Clear();

            if (_loadedRoot == null)
                return;

            var treeRoot = tree.CreateItem();
            tree.HideRoot = true;
            AddNodeToTree(tree, treeRoot, _loadedRoot);
        }

        private void AddNodeToTree(Tree tree, TreeItem parent, Node node)
        {
            var item = tree.CreateItem(parent);
            var typeName = node.GetType().Name;
            item.SetText(0, $"{node.Name} ({typeName})");
            item.SetSelectable(0, true);

            _nodeToItem[node] = item;
            _itemToNode[item] = node;

            for (int i = 0; i < node.GetChildCount(); i++)
            {
                var child = node.GetChild(i);
                if (child != _highlight)
                    AddNodeToTree(tree, item, child);
            }
        }

        /// <summary>
        /// Highlights the selected node by positioning the highlight rect over it.
        /// For <see cref="Control"/> nodes, uses <c>GetGlobalRect()</c>; for others,
        /// approximates the position via parent chain traversal. Hides the highlight
        /// if the root node itself is selected.
        /// </summary>
        public void SelectNode(TreeItem item)
        {
            if (_highlight == null || item == null)
            {
                HideHighlight();
                return;
            }

            if (!_itemToNode.TryGetValue(item, out var node))
            {
                HideHighlight();
                return;
            }

            if (node == _loadedRoot)
            {
                HideHighlight();
                return;
            }

            var control = node as Control;
            if (control != null)
            {
                var rect = control.GetGlobalRect();
                _highlight.Position = rect.Position;
                _highlight.Size = rect.Size;
                _highlight.Visible = true;
            }
            else
            {
                var pos = new Vector2(0, 0);
                var current = node;
                while (current != _loadedRoot && current != null)
                {
                    if (current is Control c)
                    {
                        pos += c.GetGlobalRect().Position;
                        break;
                    }
                    current = current.GetParent();
                }

                _highlight.Position = pos;
                _highlight.Size = new Vector2(20, 20);
                _highlight.Color = new Color(1f, 0.3f, 0.3f, 0.5f);
                _highlight.Visible = true;
            }
        }

        public TreeItem GetItemForNode(Node node)
        {
            _nodeToItem.TryGetValue(node, out var item);
            return item;
        }

        /// <summary>Reverse lookup of the hierarchy item for a live node; null when unmapped.</summary>
        public Node GetNodeForItem(TreeItem item)
        {
            return _itemToNode.TryGetValue(item, out var node) ? node : null;
        }

        /// <summary>
        /// True when the node belongs to the loaded mod scene subtree (including
        /// the mod root itself) and therefore may be edited by the inspector.
        /// Base-scene nodes and tool internals are excluded so edits never leak
        /// into files this tool cannot save.
        /// </summary>
        public bool IsEditableModNode(Node node)
        {
            if (node == null || _modRoot == null || node == _highlight)
                return false;

            for (Node current = node; current != null; current = current.GetParent())
            {
                if (current == _modRoot)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Address path of a mod node in .tscn terms, relative to the mod scene
        /// root ("", "Panel", "Panel/Btn") — exactly what TscnTextSurgery needs to
        /// locate the matching <c>[node]</c> section. Returns "" for the root node
        /// itself (its section has no parent attribute) and null for non-editable nodes.
        /// </summary>
        public string GetModNodePath(Node node)
        {
            if (!IsEditableModNode(node))
                return null;
            if (node == _modRoot)
                return "";

            var segments = new List<string>();
            for (Node current = node; current != null && current != _modRoot; current = current.GetParent())
                segments.Add(current.Name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        public Node FindNodeAtPosition(Vector2 position)
        {
            if (_loadedRoot == null)
                return null;
            return FindNodeAtPositionRecursive(_loadedRoot, position);
        }

        private Node FindNodeAtPositionRecursive(Node node, Vector2 position)
        {
            for (int i = node.GetChildCount() - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (child == _highlight)
                    continue;
                var hit = FindNodeAtPositionRecursive(child, position);
                if (hit != null)
                    return hit;
            }

            if (node == _loadedRoot)
                return null;

            if (node is Control control && control.GetGlobalRect().HasPoint(position))
                return node;

            return null;
        }

        /// <summary>Hides the node highlight overlay and resets its colour.</summary>
        public void HideHighlight()
        {
            if (_highlight != null)
                _highlight.Visible = false;
            _highlight.Color = new Color(1f, 0.7f, 0f, 0.35f);
        }
    }
}
