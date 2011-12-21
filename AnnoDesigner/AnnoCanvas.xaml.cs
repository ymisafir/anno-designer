﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MessageBox = Microsoft.Windows.Controls.MessageBox;

namespace AnnoDesigner
{
    /// <summary>
    /// Interaction logic for AnnoCanvas.xaml
    /// </summary>
    public partial class AnnoCanvas
        : UserControl
    {
        #region Properties

        private const int GridStepMin = 8;
        private const int GridStepMax = 100;
        private const int GridStepDefault = 20;
        private int _gridStep = GridStepDefault;

        /// <summary>
        /// Gets or sets the width of the grid cells.
        /// Increasing the grid size results in zooming in and vice versa.
        /// </summary>
        public int GridSize
        {
            get
            {
                return _gridStep;
            }
            set
            {
                var tmp = value;
                if (tmp < GridStepMin)
                    tmp = GridStepMin;
                if (tmp > GridStepMax)
                    tmp = GridStepMax;
                if (_gridStep != tmp)
                {
                    InvalidateVisual();
                }
                _gridStep = tmp;
            }
        }

        private bool _renderGrid;

        /// <summary>
        /// Gets or sets a value indicating whether the grid should be rendered.
        /// </summary>
        public bool RenderGrid
        {
            get
            {
                return _renderGrid;
            }
            set
            {
                if (_renderGrid != value)
                {
                    InvalidateVisual();
                }
                _renderGrid = value;
            }
        }

        private bool _renderLabel;

        /// <summary>
        /// Gets or sets a value indicating whether the labels of objects should be rendered.
        /// </summary>
        public bool RenderLabel
        {
            get
            {
                return _renderLabel;
            }
            set
            {
                if (_renderLabel != value)
                {
                    InvalidateVisual();
                }
                _renderLabel = value;
            }
        }

        private bool _renderIcon;

        /// <summary>
        /// Gets or sets a value indicating whether the icons of objects should be rendered.
        /// </summary>
        public bool RenderIcon
        {
            get
            {
                return _renderIcon;
            }
            set
            {
                if (_renderIcon != value)
                {
                    InvalidateVisual();
                }
                _renderIcon = value;
            }
        }

        /// <summary>
        /// Event which is fired when the current object is changed from within the control, i.e. not by calling SetCurrentObject().
        /// </summary>
        public event Action<AnnoObject> OnCurrentObjectChange;
        private void FireOnCurrentObjectChange(AnnoObject obj)
        {
            if (OnCurrentObjectChange != null)
            {
                OnCurrentObjectChange(obj);
            }
        }

        /// <summary>
        /// Event which is fired when the status message should be changed.
        /// </summary>
        public event Action<string> OnShowStatusMessage;
        private void FireOnShowStatusMessage(string message)
        {
            if (OnShowStatusMessage != null)
            {
                OnShowStatusMessage(message);
            }
        }

        #endregion

        #region Privates and constructor

        private enum MouseMode
        {
            // used if not dragging
            Standard,
            // used to drag the selection rect
            SelectionRectStart,
            SelectionRect,
            // used to drag objects around
            DragSelectionStart,
            DragSingleStart,
            DragSelection,
            DragAllStart,
            DragAll
        }

        private MouseMode _currentMode;
        /// <summary>
        /// Indicates the current mouse mode.
        /// </summary>
        private MouseMode CurrentMode
        {
            get
            {
                return _currentMode;
            }
            set
            {
                _currentMode = value;
                FireOnShowStatusMessage("Mode: " + _currentMode);
            }
        }
        /// <summary>
        /// Indicates whether the mouse is within this control.
        /// </summary>
        private bool _mouseWithinControl;

        /// <summary>
        /// The current mouse position.
        /// </summary>
        private Point _mousePosition;

        /// <summary>
        /// The position where the mouse button was pressed.
        /// </summary>
        private Point _mouseDragStart;

        /// <summary>
        /// The rectangle used for selection.
        /// </summary>
        private Rect _selectionRect;
        
        /// <summary>
        /// List of all currently placed objects.
        /// </summary>
        private List<AnnoObject> _placedObjects;

        /// <summary>
        /// List of all currently selected objects.
        /// All of them must also be contained in the _placedObjects list.
        /// </summary>
        private readonly List<AnnoObject> _selectedObjects;

        /// <summary>
        /// Current object to be placed.
        /// </summary>
        private AnnoObject _currentObject;

        /// <summary>
        /// Last loaded file, i.e. the currently active file
        /// </summary>
        private string _loadedFile;

        // pens and brushes
        private readonly Pen _linePen;
        private readonly Pen _highlightPen;
        private readonly Pen _radiusPen;
        private readonly Pen _influencedPen;
        private readonly Brush _lightBrush;
        private readonly Brush _influencedBrush;

        public AnnoCanvas()
        {
            InitializeComponent();
            CurrentMode = MouseMode.Standard;
            _placedObjects = new List<AnnoObject>();
            _selectedObjects = new List<AnnoObject>();
            _linePen = new Pen(Brushes.Black, 1);
            _highlightPen = new Pen(Brushes.Yellow, 1);
            _radiusPen = new Pen(Brushes.Black, 1);
            _influencedPen = new Pen(Brushes.LawnGreen, 1);
            var color = Colors.LightYellow;
            color.A = 92;
            _lightBrush = new SolidColorBrush(color);
            color = Colors.LawnGreen;
            color.A = 92;
            _influencedBrush = new SolidColorBrush(color);
            Focusable = true;
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Renders the whole scene including grid, placed objects, current object, selection highlights, influence radii and selection rectangle.
        /// </summary>
        /// <param name="drawingContext">context used for rendering</param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            //var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            //var dpiFactor = 1 / m.M11;
            var dpiFactor = 1;
            _linePen.Thickness = dpiFactor * 1;
            _highlightPen.Thickness = dpiFactor * 2;
            _radiusPen.Thickness = dpiFactor * 2;
            _influencedPen.Thickness = dpiFactor * 2;

            // assure pixel perfect drawing
            var halfPenWidth = _linePen.Thickness / 2;
            var guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(halfPenWidth);
            guidelines.GuidelinesY.Add(halfPenWidth);
            drawingContext.PushGuidelineSet(guidelines);

            var width = RenderSize.Width;
            var height = RenderSize.Height;

            // draw background
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(new Point(), RenderSize));

            // draw grid
            if (RenderGrid)
            {
                for (var i = 0; i < width; i += _gridStep)
                {
                    drawingContext.DrawLine(_linePen, new Point(i, 0), new Point(i, height));
                }
                for (var i = 0; i < height; i += _gridStep)
                {
                    drawingContext.DrawLine(_linePen, new Point(0, i), new Point(width, i));
                }
            }

            // draw mouse grid position highlight
            //drawingContext.DrawRectangle(Brushes.LightYellow, linePen, new Rect(GridToScreen(ScreenToGrid(_mousePosition)), new Size(_gridStep, _gridStep)));

            // draw placed objects
            _placedObjects.ForEach(_ => RenderObject(drawingContext, _));
            _selectedObjects.ForEach(_ => RenderObjectInfluence(drawingContext, _));
            _selectedObjects.ForEach(_ => RenderObjectSelection(drawingContext, _));

            if (_currentObject == null)
            {
                // highlight object which is currently hovered
                var hoveredObj = GetObjectAt(_mousePosition);
                if (hoveredObj != null)
                {
                    drawingContext.DrawRectangle(null, _highlightPen, GetObjectScreenRect(hoveredObj));
                }
            }
            else
            {
                // draw current object
                if (_mouseWithinControl)
                {
                    MoveCurrentObjectToMouse();
                    // draw influence radius
                    RenderObjectInfluence(drawingContext, _currentObject);
                    // draw with transparency
                    _currentObject.Color.A = 128;
                    RenderObject(drawingContext, _currentObject);
                    _currentObject.Color.A = 255;
                }
            }
            // draw selection rect while dragging the mouse
            if (CurrentMode == MouseMode.SelectionRect)
            {
                drawingContext.DrawRectangle(_lightBrush, _highlightPen, _selectionRect);
            }
            // pop back guidlines set
            drawingContext.Pop();
        }

