using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views.Tabs;

// Adds edit-mode affordances to dashboard tiles (dashed outline, grip, cursors,
// glass pane) plus two gestures armed in Customize mode. Dragging the body shows
// a bitmap ghost and a placeholder that reflows the WrapPanel live; on drop the
// new slot is committed. Dragging the grip resizes the tile with a magnetic snap
// to the S/M/L widths and a floating size badge. Both stay within a single panel
// (stat tiles vs. content widgets).
internal sealed class DashboardArrangeBehavior
{
	private const double DRAG_THRESHOLD = 5;

	// Must stay below half the smallest gap between adjacent size widths so the
	// snap zones never overlap (smallest gap today is stat tiles' 50px XS->S).
	private const double SNAP_THRESHOLD = 20;
	private const double TILE_CORNER_RADIUS = 8;
	private const double OUTLINE_THICKNESS = 1.5;

	private enum GestureMode
	{
		Reorder,
		Resize
	}

	private readonly Dictionary<DashboardWidgetId, TileWrapper> tiles = new();
	private bool editMode;

	public MainWindowViewModel? Vm { get; set; }

	// Maps (widget, size) -> exact tile width, mirroring the view's layout rule so
	// resize can snap to the same S/M/L widths the rebuild applies.
	public Func<DashboardWidgetId, DashboardWidgetSize, double>? WidthFor { get; set; }

	// Invoked to rebuild the panels when a gesture ends without a committed change
	// (cancelled, or dropped back onto the same slot), restoring a clean layout.
	public Action? RebuildPanels { get; set; }

	// True while a drop is reordering the panel in place; lets the view skip the
	// full panel rebuild its CollectionChanged handler would otherwise run.
	public bool IsCommitting { get; private set; }

	private TileWrapper? dragTile;
	private DashboardWidgetViewModel? dragWidget;
	private Panel? dragPanel;
	private Border? placeholder;
	private Image? ghost;
	private AdornerLayer? adorner;
	private TopLevel? topLevel;
	private IPointer? capturedPointer;
	private Point grabOffset;
	private Point pressPoint;
	private bool pressed;
	private bool dragging;

	private static readonly DashboardWidgetSize[] SizeOrder =
	[
			DashboardWidgetSize.XSmall, DashboardWidgetSize.Small, DashboardWidgetSize.Medium,
			DashboardWidgetSize.Large, DashboardWidgetSize.XLarge
	];

	private GestureMode mode;
	private bool resizing;
	private double resizeStartWidth;
	private double lastAppliedWidth;
	private double[] sizeWidths = [];
	private Border? sizeBadge;
	private TextBlock? sizeBadgeText;

	public Control Wrap(DashboardWidgetViewModel widget, Control content)
	{
		if(!this.tiles.TryGetValue(widget.Id, out var tile))
		{
			tile = new TileWrapper(this, content);
			this.tiles[widget.Id] = tile;
		}

		tile.Widget = widget;
		tile.SetEditMode(this.editMode);
		return tile.Root;
	}

	public void SetEditMode(bool value)
	{
		if(this.editMode == value)
		{
			return;
		}

		this.editMode = value;
		foreach(var tile in this.tiles.Values)
		{
			tile.SetEditMode(value);
		}
	}

	private void OnTilePointerPressed(TileWrapper tile, PointerPressedEventArgs e)
	{
		if(!this.editMode||this.Vm == null||this.pressed)
		{
			return;
		}

		if(!e.GetCurrentPoint(tile.Overlay).Properties.IsLeftButtonPressed)
		{
			return;
		}

		if(tile.Root.Parent is not Panel panel||tile.Widget == null)
		{
			return;
		}

		this.mode = OverGrip(e, tile.Grip)?GestureMode.Resize:GestureMode.Reorder;
		this.dragTile = tile;
		this.dragPanel = panel;
		this.dragWidget = tile.Widget;
		this.pressed = true;
		this.dragging = false;
		this.resizing = false;
		this.pressPoint = e.GetPosition(null);
		this.grabOffset = e.GetPosition(tile.Content);
		this.capturedPointer = e.Pointer;

		e.Pointer.Capture(panel);
		panel.PointerMoved += this.OnPanelPointerMoved;
		panel.PointerReleased += this.OnPanelPointerReleased;
		panel.PointerCaptureLost += this.OnPanelCaptureLost;
		e.Handled = true;
	}

	private void OnPanelPointerMoved(object? sender, PointerEventArgs e)
	{
		if(!this.pressed)
		{
			return;
		}

		if(this.mode == GestureMode.Resize)
		{
			if(!this.resizing)
			{
				this.StartResize();
			}

			this.UpdateResize(e);
			return;
		}

		if(!this.dragging)
		{
			var p = e.GetPosition(null);
			if(Math.Abs(p.X - this.pressPoint.X) < DRAG_THRESHOLD&&Math.Abs(p.Y - this.pressPoint.Y) < DRAG_THRESHOLD)
			{
				return;
			}

			this.StartDrag();
		}

		this.UpdateGhost(e);
		this.UpdatePlaceholder(e);
	}

