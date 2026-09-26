using System;
using System.Collections.Generic;
using Cthangover.Core.Interactive;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Factories.Impls;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.InteractiveEditor
{
	public partial class InteractiveEditorWindow : ToolWindow
	{
		private static readonly Color _rectColor = UiTokens.Alpha(UiTokens.Success, 0.6f);
		private static readonly Color _circleColor = UiTokens.Alpha(UiTokens.Info, 0.6f);
		private static readonly Color _vertexColor = UiTokens.Alpha(UiTokens.AccentBright, 0.8f);
		private static readonly Color _colliderOverlayColor = UiTokens.Alpha(UiTokens.Success, 0.15f);
		private static readonly Color _colliderCircleOverlayColor = UiTokens.Alpha(UiTokens.Info, 0.15f);
		private static readonly Color _colliderPolyOverlayColor = UiTokens.Alpha(UiTokens.Accent, 0.15f);
		private static readonly Color _colliderPolyOutlineColor = UiTokens.Alpha(UiTokens.Accent, 0.6f);

		private InteractiveEditorController _controller = new();

		private Control _previewContainer;
		private TextureRect _preview;
		private Control _colliderOverlay;
		private string _currentBgId;

		private readonly List<InteractiveEditorHandle> _handles = new();

		private TextEdit _jsonEdit;

		private LineEdit _idEdit, _textureEdit;
		private OptionButton _layerSelect;
		private OptionButton _hitTypeSelect;
		private ItemList _bgList;

		private LineEdit _rectX, _rectY, _rectW, _rectH;
		private LineEdit _circleX, _circleY, _circleR;
		private VBoxContainer _rectParams, _circleParams, _polyParams;

		private ColorPickerButton _highlightColorBtn;
		private LineEdit _highlightScaleEdit;

		private LineEdit _clickScenario, _hoverEnter, _hoverLeave;
		private TextEdit _clickCommands;

		private VBoxContainer _vertexList;
		private Button _addVertexBtn;

		private bool _suppressingSidebarEvents;
		private int _draggingHandleIndex = -1;

		public InteractiveEditorWindow() : base("tools/interactive_editor/title")
		{
			Size = new Vector2I(1280, 820);
			CthangoverTheme.Apply(this);

			var outer = CreateFillContainer();
			AddChild(outer);

			var toolbar = CreateToolbar();
			outer.AddChild(toolbar);

			var loadBtn = new Button { Text = Tr("tools/interactive_editor/btn_load_json") };
			loadBtn.Pressed += OnLoadJson;
			toolbar.AddChild(loadBtn);

			var saveBtn = new Button { Text = Tr("tools/interactive_editor/btn_save") };
			saveBtn.Pressed += OnSave;
			toolbar.AddChild(saveBtn);

			var importDefBtn = new Button { Text = Tr("tools/interactive_editor/btn_import_mod") };
			importDefBtn.Pressed += OnImportFromMod;
			toolbar.AddChild(importDefBtn);

			var mainHBox = new HBoxContainer();
			mainHBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			mainHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			outer.AddChild(mainHBox);

			var sidebarVBox = new VBoxContainer();
			var sidebar = CreateSidebar(sidebarVBox, 290);
			mainHBox.AddChild(sidebar);

			BuildSidebar(sidebarVBox);

			_previewContainer = new Control
			{
				MouseFilter = Control.MouseFilterEnum.Stop,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill
			};
			mainHBox.AddChild(_previewContainer);

			_preview = new TextureRect
			{
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				AnchorRight = 1f,
				AnchorBottom = 1f
			};
			_previewContainer.AddChild(_preview);

			_colliderOverlay = new Control
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Visible = false
			};
			_colliderOverlay.Connect("draw", Callable.From((Action)DrawColliderShape));
			_previewContainer.AddChild(_colliderOverlay);

			_previewContainer.Resized += () =>
			{
				RebuildHandles();
				UpdateColliderOverlay();
			};

			_jsonEdit = new TextEdit { CustomMinimumSize = new Vector2(0, 150) };
			_jsonEdit.AddThemeFontOverride("font", GetMonospaceFont());
			_jsonEdit.TextChanged += () => SetDirty();
			outer.AddChild(_jsonEdit);

			RefreshJson();
		}

		private void BuildSidebar(VBoxContainer sidebar)
		{
			sidebar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			var tabs = new TabContainer();
			tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			sidebar.AddChild(tabs);

			var bgTab = new VBoxContainer();
			tabs.AddChild(bgTab);
			tabs.SetTabTitle(0, Tr("tools/interactive_editor/tab_background"));
			BuildBackgroundTab(bgTab);

			var interactiveTab = new VBoxContainer();
			tabs.AddChild(interactiveTab);
			tabs.SetTabTitle(1, Tr("tools/interactive_editor/tab_interactive"));
			BuildInteractiveTab(interactiveTab);

			var scenarioTab = new VBoxContainer();
			tabs.AddChild(scenarioTab);
			tabs.SetTabTitle(2, Tr("tools/interactive_editor/tab_scenario"));
			BuildScenarioTab(scenarioTab);
		}

		private void BuildBackgroundTab(VBoxContainer parent)
		{
			AddLabel(parent, "tools/interactive_editor/label_id");
			_idEdit = AddLineEdit(parent, "my_interactive");
			_idEdit.TextChanged += OnParamChanged;

			AddLabel(parent, "tools/interactive_editor/label_texture_key");
			_textureEdit = AddLineEdit(parent, "lamp");
			_textureEdit.TextChanged += OnParamChanged;

			AddLabel(parent, "tools/interactive_editor/label_layer");
			_layerSelect = new OptionButton();
			_layerSelect.AddItem("foreground");
			_layerSelect.AddItem("background");
			_layerSelect.AddItem("ui");
			_layerSelect.ItemSelected += _ => OnParamChanged();
			parent.AddChild(_layerSelect);

			AddLabel(parent, "tools/interactive_editor/label_bg_preview");
			_bgList = new ItemList();
			_bgList.CustomMinimumSize = new Vector2(0, 100);
			_bgList.ItemSelected += OnBgSelected;
			parent.AddChild(_bgList);
			LoadBackgroundList();
		}

		private void BuildInteractiveTab(VBoxContainer parent)
		{
			AddLabel(parent, "tools/interactive_editor/label_collider_type");
			_hitTypeSelect = new OptionButton();
			_hitTypeSelect.AddItem("rect");
			_hitTypeSelect.AddItem("circle");
			_hitTypeSelect.AddItem("polygon");
			_hitTypeSelect.ItemSelected += OnHitTypeChanged;
			parent.AddChild(_hitTypeSelect);

			_rectParams = new VBoxContainer();
			AddLabel(_rectParams, "tools/interactive_editor/label_rect_params");
			_rectX = AddLabeledLine(_rectParams, "tools/interactive_editor/label_x"); _rectX.TextChanged += OnParamChanged;
			_rectY = AddLabeledLine(_rectParams, "tools/interactive_editor/label_y"); _rectY.TextChanged += OnParamChanged;
			_rectW = AddLabeledLine(_rectParams, "tools/interactive_editor/label_w"); _rectW.TextChanged += OnParamChanged;
			_rectH = AddLabeledLine(_rectParams, "tools/interactive_editor/label_h"); _rectH.TextChanged += OnParamChanged;
			parent.AddChild(_rectParams);

			_circleParams = new VBoxContainer();
			AddLabel(_circleParams, "tools/interactive_editor/label_circle_params");
			_circleX = AddLabeledLine(_circleParams, "tools/interactive_editor/label_cx"); _circleX.TextChanged += OnParamChanged;
			_circleY = AddLabeledLine(_circleParams, "tools/interactive_editor/label_cy"); _circleY.TextChanged += OnParamChanged;
			_circleR = AddLabeledLine(_circleParams, "tools/interactive_editor/label_r"); _circleR.TextChanged += OnParamChanged;
			_circleParams.Visible = false;
			parent.AddChild(_circleParams);

			_polyParams = new VBoxContainer();
			AddLabel(_polyParams, "tools/interactive_editor/label_polygon_vertices");
			_addVertexBtn = new Button { Text = Tr("tools/interactive_editor/btn_add_vertex") };
			_addVertexBtn.Pressed += OnAddVertex;
			_polyParams.AddChild(_addVertexBtn);
			_vertexList = new VBoxContainer();
			_polyParams.AddChild(_vertexList);
			_polyParams.Visible = false;
			parent.AddChild(_polyParams);

			AddLabel(parent, "tools/interactive_editor/label_highlight_color");
			_highlightColorBtn = new ColorPickerButton
			{
				Color = UiTokens.Alpha(UiTokens.Accent, 0.2f),
				CustomMinimumSize = new Vector2(40, 30)
			};
			_highlightColorBtn.ColorChanged += _ => OnParamChanged();
			parent.AddChild(_highlightColorBtn);

			AddLabel(parent, "tools/interactive_editor/label_highlight_scale");
			_highlightScaleEdit = AddLineEdit(parent, "1.02");
			_highlightScaleEdit.TextChanged += OnParamChanged;
		}

		private void BuildScenarioTab(VBoxContainer parent)
		{
			AddLabel(parent, "tools/interactive_editor/label_cursor");
			var cursorSelect = new OptionButton();
			cursorSelect.AddItem(Tr("tools/interactive_editor/cursor_none"));
			cursorSelect.AddItem("PointingHand");
			cursorSelect.ItemSelected += idx =>
			{
				_controller.Cursor = idx == 0 ? "" : "PointingHand";
				OnParamChanged();
			};
			parent.AddChild(cursorSelect);

			AddLabel(parent, "tools/interactive_editor/label_onclick_scenario");
			_clickScenario = AddLineEdit(parent, "scenarios/my_click.scenario");
			_clickScenario.TextChanged += OnParamChanged;

			AddLabel(parent, "tools/interactive_editor/label_onclick_commands");
			_clickCommands = new TextEdit { CustomMinimumSize = new Vector2(0, 50) };
			_clickCommands.TextChanged += OnParamChanged;
			parent.AddChild(_clickCommands);

			AddLabel(parent, "tools/interactive_editor/label_hover_enter");
			_hoverEnter = AddLineEdit(parent, "set cursor_hint=click_me");
			_hoverEnter.TextChanged += OnParamChanged;

			AddLabel(parent, "tools/interactive_editor/label_hover_leave");
			_hoverLeave = AddLineEdit(parent, "set cursor_hint=");
			_hoverLeave.TextChanged += OnParamChanged;
		}

		private static string Tr(string key) => TranslationServer.Translate(key);

		private void AddLabel(Control parent, string locKey)
		{
			var label = new Label { Text = Tr(locKey) };
			label.AddThemeColorOverride("font_color", UiTokens.Muted);
			parent.AddChild(label);
		}

		private LineEdit AddLineEdit(Control parent, string placeholder)
		{
			var edit = new LineEdit { PlaceholderText = placeholder };
			parent.AddChild(edit);
			return edit;
		}

		private LineEdit AddLabeledLine(Control parent, string locKey)
		{
			var hbox = new HBoxContainer();
			var label = new Label { Text = Tr(locKey) };
			label.CustomMinimumSize = new Vector2(25, 0);
			hbox.AddChild(label);
			var edit = new LineEdit();
			edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			hbox.AddChild(edit);
			parent.AddChild(hbox);
			return edit;
		}

		private void LoadBackgroundList()
		{
			_bgList.Clear();
			var ids = ModResourceService.GetBackgroundIds();
			foreach (var id in ids)
				_bgList.AddItem(id);
		}

		private void OnBgSelected(long index)
		{
			var id = _bgList.GetItemText((int)index);
			_currentBgId = id;
			var tex = ModResourceService.LoadBackgroundTexture(id);
			if (tex != null)
			{
				_preview.Texture = tex;
			}
			OnParamChanged();
		}

		private void OnHitTypeChanged(long index)
		{
			_controller.HitType = _hitTypeSelect.GetItemText((int)index);
			_rectParams.Visible = _controller.HitType == "rect";
			_circleParams.Visible = _controller.HitType == "circle";
			_polyParams.Visible = _controller.HitType == "polygon";
			OnParamChanged();
		}

		private void OnAddVertex()
		{
			_controller.AddPolygonVertex();
			OnParamChanged();
		}

		private void OnParamChanged()
		{
			if (_suppressingSidebarEvents)
			{
				GameLogger.Log("INTERACTIVE_EDITOR", "OnParamChanged suppressed");
				return;
			}

			GameLogger.Log("INTERACTIVE_EDITOR", $"OnParamChanged hitType={_controller.HitType} bgSize=({_previewContainer.Size.X:F0}x{_previewContainer.Size.Y:F0})");
			ReadSidebarToController();
			RebuildHandles();
			UpdateColliderOverlay();
			RefreshJson();
			SetDirty();
			UpdateVertexList();
		}

		private void OnDragUpdate()
		{
			UpdateNonDraggedHandles();
			UpdateColliderOverlay();
			RefreshJson();
			SetDirty();
		}

		private void ReadSidebarToController()
		{
			_controller.Id = string.IsNullOrWhiteSpace(_idEdit.Text) ? "new_interactive" : _idEdit.Text.Trim();
			_controller.Texture = _textureEdit.Text.Trim();
			_controller.Layer = _layerSelect.GetItemText(_layerSelect.Selected);

			if (_controller.HitType == "rect")
			{
				float.TryParse(_rectX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v);
				_controller.RectX = v;
				float.TryParse(_rectY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); _controller.RectY = v;
				float.TryParse(_rectW.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); _controller.RectW = v;
				float.TryParse(_rectH.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); _controller.RectH = v;
			}
			else if (_controller.HitType == "circle")
			{
				float.TryParse(_circleX.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v);
				_controller.CircleX = v;
				float.TryParse(_circleY.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); _controller.CircleY = v;
				float.TryParse(_circleR.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); _controller.CircleRadius = v;
			}

			_controller.HighlightColorHex = "#" + _highlightColorBtn.Color.ToHtml(false);
			float.TryParse(_highlightScaleEdit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale);
			_controller.HighlightScale = scale;

			_controller.OnClickScenario = _clickScenario.Text.Trim();
			_controller.OnClickCommands = _clickCommands.Text.Trim();
			_controller.OnHoverEnter = _hoverEnter.Text.Trim();
			_controller.OnHoverLeave = _hoverLeave.Text.Trim();
		}

		private Rect2 GetBackgroundRect()
		{
			var cw = _previewContainer.Size.X;
			var ch = _previewContainer.Size.Y;
			if (cw <= 0 || ch <= 0) return new Rect2();

			var texSize = new Vector2(1920, 1024);
			var tex = _preview.Texture;
			if (tex != null)
			{
				var ts = tex.GetSize();
				if (ts.X > 0 && ts.Y > 0) texSize = ts;
			}

			var scale = Mathf.Min(cw / texSize.X, ch / texSize.Y);
			var rw = texSize.X * scale;
			var rh = texSize.Y * scale;
			var ox = (cw - rw) / 2f;
			var oy = (ch - rh) / 2f;
			return new Rect2(ox, oy, rw, rh);
		}

		private Vector2 NormToPixel(Vector2 norm)
		{
			var bg = GetBackgroundRect();
			return bg.Position + norm * bg.Size;
		}

		private Vector2 PixelToNorm(Vector2 pixel)
		{
			var bg = GetBackgroundRect();
			if (bg.Size.X <= 0 || bg.Size.Y <= 0) return Vector2.Zero;
			return new Vector2(
				(pixel.X - bg.Position.X) / bg.Size.X,
				(pixel.Y - bg.Position.Y) / bg.Size.Y);
		}

		private void WriteControllerToSidebar()
		{
			_suppressingSidebarEvents = true;
			try
			{
				_idEdit.Text = _controller.Id;
				_textureEdit.Text = _controller.Texture;

				for (int i = 0; i < _layerSelect.ItemCount; i++)
					if (_layerSelect.GetItemText(i) == _controller.Layer)
						_layerSelect.Select(i);

				for (int i = 0; i < _hitTypeSelect.ItemCount; i++)
					if (_hitTypeSelect.GetItemText(i) == _controller.HitType)
						_hitTypeSelect.Select(i);

				_rectX.Text = _controller.RectX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				_rectY.Text = _controller.RectY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				_rectW.Text = _controller.RectW.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				_rectH.Text = _controller.RectH.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

				_circleX.Text = _controller.CircleX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				_circleY.Text = _controller.CircleY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				_circleR.Text = _controller.CircleRadius.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

				_rectParams.Visible = _controller.HitType == "rect";
				_circleParams.Visible = _controller.HitType == "circle";
				_polyParams.Visible = _controller.HitType == "polygon";

				_highlightColorBtn.Color = new Color(_controller.HighlightColorHex);
				_highlightScaleEdit.Text = _controller.HighlightScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

				_clickScenario.Text = _controller.OnClickScenario;
				_clickCommands.Text = _controller.OnClickCommands;
				_hoverEnter.Text = _controller.OnHoverEnter;
				_hoverLeave.Text = _controller.OnHoverLeave;
			}
			finally
			{
				_suppressingSidebarEvents = false;
			}
		}

		private void RebuildHandles()
		{
			foreach (var h in _handles)
				h.QueueFree();
			_handles.Clear();

			var bg = GetBackgroundRect();
			if (bg.Size.X <= 0 || bg.Size.Y <= 0)
			{
				GameLogger.Log("INTERACTIVE_EDITOR", "RebuildHandles skipped: bg size <= 0");
				return;
			}

			GameLogger.Log("INTERACTIVE_EDITOR", $"RebuildHandles hitType={_controller.HitType} bg=({bg.Position.X:F0},{bg.Position.Y:F0} {bg.Size.X:F0}x{bg.Size.Y:F0})");

			var globalTopLeft = _previewContainer.GlobalPosition;

			switch (_controller.HitType)
			{
				case "rect":
				{
					var topLeft = NormToPixel(new Vector2(_controller.RectX, _controller.RectY));
					var bottomRight = NormToPixel(new Vector2(
						_controller.RectX + _controller.RectW,
						_controller.RectY + _controller.RectH));

				AddHandleAt(globalTopLeft + topLeft, _rectColor, 0, pos =>
				{
					var norm = PixelToNorm(pos - globalTopLeft);
					_controller.RectX = Mathf.Clamp(norm.X, 0f, 1f);
					_controller.RectY = Mathf.Clamp(norm.Y, 0f, 1f);
					_draggingHandleIndex = 0;
					OnDragUpdate();
				});
				AddHandleAt(globalTopLeft + bottomRight, _rectColor, 1, pos =>
				{
					var norm = PixelToNorm(pos - globalTopLeft);
					_controller.RectW = Mathf.Max(0.01f, norm.X - _controller.RectX);
					_controller.RectH = Mathf.Max(0.01f, norm.Y - _controller.RectY);
					_draggingHandleIndex = 1;
					OnDragUpdate();
				});
					break;
				}
				case "circle":
				{
					var refDim = Mathf.Min(bg.Size.X, bg.Size.Y);
					var center = NormToPixel(new Vector2(_controller.CircleX, _controller.CircleY));
					var radiusPx = _controller.CircleRadius * refDim;

				AddHandleAt(globalTopLeft + center, _circleColor, 0, pos =>
				{
					var norm = PixelToNorm(pos - globalTopLeft);
					_controller.CircleX = Mathf.Clamp(norm.X, 0f, 1f);
					_controller.CircleY = Mathf.Clamp(norm.Y, 0f, 1f);
					_draggingHandleIndex = 0;
					OnDragUpdate();
				});
				AddHandleAt(globalTopLeft + new Vector2(center.X + radiusPx, center.Y), _circleColor, 1, pos =>
				{
					var local = pos - globalTopLeft;
					var centerPx = NormToPixel(new Vector2(_controller.CircleX, _controller.CircleY));
					var dist = (local - centerPx).Length();
					_controller.CircleRadius = Mathf.Max(0.005f, dist / refDim);
					_draggingHandleIndex = 1;
					OnDragUpdate();
				});
					break;
				}
				case "polygon":
				{
					for (int i = 0; i < _controller.PolygonVertices.Count; i++)
					{
						var idx = i;
						var v = _controller.PolygonVertices[i];
				AddHandleAt(globalTopLeft + NormToPixel(v), _vertexColor, i, pos =>
					{
						var norm = PixelToNorm(pos - globalTopLeft);
						_controller.SetPolygonVertex(idx, new Vector2(
							Mathf.Clamp(norm.X, 0f, 1f),
							Mathf.Clamp(norm.Y, 0f, 1f)));
						_draggingHandleIndex = idx;
						OnDragUpdate();
					});
					}
					break;
				}
			}
		}

		private void UpdateNonDraggedHandles()
		{
			var bg = GetBackgroundRect();
			if (bg.Size.X <= 0 || bg.Size.Y <= 0) return;

			var globalTopLeft = _previewContainer.GlobalPosition;

			for (int i = 0; i < _handles.Count; i++)
			{
				if (i == _draggingHandleIndex) continue;
				var pos = GetHandleNormPosition(i);
				var pixelPos = globalTopLeft + NormToPixel(pos);
				var h = _handles[i];
				if (h != null && !h.IsQueuedForDeletion())
					h.GlobalPosition = pixelPos - h.Size / 2;
			}
		}

		private Vector2 GetHandleNormPosition(int index)
		{
			switch (_controller.HitType)
			{
				case "rect":
					if (index == 0)
						return new Vector2(_controller.RectX, _controller.RectY);
					return new Vector2(_controller.RectX + _controller.RectW, _controller.RectY + _controller.RectH);
				case "circle":
					if (index == 0)
						return new Vector2(_controller.CircleX, _controller.CircleY);
					return new Vector2(_controller.CircleX + _controller.CircleRadius, _controller.CircleY);
				case "polygon":
					if (index >= 0 && index < _controller.PolygonVertices.Count)
						return _controller.PolygonVertices[index];
					return Vector2.Zero;
				default:
					return Vector2.Zero;
			}
		}

		private void UpdateColliderOverlay()
		{
			var bg = GetBackgroundRect();
			if (bg.Size.X <= 0 || bg.Size.Y <= 0)
			{
				_colliderOverlay.Visible = false;
				return;
			}

			_colliderOverlay.Visible = true;
			_colliderOverlay.Position = bg.Position;
			_colliderOverlay.Size = bg.Size;
			_colliderOverlay.QueueRedraw();
		}

		private void DrawColliderShape()
		{
			var size = _colliderOverlay.Size;
			if (size.X <= 0 || size.Y <= 0) return;

			switch (_controller.HitType)
			{
				case "rect":
				{
					var pos = NormToPixelLocal(new Vector2(_controller.RectX, _controller.RectY));
					var rectSize = new Vector2(_controller.RectW * size.X, _controller.RectH * size.Y);
					_colliderOverlay.DrawRect(new Rect2(pos, rectSize), _colliderOverlayColor);
					break;
				}
				case "circle":
				{
					var rPx = _controller.CircleRadius * Mathf.Min(size.X, size.Y);
					var center = NormToPixelLocal(new Vector2(_controller.CircleX, _controller.CircleY));
					_colliderOverlay.DrawCircle(center, rPx, _colliderCircleOverlayColor);
					break;
				}
				case "polygon":
				{
				if (_controller.PolygonVertices.Count >= 3)
				{
					var points = new Vector2[_controller.PolygonVertices.Count];
					for (int i = 0; i < _controller.PolygonVertices.Count; i++)
						points[i] = NormToPixelLocal(_controller.PolygonVertices[i]);

					if (PolygonArea(points) > 0.0001f)
						_colliderOverlay.DrawColoredPolygon(points, _colliderPolyOverlayColor);
					_colliderOverlay.DrawPolyline(points, _colliderPolyOutlineColor);
				}
					break;
				}
			}
		}

	private Vector2 NormToPixelLocal(Vector2 norm)
	{
		return norm * _colliderOverlay.Size;
	}

	private static float PolygonArea(Vector2[] points)
	{
		float area = 0;
		for (int i = 0; i < points.Length; i++)
		{
			var j = (i + 1) % points.Length;
			area += points[i].X * points[j].Y;
			area -= points[j].X * points[i].Y;
		}
		return Mathf.Abs(area) * 0.5f;
	}

		private void AddHandleAt(Vector2 globalPos, Color color, int index, Action<Vector2> onDrag)
		{
			var handle = new InteractiveEditorHandle(color, index);
			handle.DragUpdate = center =>
			{
				onDrag(center);
			};
			handle.Clicked = () =>
			{
				SelectHandle(index);
			};
		handle.DragEnd = () =>
		{
			GameLogger.Log("INTERACTIVE_EDITOR", $"DragEnd idx={index} syncing sidebar");
			_draggingHandleIndex = -1;
			_suppressingSidebarEvents = true;
			WriteControllerToSidebar();
			_suppressingSidebarEvents = false;
			UpdateVertexList();
		};
			handle.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
			_previewContainer.AddChild(handle);
			handle.GlobalPosition = globalPos - handle.Size / 2;
			_handles.Add(handle);
		}

		private void SelectHandle(int index)
		{
			if (_controller.HitType == "polygon" && index >= 0 && index < _controller.PolygonVertices.Count)
			{
				var v = _controller.PolygonVertices[index];
				GD.Print($"Selected vertex {index}: ({v.X:F3}, {v.Y:F3})");
			}
		}

		private void UpdateVertexList()
		{
			for (int i = _vertexList.GetChildCount() - 1; i >= 0; i--)
				_vertexList.GetChild(i).QueueFree();

			if (_controller.HitType != "polygon") return;

			for (int i = 0; i < _controller.PolygonVertices.Count; i++)
			{
				var idx = i;
				var v = _controller.PolygonVertices[i];
				var row = new HBoxContainer();

				var xEdit = new LineEdit();
				xEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				xEdit.Text = v.X.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				xEdit.TextChanged += txt =>
				{
					if (float.TryParse(xEdit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
						_controller.SetPolygonVertex(idx, new Vector2(val, _controller.PolygonVertices[idx].Y));
					OnParamChanged();
				};
				row.AddChild(xEdit);

				var yEdit = new LineEdit();
				yEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				yEdit.Text = v.Y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
				yEdit.TextChanged += txt =>
				{
					if (float.TryParse(yEdit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
						_controller.SetPolygonVertex(idx, new Vector2(_controller.PolygonVertices[idx].X, val));
					OnParamChanged();
				};
				row.AddChild(yEdit);

				var delBtn = new Button { Text = "X" };
				delBtn.CustomMinimumSize = new Vector2(30, 0);
				delBtn.Pressed += () =>
				{
					_controller.RemovePolygonVertex(idx);
					OnParamChanged();
				};
				row.AddChild(delBtn);

				_vertexList.AddChild(row);
			}
		}

		private void RefreshJson()
		{
			_jsonEdit.Text = _controller.ToJson();
		}

		private void OnLoadJson()
		{
			var dialog = new FileDialog();
			dialog.FileMode = FileDialog.FileModeEnum.OpenFile;
			dialog.AddFilter("*.json", "JSON Files");
			dialog.AddFilter("*.interactive", "Interactive Files");
			dialog.FileSelected += path =>
			{
				var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					var text = file.GetAsText();
					file.Close();
					if (_controller.FromJson(text))
					{
						WriteControllerToSidebar();
						UpdateVertexList();
						RebuildHandles();
						UpdateColliderOverlay();
						RefreshJson();
						MarkClean();
					}
					else
					{
						GD.PrintErr("Failed to parse JSON");
					}
				}
				dialog.QueueFree();
			};
			dialog.Canceled += () => dialog.QueueFree();
			AddChild(dialog);
			dialog.PopupCentered(new Vector2I(800, 600));
		}

		private void OnImportFromMod()
		{
			var ids = Interactives.Collect();
			if (ids.Count == 0)
			{
				GD.Print("No interactive definitions found in mods");
				return;
			}

			var dialog = new AcceptDialog { Title = Tr("tools/interactive_editor/dlg_select_def") };
			dialog.Size = new Vector2I(400, 300);

			var list = new ItemList();
			list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
			foreach (var kv in ids)
				list.AddItem(kv.Key);
			list.ItemActivated += idx =>
			{
				var id = list.GetItemText((int)idx);
				var def = InteractiveFactory.Instance.Get(id);
				if (def != null)
				{
					_controller.FromDefinition(def);
					WriteControllerToSidebar();
					UpdateVertexList();
					RebuildHandles();
					UpdateColliderOverlay();
					RefreshJson();
					MarkClean();
				}
				dialog.QueueFree();
			};
			dialog.AddChild(list);
			AddChild(dialog);
			dialog.PopupCentered();
		}

		private void OnSave()
		{
			var dialog = new FileDialog();
			dialog.FileMode = FileDialog.FileModeEnum.SaveFile;
			dialog.AddFilter("*.json", "JSON Files");
			dialog.FileSelected += path =>
			{
				var json = _controller.ToJson();
				var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
				if (file != null)
				{
					file.StoreString(json);
					file.Close();
					MarkClean();
					GD.Print($"Saved to {path}");
				}
				dialog.QueueFree();
			};
			dialog.Canceled += () => dialog.QueueFree();
			AddChild(dialog);
			dialog.PopupCentered(new Vector2I(800, 600));
		}

		private void OnParamChanged(string _ = null) => OnParamChanged();

		protected override void Cleanup()
		{
			foreach (var h in _handles)
				h.QueueFree();
			_handles.Clear();
		}
	}
}