        /// <summary>
        /// Moves the current object to the mouse position.
        /// </summary>
        private void MoveCurrentObjectToMouse()
        {
            if (_currentObject == null)
            {
                return;
            }
            // determine grid position beneath mouse
            var pos = _mousePosition;
            var size = GridToScreen(_currentObject.Size);
            pos.X -= size.Width / 2;
            pos.Y -= size.Height / 2;
            _currentObject.Position = RoundScreenToGrid(pos);
        }

        /// <summary>
        /// Renders the given AnnoObject to the given DrawingContext.
        /// </summary>
        /// <param name="drawingContext">context used for rendering</param>
        /// <param name="obj">object to render</param>
        private void RenderObject(DrawingContext drawingContext, AnnoObject obj)
        {
            // draw object rectangle
            var objRect = GetObjectScreenRect(obj);
            drawingContext.DrawRectangle(new SolidColorBrush(obj.Color), _linePen, objRect);
            // draw object icon if it is at least 2x2 cells
            var iconRendered = false;
            if (_renderIcon && !string.IsNullOrEmpty(obj.Icon))
            {
                // draw icon 2x2 grid cells large
                var iconSize = obj.Size.Width < 2 && obj.Size.Height < 2
                    ? GridToScreen(new Size(1,1))
                    : GridToScreen(new Size(2,2));
                // center icon within the object
                var iconPos = objRect.TopLeft;
                iconPos.X += objRect.Width/2 - iconSize.Width/2;
                iconPos.Y += objRect.Height/2 - iconSize.Height/2;
                if (File.Exists(obj.Icon))
                {
                    drawingContext.DrawImage(new BitmapImage(new Uri(obj.Icon, UriKind.Relative)), new Rect(iconPos, iconSize));
                    iconRendered = true;
                }
                else
                {
                    FireOnShowStatusMessage(string.Format("Icon file missing ({0}).", obj.Icon));
                }
            }
            // draw object label
            if (_renderLabel)
            {
                var textPoint = objRect.TopLeft;
                var text = new FormattedText(obj.Label, Thread.CurrentThread.CurrentCulture, FlowDirection.LeftToRight,
                                             new Typeface("Verdana"), 12, Brushes.Black)
                {
                    MaxTextWidth = objRect.Width,
                    MaxTextHeight = objRect.Height
                };
                if (iconRendered)
                {
                    // place the text in the top left corner if a icon is present
                    text.TextAlignment = TextAlignment.Left;
                    textPoint.X += 3;
                    textPoint.Y += 2;
                }
                else
                {
                    // center the text if no icon is present
                    text.TextAlignment = TextAlignment.Center;
                    textPoint.Y += (objRect.Height - text.Height) / 2;
                }
                drawingContext.DrawText(text, textPoint);
            }
        }