	private void OnPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		this.FinishGesture(commit: true);
	}

	// Capture can be stolen (e.g. the window loses focus mid-drag); without this
	// the press state would stay latched and block every future drag.
	private void OnPanelCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		this.FinishGesture(commit: false);
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if(e.Key == Key.Escape&&(this.dragging||this.resizing))
		{
			this.FinishGesture(commit: false);
		}
	}

	private void StartDrag()
	{
		if(this.dragTile == null||this.dragPanel == null)
		{
			return;
		}

		this.dragging = true;
		var content = this.dragTile.Content;
		var w = content.Bounds.Width;
		var h = content.Bounds.Height;

		this.adorner = AdornerLayer.GetAdornerLayer(this.dragPanel);
		if(this.adorner != null)
		{
			var bmp = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)w), Math.Max(1, (int)h)));
			bmp.Render(content);
			this.ghost = new Image { Source = bmp, Width = w, Height = h, Opacity = 0.65, IsHitTestVisible = false };
			this.adorner.Children.Add(this.ghost);
		}

		this.placeholder = new Border
		                   {
				                   Width = w,
				                   Height = h,
				                   Margin = content.Margin,
				                   CornerRadius = new CornerRadius(TILE_CORNER_RADIUS),
				                   BorderThickness = new Thickness(OUTLINE_THICKNESS)
		                   };
		this.placeholder[!Border.BackgroundProperty] = new DynamicResourceExtension("AppAccentCyanWash");
		this.placeholder[!Border.BorderBrushProperty] = new DynamicResourceExtension("AppAccentCyan");

		var idx = this.dragPanel.Children.IndexOf(this.dragTile.Root);
		this.dragPanel.Children[idx] = this.placeholder;

		this.topLevel = TopLevel.GetTopLevel(this.dragPanel);
		if(this.topLevel != null)
		{
			this.topLevel.KeyDown += this.OnKeyDown;
		}
	}

	private void UpdateGhost(PointerEventArgs e)
	{
		if(this.ghost == null||this.adorner == null)
		{
			return;
		}

		var p = e.GetPosition(this.adorner);
		Canvas.SetLeft(this.ghost, p.X - this.grabOffset.X);
		Canvas.SetTop(this.ghost, p.Y - this.grabOffset.Y);
	}

	private void UpdatePlaceholder(PointerEventArgs e)
	{
		if(this.placeholder == null||this.dragPanel == null)
		{
			return;
		}

		var pos = e.GetPosition(this.dragPanel);

		// Hold while the cursor is still over the placeholder's own slot. Moving it
		// reflows the panel, which shifts every tile's bounds; without this deadzone
		// the next hit-test flips back and the target oscillates.
		if(this.placeholder.Bounds.Contains(pos))
		{
			return;
		}

		var current = this.dragPanel.Children.IndexOf(this.placeholder);
		var target = current;
		for(var i = 0; i < this.dragPanel.Children.Count; i++)
		{
			var child = this.dragPanel.Children[i];
			if(child == this.placeholder||!child.IsVisible||!child.Bounds.Contains(pos))
			{
				continue;
			}

			// Removing the placeholder shifts later indices down one, so subtract
			// one when it currently sits before the tile we're inserting next to.
			var anchor = pos.X < child.Bounds.Center.X ? i : i + 1;
			target = current < anchor ? anchor - 1 : anchor;
			break;
		}

		target = Math.Clamp(target, 0, this.dragPanel.Children.Count - 1);
		if(current != target)
		{
			this.dragPanel.Children.Move(current, target);
		}
	}

	private void StartResize()
	{
		if(this.dragTile == null||this.dragPanel == null||this.dragWidget == null||this.WidthFor == null)
		{
			return;
		}

		this.resizing = true;
		this.resizeStartWidth = this.dragTile.Content.Bounds.Width;
		this.lastAppliedWidth = this.resizeStartWidth;
		var id = this.dragWidget.Id;
		this.sizeWidths = new double[SizeOrder.Length];
		for(var i = 0; i < SizeOrder.Length; i++)
		{
			this.sizeWidths[i] = this.WidthFor(id, SizeOrder[i]);
		}

		this.adorner = AdornerLayer.GetAdornerLayer(this.dragPanel);
		if(this.adorner != null)
		{
			this.sizeBadgeText = new TextBlock
			                     {
					                     Foreground = Brushes.Black,
					                     FontSize = 11,
					                     FontWeight = FontWeight.Bold,
					                     HorizontalAlignment = HorizontalAlignment.Center,
					                     VerticalAlignment = VerticalAlignment.Center
			                     };
			this.sizeBadge = new Border
			                 {
					                 MinWidth = 22,
					                 Height = 20,
					                 Padding = new Thickness(6, 0),
					                 CornerRadius = new CornerRadius(TILE_CORNER_RADIUS),
					                 IsHitTestVisible = false,
					                 Child = this.sizeBadgeText
			                 };
			this.sizeBadge[!Border.BackgroundProperty] = new DynamicResourceExtension("AppAccentCyan");
			this.sizeBadgeText.Text = SizeLabel(this.NearestSize(this.resizeStartWidth));
			this.adorner.Children.Add(this.sizeBadge);
		}

		this.topLevel = TopLevel.GetTopLevel(this.dragPanel);
		if(this.topLevel != null)
		{
			this.topLevel.KeyDown += this.OnKeyDown;
		}
	}

	private void UpdateResize(PointerEventArgs e)
	{
		if(this.dragTile == null)
		{
			return;
		}

		var raw = this.resizeStartWidth + (e.GetPosition(null).X - this.pressPoint.X);
		var snapped = this.SnapWidth(Math.Clamp(raw, this.sizeWidths[0], this.sizeWidths[^1]));

		// Only touch the width (and badge letter) when it actually changes, so the
		// WrapPanel isn't relaid out on every sub-pixel pointer move.
		if(Math.Abs(snapped - this.lastAppliedWidth) > 0.5)
		{
			this.lastAppliedWidth = snapped;
			this.dragTile.Content.Width = snapped;
			if(this.sizeBadgeText != null)
			{
				this.sizeBadgeText.Text = SizeLabel(this.NearestSize(snapped));
			}
		}

		if(this.sizeBadge != null&&this.adorner != null)
		{
			var p = e.GetPosition(this.adorner);
			Canvas.SetLeft(this.sizeBadge, p.X + 14);
			Canvas.SetTop(this.sizeBadge, p.Y + 14);
		}
	}

	private double SnapWidth(double width)
	{
		foreach(var w in this.sizeWidths)
		{
			if(Math.Abs(width - w) <= SNAP_THRESHOLD)
			{
				return w;
			}
		}

		return width;
	}

	private DashboardWidgetSize NearestSize(double width)
	{
		var best = 0;
		var bestDistance = double.MaxValue;
		for(var i = 0; i < this.sizeWidths.Length; i++)
		{
			var distance = Math.Abs(width - this.sizeWidths[i]);
			if(distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}

		return SizeOrder[best];
	}

	private void CommitResize()
	{
		if(this.dragTile == null||this.dragWidget == null||this.Vm == null||this.WidthFor == null)
		{
			return;
		}

		var size = this.NearestSize(this.dragTile.Content.Width);

		// The tile already holds the exact snapped width, so persist the size with
		// the rebuild suppressed instead of tearing down and re-adding every tile.
		this.dragTile.Content.Width = this.WidthFor(this.dragWidget.Id, size);
		this.IsCommitting = true;
		this.Vm.SetDashboardWidgetSize(this.dragWidget, size);
		this.IsCommitting = false;
	}

	private static string SizeLabel(DashboardWidgetSize size)
	{
		return size switch
		       {
				       DashboardWidgetSize.XSmall => "XS",
				       DashboardWidgetSize.Small => "S",
				       DashboardWidgetSize.Medium => "M",
				       DashboardWidgetSize.Large => "L",
				       _ => "XL"
		       };
	}

	private void FinishGesture(bool commit)
	{
		if(!this.pressed)
		{
			return;
		}

		if(this.dragPanel != null)
		{
			this.dragPanel.PointerMoved -= this.OnPanelPointerMoved;
			this.dragPanel.PointerReleased -= this.OnPanelPointerReleased;
			this.dragPanel.PointerCaptureLost -= this.OnPanelCaptureLost;
		}

		this.capturedPointer?.Capture(null);

		if(this.topLevel != null)
		{
			this.topLevel.KeyDown -= this.OnKeyDown;
			this.topLevel = null;
		}

		if(this.ghost != null&&this.adorner != null)
		{
			this.adorner.Children.Remove(this.ghost);
		}

		if(this.sizeBadge != null&&this.adorner != null)
		{
			this.adorner.Children.Remove(this.sizeBadge);
		}

		// RenderTargetBitmap wraps an unmanaged surface; drop it explicitly.
		(this.ghost?.Source as IDisposable)?.Dispose();

		if(this.mode == GestureMode.Resize)
		{
			if(this.resizing&&commit)
			{
				this.CommitResize();
			}
			else if(this.resizing)
			{
				this.RebuildPanels?.Invoke();
			}
		}
		else if(this.dragging&&commit)
		{
			this.CommitReorder();
		}
		else if(this.dragging)
		{
			this.RebuildPanels?.Invoke();
		}

		this.ghost = null;
		this.adorner = null;
		this.placeholder = null;
		this.sizeBadge = null;
		this.sizeBadgeText = null;
		this.dragPanel = null;
		this.dragWidget = null;
		this.dragTile = null;
		this.capturedPointer = null;
		this.dragging = false;
		this.resizing = false;
		this.pressed = false;
	}

	private void CommitReorder()
	{
		if(this.placeholder == null||this.dragPanel == null||this.dragWidget == null||this.dragTile == null||this.Vm == null)
		{
			return;
		}

		var siblings = this.dragPanel.Children
		                   .Where(c => c != this.placeholder)
		                   .Select(this.WidgetForRoot)
		                   .Where(w => w != null)
		                   .Select(w => w!)
		                   .ToList();

		var widgets = this.Vm.DashboardWidgets;
		var from = widgets.IndexOf(this.dragWidget);
		var rank = this.dragPanel.Children.IndexOf(this.placeholder);

		// Drop the real tile into the placeholder's slot: the panel is already in
		// its final visual order, so committing the model move below can skip the
		// full panel rebuild rather than tearing down and re-adding every tile.
		this.dragPanel.Children[rank] = this.dragTile.Root;

		if(siblings.Count == 0)
		{
			return;
		}

		// Removing the dragged item shifts every later index down by one, so when
		// the anchor sits below the source we subtract one to land just before it.
		int target;
		if(rank < siblings.Count)
		{
			var before = widgets.IndexOf(siblings[rank]);
			target = from < before ? before - 1 : before;
		}
		else
		{
			var last = widgets.IndexOf(siblings[^1]);
			target = from < last ? last : last + 1;
		}

		target = Math.Clamp(target, 0, widgets.Count - 1);
		if(target == from)
		{
			return;
		}

		this.IsCommitting = true;
		this.Vm.MoveDashboardWidget(this.dragWidget, target);
		this.IsCommitting = false;
	}

	private DashboardWidgetViewModel? WidgetForRoot(Control root)
	{
		foreach(var tile in this.tiles.Values)
		{
			if(tile.Root == root)
			{
				return tile.Widget;
			}
		}

		return null;
	}

	private static bool OverGrip(PointerEventArgs e, Visual grip)
	{
		var p = e.GetPosition(grip);
		return p.X >= 0&&p.Y >= 0&&p.X <= grip.Bounds.Width&&p.Y <= grip.Bounds.Height;
	}

	private sealed class TileWrapper
	{
		private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);
		private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

		public Control Content { get; }

		public Control Overlay { get; }

		public Border Grip { get; }

		public Grid Root { get; }

		public DashboardWidgetViewModel? Widget { get; set; }

		public TileWrapper(DashboardArrangeBehavior owner, Control content)
		{
			this.Content = content;

			var outline = new Rectangle
			              {
					              RadiusX = TILE_CORNER_RADIUS,
					              RadiusY = TILE_CORNER_RADIUS,
					              StrokeThickness = OUTLINE_THICKNESS,
					              StrokeDashArray = new AvaloniaList<double> { 4, 3 }
			              };
			outline[!Shape.StrokeProperty] = new DynamicResourceExtension("AppAccentCyan");
			outline[!Shape.FillProperty] = new DynamicResourceExtension("AppAccentCyanWash");

			this.Grip = new Border
			            {
					            Width = 16,
					            Height = 16,
					            CornerRadius = new CornerRadius(8, 0, 8, 0),
					            HorizontalAlignment = HorizontalAlignment.Right,
					            VerticalAlignment = VerticalAlignment.Bottom,
					            Cursor = ResizeCursor,
					            Child = new TextBlock
					                    {
							                    Text = "◢",
							                    FontSize = 9,
							                    Foreground = Brushes.Black,
							                    HorizontalAlignment = HorizontalAlignment.Center,
							                    VerticalAlignment = VerticalAlignment.Center
					                    }
			            };
			this.Grip[!Border.BackgroundProperty] = new DynamicResourceExtension("AppAccentCyan");

			this.Overlay = new Grid
			               {
					               Margin = content.Margin,
					               Cursor = DragCursor,
					               Background = Brushes.Transparent,
					               Children = { outline, this.Grip }
			               };
			this.Overlay.PointerPressed += (_, e) => owner.OnTilePointerPressed(this, e);

			this.Root = new Grid { Children = { content, this.Overlay } };
		}

		public void SetEditMode(bool value)
		{
			this.Overlay.IsVisible = value;
			this.Overlay.IsHitTestVisible = value;
			this.Content.IsHitTestVisible = !value;
		}
	}
}