        /// <summary>
        /// Renders a selection highlight on the specified object.
        /// </summary>
        /// <param name="drawingContext">context used for rendering</param>
        /// <param name="obj">object to render as selected</param>
        private void RenderObjectSelection(DrawingContext drawingContext, AnnoObject obj)
        {
            // draw object rectangle
            var objRect = GetObjectScreenRect(obj);
            drawingContext.DrawRectangle(null, _highlightPen, objRect);
        }

        /// <summary>
        /// Renders the influence radius of the given object and highlights other objects within range.
        /// </summary>
        /// <param name="drawingContext">context used for rendering</param>
        /// <param name="obj">object which's influence is rendered</param>
        private void RenderObjectInfluence(DrawingContext drawingContext, AnnoObject obj)
        {
            if (obj.Radius >= 0.5)
            {
                // highlight buildings within influence
                var radius = GridToScreen(obj.Radius);
                var circle = new EllipseGeometry(GetCenterPoint(GetObjectScreenRect(obj)), radius, radius);
                foreach (var o in _placedObjects)
                {
                    var oRect = GetObjectScreenRect(o);
                    var distance = GetCenterPoint(oRect);
                    distance.X -= circle.Center.X;
                    distance.Y -= circle.Center.Y;
                    // check if the center is within the influence circle
                    if (distance.X*distance.X + distance.Y*distance.Y <= radius*radius)
                    {
                        drawingContext.DrawRectangle(_influencedBrush, _influencedPen, oRect);
                    }
                }
                // draw circle
                drawingContext.DrawGeometry(_lightBrush, _radiusPen, circle);
            }
        }

        #endregion

        #region Coordinate and rectangle conversions

        /// <summary>
        /// Convert a screen coordinate to a grid coordinate by determining in which grid cell the point is contained.
        /// </summary>
        /// <param name="screenPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point ScreenToGrid(Point screenPoint)
        {
            return new Point(Math.Floor(screenPoint.X / _gridStep), Math.Floor(screenPoint.Y / _gridStep));
        }

        /// <summary>
        /// Converts a screen coordinate to a grid coordinate by determining which grid cell is nearest.
        /// </summary>
        /// <param name="screenPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point RoundScreenToGrid(Point screenPoint)
        {
            return new Point(Math.Round(screenPoint.X / _gridStep), Math.Round(screenPoint.Y / _gridStep));
        }

        /// <summary>
        /// Converts a length given in (pixel-)units to a length given in grid cells.
        /// </summary>
        /// <param name="screenLength"></param>
        /// <returns></returns>
        [Pure]
        private double ScreenToGrid(double screenLength)
        {
            return screenLength / _gridStep;
        }

        /// <summary>
        /// Convert a grid coordinate to a screen coordinate.
        /// </summary>
        /// <param name="gridPoint"></param>
        /// <returns></returns>
        [Pure]
        private Point GridToScreen(Point gridPoint)
        {
            return new Point(gridPoint.X * _gridStep, gridPoint.Y * _gridStep);
        }

        /// <summary>
        /// Converts a size given in grid cells to a size given in (pixel-)units.
        /// </summary>
        /// <param name="gridSize"></param>
        /// <returns></returns>
        [Pure]
        private Size GridToScreen(Size gridSize)
        {
            return new Size(gridSize.Width * _gridStep, gridSize.Height * _gridStep);
        }

        /// <summary>
        /// Converts a length given in grid cells to a length given in (pixel-)units.
        /// </summary>
        /// <param name="gridLength"></param>
        /// <returns></returns>
        [Pure]
        private double GridToScreen(double gridLength)
        {
            return gridLength * _gridStep;
        }

        /// <summary>
        /// Calculates the exact center point of a given rect
        /// </summary>
        /// <param name="rect"></param>
        /// <returns></returns>
        [Pure]
        private static Point GetCenterPoint(Rect rect)
        {
            var pos = rect.Location;
            var size = rect.Size;
            pos.X += size.Width / 2;
            pos.Y += size.Height / 2;
            return pos;
        }

        /// <summary>
        /// Generates the rect to which the given object is rendered.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        [Pure]
        private Rect GetObjectScreenRect(AnnoObject obj)
        {
            return new Rect(GridToScreen(obj.Position), GridToScreen(obj.Size));
        }

        /// <summary>
        /// Gets the rect which is used for collision detection for the given object.
        /// Prevents undesired collisions which occur when using GetObjectScreenRect().
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        [Pure]
        private static Rect GetObjectCollisionRect(AnnoObject obj)
        {
            return new Rect(obj.Position, new Size(obj.Size.Width - 0.5, obj.Size.Height - 0.5));
        }

        /// <summary>
        /// Rotates the given Size object, i.e. switches width and height.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        [Pure]
        private static Size Rotate(Size size)
        {
            return new Size(size.Height, size.Width);
        }

        #endregion

        #region Event handling

        #region Mouse

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            _mouseWithinControl = true;
            Focus();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _mouseWithinControl = false;
            InvalidateVisual();
        }

        /// <summary>
        /// Handles the zoom level
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            GridSize += e.Delta / 100;
        }

        private void HandleMouse(MouseEventArgs e)
        {
            // refresh retrieved mouse position
            _mousePosition = e.GetPosition(this);
            MoveCurrentObjectToMouse();
        }

        /// <summary>
        /// Handles pressing of mouse buttons
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            HandleMouse(e);
            if (e.ClickCount > 1)
            {
                var obj = GetObjectAt(_mousePosition);
                if (obj != null)
                {
                    _currentObject = new AnnoObject(obj);
                    FireOnCurrentObjectChange(_currentObject);
                }
                return;
            }
            _mouseDragStart = _mousePosition;
            if (e.LeftButton == MouseButtonState.Pressed && e.RightButton == MouseButtonState.Pressed)
            {
                CurrentMode = MouseMode.DragAllStart;
            }
            else if (e.LeftButton == MouseButtonState.Pressed && _currentObject != null)
            {
                // place new object
                TryPlaceCurrentObject();
            }
            else if (e.LeftButton == MouseButtonState.Pressed && _currentObject == null)
            {
                var obj = GetObjectAt(_mousePosition);
                if (obj == null)
                {
                    // user clicked nothing: start dragging the selection rect
                    CurrentMode = MouseMode.SelectionRectStart;
                }
                else if (!IsControlPressed())
                {
                    CurrentMode = _selectedObjects.Contains(obj) ? MouseMode.DragSelectionStart : MouseMode.DragSingleStart;
                }
            }
            InvalidateVisual();
        }

        /// <summary>
        /// Here be dragons.
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            HandleMouse(e);
            // check if user begins to drag
            if (Math.Abs(_mouseDragStart.X - _mousePosition.X) > 1 || Math.Abs(_mouseDragStart.Y - _mousePosition.Y) > 1)
            {
                switch (CurrentMode)
                {
                    case MouseMode.SelectionRectStart:
                        CurrentMode = MouseMode.SelectionRect;
                        _selectionRect = new Rect();
                        break;
                    case MouseMode.DragSelectionStart:
                        CurrentMode = MouseMode.DragSelection;
                        break;
                    case MouseMode.DragSingleStart:
                        _selectedObjects.Clear();
                        _selectedObjects.Add(GetObjectAt(_mouseDragStart));
                        CurrentMode = MouseMode.DragSelection;
                        break;
                    case MouseMode.DragAllStart:
                        CurrentMode = MouseMode.DragAll;
                        break;
                }
            }
            if (CurrentMode == MouseMode.DragAll)
            {
                // move all selected objects
                var dx = (int)ScreenToGrid(_mousePosition.X - _mouseDragStart.X);
                var dy = (int)ScreenToGrid(_mousePosition.Y - _mouseDragStart.Y);
                // check if the mouse has moved at least one grid cell in any direction
                if (dx != 0 || dy != 0)
                {
                    foreach (var obj in _placedObjects)
                    {
                        obj.Position.X += dx;
                        obj.Position.Y += dy;
                    }
                    // adjust the drag start to compensate the amount we already moved
                    _mouseDragStart.X += GridToScreen(dx);
                    _mouseDragStart.Y += GridToScreen(dy);
                }
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_currentObject != null)
                {
                    // place new object
                    TryPlaceCurrentObject();
                }
                else
                {
                    // selection of multiple objects
                    switch (CurrentMode)
                    {
                        case MouseMode.SelectionRect:
                            if (IsControlPressed())
                            {
                                // remove previously selected by the selection rect
                                _selectedObjects.RemoveAll(_ => GetObjectScreenRect(_).IntersectsWith(_selectionRect));
                            }
                            else
                            {
                                _selectedObjects.Clear();
                            }
                            // adjust rect
                            _selectionRect = new Rect(_mouseDragStart, _mousePosition);
                            // select intersecting objects
                            _selectedObjects.AddRange(_placedObjects.FindAll(_ => GetObjectScreenRect(_).IntersectsWith(_selectionRect)));
                            break;
                        case MouseMode.DragSelection:
                            // move all selected objects
                            var dx = (int)ScreenToGrid(_mousePosition.X - _mouseDragStart.X);
                            var dy = (int)ScreenToGrid(_mousePosition.Y - _mouseDragStart.Y);
                            // check if the mouse has moved at least one grid cell in any direction
                            if (dx == 0 && dy == 0)
                            {
                                break;
                            }
                            var unselected = _placedObjects.FindAll(_ => !_selectedObjects.Contains(_));
                            var collisionsExist = false;
                            // temporary move each object and check if collisions with unselected objects exist
                            foreach (var obj in _selectedObjects)
                            {
                                var originalPosition = obj.Position;
                                // move object
                                obj.Position.X += dx;
                                obj.Position.Y += dy;
                                // check for collisions
                                var collides = unselected.Find(_ => ObjectIntersectionExists(obj, _)) != null;
                                obj.Position = originalPosition;
                                if (collides)
                                {
                                    collisionsExist = true;
                                    break;
                                }
                            }
                            // if no collisions were found, permanently move all selected objects
                            if (!collisionsExist)
                            {
                                foreach (var obj in _selectedObjects)
                                {
                                    obj.Position.X += dx;
                                    obj.Position.Y += dy;
                                }
                                // adjust the drag start to compensate the amount we already moved
                                _mouseDragStart.X += GridToScreen(dx);
                                _mouseDragStart.Y += GridToScreen(dy);
                            }
                            break;
                    }
                }
            }
            InvalidateVisual();
        }

        /// <summary>
        /// Handles the release of mouse buttons.
        /// </summary>
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            HandleMouse(e);
            if (CurrentMode == MouseMode.DragAll)
            {
                if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released)
                {
                    CurrentMode = MouseMode.Standard;
                }
                return;
            }
            if (e.ChangedButton == MouseButton.Left && _currentObject == null)
            {
                switch (CurrentMode)
                {
                    default:
                        // clear selection if no key is pressed
                        if (!IsControlPressed())
                        {
                            _selectedObjects.Clear();
                        }
                        var obj = GetObjectAt(_mousePosition);
                        if (obj != null)
                        {
                            // user clicked an object: select or deselect it
                            if (_selectedObjects.Contains(obj))
                            {
                                _selectedObjects.Remove(obj);
                            }
                            else
                            {
                                _selectedObjects.Add(obj);
                            }
                        }
                        // return to standard mode, i.e. clear any drag-start modes
                        CurrentMode = MouseMode.Standard;
                        break;
                    case MouseMode.SelectionRect:
                        // cancel dragging of selection rect
                        CurrentMode = MouseMode.Standard;
                        break;
                    case MouseMode.DragSelection:
                        // stop dragging of selected objects
                        CurrentMode = MouseMode.Standard;
                        break;
                }
            }
            if (e.ChangedButton == MouseButton.Right)
            {
                switch (CurrentMode)
                {
                    case MouseMode.Standard:
                        if (_currentObject == null)
                        {
                            var obj = GetObjectAt(_mousePosition);
                            if (obj == null)
                            {
                                if (!IsControlPressed())
                                {
                                    // clear selection
                                    _selectedObjects.Clear();
                                }
                            }
                            else
                            {
                                // remove clicked object
                                _placedObjects.Remove(obj);
                                _selectedObjects.Remove(obj);
                            }
                        }
                        else
                        {
                            // cancel placement of object
                            _currentObject = null;
                        }
                        break;
                }
            }
            // rotate current object
            if (e.ChangedButton == MouseButton.Middle && _currentObject != null)
            {
                _currentObject.Size = Rotate(_currentObject.Size);
            }
            InvalidateVisual();
        }

        #endregion

        #region Keyboard

        /// <summary>
        /// Handles key presses
        /// </summary>
        /// <param name="e"></param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Delete:
                    // remove all currently selected objects from the grid and clear selection
                    _selectedObjects.ForEach(_ => _placedObjects.Remove(_));
                    _selectedObjects.Clear();
                    break;
            }
            InvalidateVisual();
        }

        /// <summary>
        /// Checks whether the user is pressing keys to signal that he wants to select multiple objects
        /// </summary>
        /// <returns></returns>
        private static bool IsControlPressed()
        {
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        }

        #endregion

        #endregion

        #region Collision handling

        /// <summary>
        /// Checks if there is a collision between given objects a and b.
        /// </summary>
        /// <param name="a">first object</param>
        /// <param name="b">second object</param>
        /// <returns>true if there is a collision, otherwise false</returns>
        private static bool ObjectIntersectionExists(AnnoObject a, AnnoObject b)
        {
            return GetObjectCollisionRect(a).IntersectsWith(GetObjectCollisionRect(b));
        }

        /// <summary>
        /// Tries to place the current object on the grid.
        /// Fails if there are any collisions.
        /// </summary>
        /// <returns>true if placement succeeded, otherwise false</returns>
        private bool TryPlaceCurrentObject()
        {
            if (_currentObject != null && !_placedObjects.Exists(_ => ObjectIntersectionExists(_currentObject, _)))
            {
                _placedObjects.Add(new AnnoObject(_currentObject));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the object at the given position given in screen coordinates.
        /// </summary>
        /// <param name="position">position given in screen coordinates</param>
        /// <returns>object at the position, if there is no object null</returns>
        private AnnoObject GetObjectAt(Point position)
        {
            return _placedObjects.FindLast(_ => GetObjectScreenRect(_).Contains(position));
        }

        #endregion

        #region API

        /// <summary>
        /// Sets the current object, i.e. the object which the user can place.
        /// </summary>
        /// <param name="obj">object to apply</param>
        public void SetCurrentObject(AnnoObject obj)
        {
            obj.Position = _mousePosition;
            _currentObject = obj;
            InvalidateVisual();
        }

        /// <summary>
        /// Removes all objects from the grid.
        /// </summary>
        public void NewFile()
        {
            _placedObjects.Clear();
            _loadedFile = "";
            InvalidateVisual();
        }

        /// <summary>
        /// Resets the zoom to the default level.
        /// </summary>
        public void ResetZoom()
        {
            GridSize = GridStepDefault;
        }

        /// <summary>
        /// Normalizes the layout with border parameter set to zero.
        /// </summary>
        public void Normalize()
        {
            Normalize(0);
        }
        
        /// <summary>
        /// Normalizes the layout, i.e. moves all objects so that the top-most and left-most objects are exactly at the top and left coordinate zero if border is zero.
        /// Otherwise moves all objects further to the bottom-right by border in grid-units.
        /// </summary>
        /// <param name="border"></param>
        public void Normalize(int border)
        {
            if (_placedObjects.Count == 0)
            {
                return;
            }
            var dx = _placedObjects.Min(_ => _.Position.X) - border;
            var dy = _placedObjects.Min(_ => _.Position.Y) - border;
            _placedObjects.ForEach(_ => _.Position.X -= dx);
            _placedObjects.ForEach(_ => _.Position.Y -= dy);
            InvalidateVisual();
        }

        #endregion

        #region Save/Load/Export methods

        /// <summary>
        /// Writes layout to file.
        /// </summary>
        private void SaveFile()
        {
            try
            {
                Normalize(1);
                DataIO.SaveToFile(_placedObjects, _loadedFile);
            }
            catch (Exception e)
            {
                IOErrorMessageBox(e);
            }
        }

        /// <summary>
        /// Saves the current layout to file.
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(_loadedFile))
            {
                SaveAs();
            }
            else
            {
                SaveFile();
            }
        }

        /// <summary>
        /// Opens a dialog and saves the current layout to file.
        /// </summary>
        public void SaveAs()
        {
            var dialog = new SaveFileDialog
            {
                DefaultExt = ".ad",
                Filter = "Anno Designer Files (*.ad)|*.ad|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                _loadedFile = dialog.FileName;
                SaveFile();
            }
        }

        /// <summary>
        /// Opens a dialog and loads the given file.
        /// </summary>
        public void OpenFile()
        {
            var dialog = new OpenFileDialog
            {
                DefaultExt = ".ad",
                Filter = "Anno Designer Files (*.ad)|*.ad|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                OpenFile(dialog.FileName);
            }
        }

        /// <summary>
        /// Loads a new layout from file.
        /// </summary>
        public void OpenFile(string filename)
        {
            try
            {
                _selectedObjects.Clear();
                DataIO.LoadFromFile(out _placedObjects, filename);
                _loadedFile = filename;
                Normalize(1);
            }
            catch (Exception e)
            {
                IOErrorMessageBox(e);
            }
        }

        /// <summary>
        /// Renders the current layout to file.
        /// </summary>
        /// <param name="exportZoom">indicates whether the current zoom level should be applied, if false the default zoom is used</param>
        /// <param name="exportSelection">indicates whether selection and influence highlights should be rendered</param>
        public void ExportImage(bool exportZoom, bool exportSelection)
        {
            var dialog = new SaveFileDialog
            {
                DefaultExt = ".png",
                Filter = "PNG (*.png)|*.png|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    RenderToFile(dialog.FileName, 1, exportZoom, exportSelection);
                }
                catch (Exception e)
                {
                    IOErrorMessageBox(e);
                }
            }
        }

        /// <summary>
        /// Renders the current layout to file.
        /// </summary>
        /// <param name="filename">filename of the output image</param>
        /// <param name="border">normalization value used prior to exporting</param>
        /// <param name="exportZoom">indicates whether the current zoom level should be applied, if false the default zoom is used</param>
        /// <param name="exportSelection">indicates whether selection and influence highlights should be rendered</param>
        private void RenderToFile(string filename, int border, bool exportZoom, bool exportSelection)
        {
            //TODO: copy objects to output target before normalization
            Normalize(border);
            // initialize output canvas
            var target = new AnnoCanvas
            {
                _placedObjects = _placedObjects,
                RenderGrid = RenderGrid,
                RenderIcon = RenderIcon,
                RenderLabel = RenderLabel
            };
            if (exportZoom)
            {
                target.GridSize = GridSize;
            }
            if (exportSelection)
            {
                target._selectedObjects.AddRange(_selectedObjects);
            }
            // calculate correct size
            var width = target.GridToScreen(_placedObjects.Max(_ => _.Position.X + _.Size.Width) + border) + 1;
            var height = target.GridToScreen(_placedObjects.Max(_ => _.Position.Y + _.Size.Height) + border) + 1;
            target.Width = width;
            target.Height = height;
            // correctly apply size
            var outputSize = new Size(width, height);
            target.Measure(outputSize);
            target.Arrange(new Rect(outputSize));
            // render canvas to file
            DataIO.RenderToFile(target, filename);
        }

        /// <summary>
        /// Displays a message box containing some error information.
        /// </summary>
        /// <param name="e">exception containing error information</param>
        private static void IOErrorMessageBox(Exception e)
        {
            MessageBox.Show(e.Message, "Something went wrong while saving/loading file.");
        }

        #endregion

        #region Commands

        private static readonly Dictionary<ICommand, Action<AnnoCanvas>> CommandExecuteMappings;

        static AnnoCanvas()
        {
            CommandExecuteMappings = new Dictionary<ICommand, Action<AnnoCanvas>>
            {
                { ApplicationCommands.New, _ => _.NewFile() },
                { ApplicationCommands.Open, _ => _.OpenFile() },
                { ApplicationCommands.Save, _ => _.Save() },
                { ApplicationCommands.SaveAs, _ => _.SaveAs() }
            };
            foreach (var action in CommandExecuteMappings)
            {
                CommandManager.RegisterClassCommandBinding(typeof(AnnoCanvas), new CommandBinding(action.Key, ExecuteCommand));
            }
        }

        private static void ExecuteCommand(object sender, ExecutedRoutedEventArgs e)
        {
            var canvas = sender as AnnoCanvas;
            if (canvas != null && CommandExecuteMappings.ContainsKey(e.Command))
            {
                CommandExecuteMappings[e.Command].Invoke(canvas);
                e.Handled = true;
            }
        }
    
        #endregion
    }
}