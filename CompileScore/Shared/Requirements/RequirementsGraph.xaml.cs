﻿using CompileScore.Includers;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CompileScore.Requirements
{
    public class RequirementGraphNode
    {
        public ParserFileRequirements Value { set; get; }

        public RequirementGraphNode ChildNode { set; get; }

        public CompileValue ProfilerValue { set; get; }

        public object IncluderValue { set; get; }

        public string Label { set; get; }

        public int Row { set; get; } = -1;
        public int Column { set; get; } = -1;

        

        //precomputed requirement icons
    }

    public class RequirementGraphRoot
    {
        public ParserUnit Value { get; set; }

        public List<RequirementGraphNode> Nodes { set; get; }

        public object ProfilerValue { set; get; }

        public string Label {  get; set; }

        public int MaxColumn { set; get; } = 0;
    }

    public static class RequirementGraphGenerator
    {
        public static RequirementGraphNode BuildGraphNode(ParserFileRequirements file)
        {
            RequirementGraphNode node = new RequirementGraphNode();
            node.Value = file;
            node.Label = GetFileNameSafe(file.Name);

            object profilerObject = CompilerData.Instance.SeekProfilerValueFromFullPath(file.Name);
            node.ProfilerValue = profilerObject as CompileValue;

            return node;
        }
        
        private static object GetIncluderData(object includer, CompileValue includee)
        {
            if (includer == null || includee == null)
                return null;

            int IncludeeIndex = CompilerData.Instance.GetIndexOf(CompilerData.CompileCategory.Include, includee);
            if (IncludeeIndex < 0)
                return null;

            if ( includer is UnitValue )
            {
                int includerIndex = CompilerData.Instance.GetIndexOf(includer as UnitValue);
                return Includers.CompilerIncluders.Instance.GetIncludeUnitValue(includerIndex, IncludeeIndex);
            }
            else if ( includer is CompileValue )
            {
                int includerIndex = CompilerData.Instance.GetIndexOf(CompilerData.CompileCategory.Include, includer as CompileValue);
                return Includers.CompilerIncluders.Instance.GetIncludeInclValue(includerIndex, IncludeeIndex);
            }

            return null;
        }

        public static RequirementGraphRoot BuildGraph(ParserUnit parserUnit)
        {
            if (parserUnit == null) return null;

            RequirementGraphRoot root = new RequirementGraphRoot();
            root.Value = parserUnit;
            root.Label = GetFileNameSafe(parserUnit.Filename);
            root.Nodes = new List<RequirementGraphNode>(parserUnit.DirectIncludes.Count);
            root.ProfilerValue = CompilerData.Instance.SeekProfilerValueFromFullPath(parserUnit.Filename);

            foreach (ParserFileRequirements file in parserUnit.DirectIncludes ?? Enumerable.Empty<ParserFileRequirements>())
            {
                int column = 0;

                RequirementGraphNode newNode = BuildGraphNode(file);
                newNode.Row = root.Nodes.Count;
                newNode.Column = column++;
                newNode.IncluderValue = GetIncluderData(root.ProfilerValue, newNode.ProfilerValue);

                RequirementGraphNode lastInclude = newNode;
                foreach (ParserFileRequirements indirectFile in file.Includes ?? Enumerable.Empty<ParserFileRequirements>())
                {
                    RequirementGraphNode indirectNode = BuildGraphNode(indirectFile);
                    indirectNode.Row = newNode.Row;
                    indirectNode.Column = column++;

                    lastInclude.ChildNode = indirectNode;
                    lastInclude = indirectNode;
                }

                root.MaxColumn = Math.Max(root.MaxColumn, column);

                root.Nodes.Add(newNode);
            }

            return root;
        }

        private static string GetFileNameSafe(string path)
        {
            string ret = EditorUtils.GetFileNameSafe(path);
            return ret == null ? "<Unknown>" : ret;
        }
    }

    public partial class RequirementsGraph : UserControl
    {
        private ToolTip tooltip = new ToolTip { Content = new RequirementsGraphTooltip(), Padding = new Thickness(0) };
        private DispatcherTimer tooltipTimer = new DispatcherTimer() { Interval = new TimeSpan(4000000) };

        const double CanvasPadding = 5.0;
        const double RootWidth = 20.0;
        const double RootWidthSeparation = 10.0;
        const double NodeWidth = 200.0;
        const double NodeHeight = 40.0;
        const double NodeWidthSeparation = 10.0;
        const double NodeHeightSeparation = 10.0;
        const double IndirectExtraSeparation = 20.0;

        private double restoreScrollX = -1.0;
        private double restoreScrollY = -1.0;
        private Timeline.VisualHost baseVisual = new Timeline.VisualHost();
        private Timeline.VisualHost overlayVisual = new Timeline.VisualHost();
        private Brush overlayBrush = Brushes.White.Clone();
        private Brush activeBrush  = Brushes.White.Clone();
        private Pen borderPen = new Pen(Brushes.Black, 1);
        private Pen dashedPen = new Pen(Brushes.Black, 1);
        private Typeface Font = new Typeface("Verdana");

        private ParserUnit Unit { set; get; }
        private RequirementGraphRoot Root { set; get; }
        private object Hover { set; get; }
        private object Active { set; get; }

        public RequirementsGraph()
        {
            InitializeComponent();

            this.DataContext = this;

            CompilerData compilerData = CompilerData.Instance;
            compilerData.Hydrate(CompilerData.HydrateFlag.Main);

            compilerData.ScoreDataChanged += OnScoreDataChanged;
            //TODO ~ ramonv ~ add parser data changed 

            dashedPen.DashStyle = DashStyles.Dash;
            overlayBrush.Opacity = 0.3;
            activeBrush.Opacity = 0.2;

            tooltipTimer.Tick += ShowTooltip;

            scrollViewer.Loaded += OnScrollViewerLoaded;
            scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
            scrollViewer.On2DMouseScroll += OnScrollView2DMouseScroll;
            scrollViewer.MouseMove += OnScrollViewerMouseMove;
            scrollViewer.MouseLeave += OnScrollViewerMouseLeave;
            scrollViewer.OnMouseLeftClick += OnScrollViewerMouseLeftClick;
            //scrollViewer.MouseDoubleClick += OnScrollViewerDoubleClick;
            //scrollViewer.MouseRightButtonDown += OnScrollViewerContextMenu;
        }

        private void OnScoreDataChanged()
        {
            //Rebuild the graph with the new info
            ThreadHelper.ThrowIfNotOnUIThread();
            SetRoot(RequirementGraphGenerator.BuildGraph(Unit));
        }

        public void SetUnit(ParserUnit unit)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Unit = unit;
            SetRoot(RequirementGraphGenerator.BuildGraph(Unit));
        }

        private void SetRoot(RequirementGraphRoot root)
        {
            Root = root;

            restoreScrollX = -1.0;
            restoreScrollY = -1.0;
            SetupCanvas();
            RefreshAll();
        }

        private void SetHoverNode(object node)
        {
            if (node != Hover)
            {
                //Close Tooltip 
                tooltip.IsOpen = false;
                tooltipTimer.Stop();

                Hover = node;

                //Start Tooltip if applicable
                if (Hover != null)
                {
                    tooltipTimer.Start();
                }

                RenderOverlay();
            }
        }

        private void SetActiveNode(object node)
        {
            if (node != Active)
            {
                Active = node;

                //TODO ~ ramonv ~ send event so we can display this node's information in the other windows

                RenderOverlay();
            }
        }

        private void ShowTooltip(Object a, object b)
        {
            tooltipTimer.Stop();
            (tooltip.Content as RequirementsGraphTooltip).ReferenceNode = Hover;
            (tooltip.Content as RequirementsGraphTooltip).RootNode      = Root;
            tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse;
            tooltip.IsOpen = true;
            tooltip.PlacementTarget = this;
        }

        private void SetupCanvas()
        {
            if (Root != null)
            {
                int numVisalCells = Math.Max(Root.Nodes.Count, 1); //At least one cell to show the root node

                double extraWidth = Root.MaxColumn > 0 ? IndirectExtraSeparation : 0; 
                canvas.Width = RootWidth + RootWidthSeparation + extraWidth + (Root.MaxColumn * (NodeWidth + NodeWidthSeparation) ) + 2 * CanvasPadding;
                canvas.Height = (numVisalCells * ( NodeHeight + NodeHeightSeparation ) ) + ( 2 * CanvasPadding ) - NodeHeightSeparation;

                if (restoreScrollX >= 0)
                {
                    scrollViewer.ScrollToHorizontalOffset(restoreScrollX);
                }

                if (restoreScrollY >= 0)
                {
                    scrollViewer.ScrollToVerticalOffset(restoreScrollY);
                }
            }
        }

        private void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            //Fix the issue with the colored corner square
            ((Rectangle)scrollViewer.Template.FindName("Corner", scrollViewer)).Fill = scrollViewer.Background;

            SetupCanvas();
            //FocusNodeInternal(FocusPending == null ? Root : FocusPending);
            //FocusPending = null;
            RefreshAll();
        }

        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            restoreScrollX = scrollViewer.HorizontalOffset;
            restoreScrollY = scrollViewer.VerticalOffset;
            RefreshAll();
        }

        private void OnScrollView2DMouseScroll(object sender, Timeline.Mouse2DScrollEventArgs e)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta.X);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta.Y);
        }

        private void OnScrollViewerMouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(canvas);
            SetHoverNode(Root == null ? null : GetElementAtPosition(p.X, p.Y));
        }

        private void OnScrollViewerMouseLeave(object sender, MouseEventArgs e)
        {
            SetHoverNode(null);
        }

        private void OnScrollViewerMouseLeftClick(object sender, MouseButtonEventArgs e)
        {
            if ( Root == null )
                return;

            Point p = e.GetPosition(canvas);
            SetActiveNode(Root == null ? null : GetElementAtPosition(p.X, p.Y));
        }

        private void RefreshCanvasVisual(Timeline.VisualHost visual)
        {
            canvas.Children.Remove(visual);
            canvas.Children.Add(visual);
        }

        private void RefreshAll()
        {
            RenderBase();
            RenderOverlay();
        }

        private void RenderBase()
        {
            if (Root == null)
            {
                //Clear the canvas
                using (DrawingContext drawingContext = baseVisual.Visual.RenderOpen()) { }
                RefreshCanvasVisual(baseVisual);
            }
            else
            {
                borderPen.Brush = Foreground;
                dashedPen.Brush = Foreground;

                using (DrawingContext drawingContext = baseVisual.Visual.RenderOpen())
                {
                    //TODO ~ Ramonv~ placeholderCOLOR
                    RenderRootNode(drawingContext, Common.Colors.FrontEndBrush);

                    if ( Root.Nodes.Count > 0 )
                    {
                        //Get the Row and Columns we need to draw
                        int firstRow = GetRow(scrollViewer.VerticalOffset);
                        int lastRow = GetRow(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);

                        int firstColumn = GetColumn(scrollViewer.HorizontalOffset);
                        int lastColumn = GetColumn(scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth);

                        for ( int row = firstRow; row <= lastRow && row < Root.Nodes.Count; ++row)
                        {
                            RenderNodeRow(drawingContext, Root.Nodes[row], firstColumn, lastColumn);
                        }
                    }
                }

                //force a canvas redraw
                RefreshCanvasVisual(baseVisual);
            }
        }

        private void RenderOverlayedNode(DrawingContext drawingContext, object node, Brush brush)
        {
            if (node != null)
            {
                if (node is RequirementGraphNode)
                {
                    RequirementGraphNode graphNode = node as RequirementGraphNode;
                    RenderNodeSingle(drawingContext, graphNode, GetColumnLocation(graphNode.Column), GetRowLocation(graphNode.Row), brush);
                }
                else if (Hover is RequirementGraphRoot)
                {
                    RenderRootNode(drawingContext, brush);
                }
            }
        }

        private void RenderOverlay()
        {
            using (DrawingContext drawingContext = overlayVisual.Visual.RenderOpen())
            {
                RenderOverlayedNode(drawingContext, Active, activeBrush);
                RenderOverlayedNode(drawingContext, Hover,  overlayBrush);
            }

            RefreshCanvasVisual(overlayVisual);
        }

        private double GetRowLocation(int row)
        {
            double initialOffset = CanvasPadding;
            double cellSize = NodeHeight + NodeHeightSeparation;
            return initialOffset + row * cellSize; 
        }

        private double GetColumnLocation(int column)
        {
            double initialOffset = CanvasPadding + RootWidth + RootWidthSeparation + (column > 0 ? IndirectExtraSeparation : 0);
            double cellSize = NodeWidth + NodeWidthSeparation;
            return initialOffset + column * cellSize;
        }

        private int GetColumn(double x)
        {
            double initialOffset = CanvasPadding + RootWidth + RootWidthSeparation;
            if (x < initialOffset)
                return -1;
            
            double initialIndirectOffset = initialOffset + NodeWidth + NodeWidthSeparation + IndirectExtraSeparation;
            if (x < initialIndirectOffset)
                return 0;

            return (int)((x - ( initialOffset + IndirectExtraSeparation ) ) / (NodeWidth + NodeWidthSeparation) );
        }

        private int GetRow(double y)
        {
            double initialOffset = CanvasPadding;
            double cellSize = NodeHeight + NodeHeightSeparation;
            return (int)((y - initialOffset) / cellSize); 
        }

        private object GetElementAtPosition(double x, double y)
        {
            RequirementGraphRoot foundRoot = GetRootNodeAtPosition(x, y); 
            if (foundRoot != null)
            {
                return foundRoot;
            }
            return GetGraphNodeAtPosition(x, y);
        }

        private RequirementGraphRoot GetRootNodeAtPosition(double x, double y)
        {
            double localX = x - CanvasPadding; 
            double localY = y - CanvasPadding;
            double rootHeight = canvas.Height - ((2.0 * CanvasPadding));
            return localX < 0 || localY < 0 || localX > RootWidth || localY > rootHeight ? null : Root;
        }

        private RequirementGraphNode GetGraphNodeAtPosition(double x, double y)
        {
            if (Root == null) 
                return null;

            int column = GetColumn(x);
            int row    = GetRow(y);
            if (row < 0 || column < 0 || row >= Root.Nodes.Count) 
                return null;

            double localX = x - GetColumnLocation(column);
            double localY = y - GetRowLocation(row);
            if ( localX > NodeWidth || localY > NodeHeight) 
                return null;

            RequirementGraphNode node = Root.Nodes[row];
            for( int col = 0; node != null && col < column; ++col )
            {
                node = node.ChildNode;
            }

            return node;
        }

        private void RenderNodeRow(DrawingContext drawingContext, RequirementGraphNode node, int firstColumn, int lastColumn)
        {
            double nodePositionY = GetRowLocation(node.Row);

            for (int column = 0; column <= lastColumn && node != null; ++column)
            {
                if ( column >= firstColumn )
                {
                    double nodePositionX = GetColumnLocation(node.Column);
                    RenderConnectingLine(drawingContext, node, nodePositionX, nodePositionY);
                    RenderNodeSingle(drawingContext, node, nodePositionX, nodePositionY, Common.Colors.InstantiateFuncBrush);
                }

                node = node.ChildNode;
            }
        }

        private void RenderConnectingLine(DrawingContext drawingContext, RequirementGraphNode node, double posX, double posY)
        {
            if (node.Column == 0)
            {
                drawingContext.DrawLine(borderPen, new Point(posX, posY + (NodeHeight * 0.5)), new Point(posX - RootWidthSeparation, posY + (NodeHeight * 0.5)));
            }
            else if (node.Column == 1)
            {
                double separation = RootWidthSeparation + IndirectExtraSeparation;
                drawingContext.DrawLine(dashedPen, new Point(posX, posY + (NodeHeight * 0.5)), new Point(posX - separation, posY + (NodeHeight * 0.5)));
            }
            else
            {
                drawingContext.DrawLine(dashedPen, new Point(posX, posY + (NodeHeight * 0.5)), new Point(posX - NodeWidthSeparation, posY + (NodeHeight * 0.5)));
            }
        }

        private void RenderNodeSingle(DrawingContext drawingContext, RequirementGraphNode node, double posX, double posY, Brush brush)
        {
            drawingContext.DrawRectangle(brush, borderPen, new Rect(posX, posY, NodeWidth, NodeHeight));

            //Render text
            var UIText = new FormattedText(node.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 12, Common.Colors.GetCategoryForeground(), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            UIText.MaxTextWidth = Math.Min(NodeWidth, UIText.Width);
            UIText.MaxTextHeight = NodeHeight;

            double textPosX = posX + (NodeWidth - UIText.Width) * 0.5;
            double textPosY = posY + (NodeHeight - UIText.Height) * 0.5;

            drawingContext.DrawText(UIText, new Point(textPosX, textPosY));
        }

        private void RenderRootNode(DrawingContext drawingContext, Brush brush)
        {
            double rootHeight = canvas.Height - ( ( 2.0 * CanvasPadding) );
            drawingContext.DrawRectangle(brush, borderPen, new Rect(CanvasPadding, CanvasPadding, RootWidth, rootHeight));
        }

    }
}







/*
using CompileScore.Includers;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CompileScore.Timeline
{
    public class Mouse2DScrollEventArgs
    {
        public Mouse2DScrollEventArgs(Vector delta) { Delta = delta; }

        public Vector Delta { get; }
    };

    public delegate void Mouse2DScrollEventHandler(object sender, Mouse2DScrollEventArgs e);

    public class CustomScrollViewer : ScrollViewer
    {
        private bool Is2DScolling { set; get; }
        private Point lastScrollingPosition { set; get; }

        public event MouseWheelEventHandler OnControlMouseWheel;
        public event Mouse2DScrollEventHandler On2DMouseScroll;

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (OnControlMouseWheel != null)
                {
                    OnControlMouseWheel.Invoke(this,e);
                }
            }
            else 
            { 
                //Default behavior
                base.OnMouseWheel(e); 
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            if (Mouse.MiddleButton == MouseButtonState.Pressed)
            {
                Is2DScolling = true;
                lastScrollingPosition = e.GetPosition(this);
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            Is2DScolling = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        { 
            base.OnMouseDown(e);
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
            {
                Is2DScolling = true;
                lastScrollingPosition = e.GetPosition(this);
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Released)
            {
                Is2DScolling = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Is2DScolling)
            {
                Point nextPosition = e.GetPosition(this);
                if (On2DMouseScroll != null)
                {
                    On2DMouseScroll.Invoke(this, new Mouse2DScrollEventArgs(nextPosition - lastScrollingPosition));
                }
                lastScrollingPosition = nextPosition;
            }
            base.OnMouseMove(e);
        }
    }

    public class VisualHost : UIElement
    {
        public DrawingVisual Visual = new DrawingVisual();

        public VisualHost()
        {
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount
        {
            get { return Visual != null ? 1 : 0; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return Visual;
        }
    }

    /// <summary>
    /// Interaction logic for Timeline.xaml
    /// </summary>
    public partial class Timeline : UserControl
    {
        public enum Mode
        {
            Timeline, 
            Includers,
        };

        const double NodeHeight = 20.0;
        const double zoomIncreaseRatio = 1.1;
        const double FakeWidth = 3;
        const double textRenderMinWidth = 60;

        private double pixelToTimeRatio = -1.0;
        private double restoreScrollX = -1.0;
        private double restoreScrollY = -1.0;
        private bool zoomSliderLock = false; //used to avoid slider event feedback on itself 
        private VisualHost baseVisual = new VisualHost();
        private VisualHost overlayVisual = new VisualHost();
        private Brush overlayBrush = Brushes.White.Clone();
        private Pen borderPen = new Pen(Brushes.Black, 1);
        private Typeface Font = new Typeface("Verdana");

        private ToolTip tooltip = new ToolTip { Content = new TimelineNodeTooltip(), Padding = new Thickness(0) };
        private DispatcherTimer tooltipTimer = new DispatcherTimer() { Interval = new TimeSpan(4000000) };

        public static IncludersDisplayMode DefaultDisplayMode { set; get; } = IncludersDisplayMode.Once;

        private Mode CurrentMode { set; get; } = Mode.Timeline;
        private CompileValue IncludersValue { set; get; }
        private UnitValue Unit { set; get; }
        private TimelineNode Root { set; get; }
        private TimelineNode Hover { set; get; }
        private TimelineNode FocusPending { set; get; }
        private string SourcePath { set; get; }

        public Timeline()
        {
            InitializeComponent();

            this.DataContext = this;

            CompilerData compilerData = CompilerData.Instance;
            compilerData.Hydrate(CompilerData.HydrateFlag.Main);
            compilerData.Hydrate(CompilerData.HydrateFlag.Globals);

            compilerData.ScoreDataChanged += OnDataChanged;

            overlayBrush.Opacity = 0.3;

            tooltipTimer.Tick += ShowTooltip;

            nodeSearchBox.SetPlaceholderText("Search Nodes");

            GeneralSettingsPageGrid settings = CompilerData.Instance.GetGeneralSettings();
            displayModeComboBox.ItemsSource = Enum.GetValues(typeof(IncludersDisplayMode)).Cast<IncludersDisplayMode>();
            displayModeComboBox.SelectedIndex = (int)settings.OptionIncludersDefaultDisplayMode;
            displayModeComboBox.Visibility = Visibility.Collapsed;

            scrollViewer.Loaded += OnScrollViewerLoaded;
            scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
            scrollViewer.On2DMouseScroll += OnScrollView2DMouseScroll;
            scrollViewer.OnControlMouseWheel += OnScrollViewerControlMouseWheel;
            scrollViewer.MouseMove += OnScrollViewerMouseMove;
            scrollViewer.MouseLeave += OnScrollViewerMouseLeave;
            scrollViewer.MouseDoubleClick += OnScrollViewerDoubleClick;
            scrollViewer.MouseRightButtonDown += OnScrollViewerContextMenu;
            scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
            sliderZoom.ValueChanged += OnSliderZoomChanged;

            unitSearchBox.OnSelection += OnSearchUnitSelected;
            nodeSearchBox.OnSelection += OnSearchNodeSelected;

            RefrehsSearchUnitBox();
        }

        public void SetUnit(UnitValue unit, string unitPath = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            SetMode(Mode.Timeline);
            Unit = unit;
            SourcePath = unitPath != null && unit != null? unitPath : CompilerData.Instance.Folders.GetUnitPath(unit);

            SetRoot(CompilerTimeline.Instance.LoadTimeline(Unit));
        }

        public void SetIncluders(CompileValue value, string valuePath = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            SetMode(Mode.Includers);
            IncludersValue = value;
            SourcePath = valuePath != null && IncludersValue != null? valuePath : CompilerData.Instance.Folders.GetValuePath(value);

            int index = CompilerData.Instance.GetIndexOf(CompilerData.CompileCategory.Include, value);
            SetRoot(index >= 0 ? Includers.CompilerIncluders.Instance.BuildIncludersTree((uint)index, GetSelectedDisplayMode()) : null);
        }

        public void FocusNode(CompileValue value)
        {
            if (FocusNodeInternal(value))
            {
                RefreshAll();
            }
        }

        private bool FocusNodeInternal(CompileValue value)
        {
            FocusPending = FindNodeByValue(value);
            return FocusNodeInternal(FocusPending);
        }

        private bool FocusNodeInternal(TimelineNode node)
        {
            if (node != null && Root != null)
            {
                const double margin = 10;

                double viewPortWidth = scrollViewer.ViewportWidth - 2 * margin;
                double zoom = node.Duration == 0? GetMaxZoom() : (viewPortWidth > 0 ? viewPortWidth / node.Duration : 0);
                pixelToTimeRatio = Math.Max(Math.Min(zoom, GetMaxZoom()), GetMinZoom());
                double scrollOffset = (node.Start * pixelToTimeRatio) - margin;

                double verticalOffset = ((node.DepthLevel + 0.5) * NodeHeight) - (scrollViewer.ViewportHeight * 0.5);

                canvas.Width = Root.Duration * pixelToTimeRatio;
                scrollViewer.ScrollToHorizontalOffset(scrollOffset);
                scrollViewer.ScrollToVerticalOffset(verticalOffset);

                RefreshZoomSlider();
                return true;
            }
            return false;
        }

        public void SetMode(Mode newMode)
        {
            if (newMode != CurrentMode)
            {
                CurrentMode = newMode;

                (tooltip.Content as TimelineNodeTooltip).Mode = newMode;

                if (CurrentMode != Mode.Timeline) { Unit = null; }
                if (CurrentMode != Mode.Includers) { IncludersValue = null; }

                RefrehsSearchUnitBox();

                displayModeComboBox.Visibility = CurrentMode == Mode.Includers ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private TimelineNode FindNodeByValue(object value)
        {
            if (value != null && Root != null)
            {
                return FindNodeByValueRecursive(Root, value);
            }
            return null;
        }

        private TimelineNode FindNodeByValueRecursive(TimelineNode node, object value)
        {
            if (node.Value == value)
            {
                return node; 
            }

            foreach(TimelineNode child in node.Children)
            {
                TimelineNode found = FindNodeByValueRecursive(child, value);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnDataChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            CompilerData compilerData = CompilerData.Instance;

            RefrehsSearchUnitBox();

            switch(CurrentMode)
            {
                case Mode.Timeline:
                    {
                        if (Unit != null)
                        {
                            UnitValue unit = compilerData.Folders.GetUnitByPath(SourcePath);
                            if ( unit != null )
                            {
                                SetUnit(unit,SourcePath);
                            }
                            else
                            {
                                SetUnit(compilerData.GetUnitByName(Unit.Name));
                            }
                        }
                        break;
                    }
                case Mode.Includers:
                    {
                        if (IncludersValue != null)
                        {
                            CompileValue value = compilerData.Folders.GetValueByPath(CompilerData.CompileCategory.Include,SourcePath);
                            if (value != null)
                            {
                                SetIncluders(value, SourcePath);
                            }
                            else
                            {
                                SetIncluders(compilerData.GetValueByName(CompilerData.CompileCategory.Include, IncludersValue.Name));
                            }
                        }

                        break;
                    }
            }
        }

        private void SetRoot(TimelineNode root)
        {
            Root = root;
            nodeSearchBox.SetData(ComputeFlatNameList());

            pixelToTimeRatio = -1.0;
            restoreScrollX = -1.0;
            restoreScrollY = -1.0;
            SetupCanvas();
            RefreshAll();
        }

        private List<string> ComputeFlatNameList()
        {
            List<string> list = new List<string>();
            if (Root != null)
            {
                ComputeFlatNameListRecursive(list,Root);
            }
            list.Sort();
            return list;
        }

        private void ComputeFlatNameListRecursive(List<string> list, TimelineNode node)
        {
            list.Add(node.Label);
            foreach (TimelineNode child in node.Children)
            {
                ComputeFlatNameListRecursive(list, child);
            }
        }

        private void RefrehsSearchUnitBox()
        {
            switch(CurrentMode)
            {
                case Mode.Timeline:
                {
                    unitSearchBox.SetPlaceholderText("Search Units");
                    RefreshSearchUnitListTimeline();
                    break;
                }
                case Mode.Includers:
                {
                    unitSearchBox.SetPlaceholderText("Search Includes");
                    RefreshSearchUnitListIncluders();
                    break;
                }
            }
        }

        private void RefreshSearchUnitListTimeline()
        {
            List<string> list = new List<string>();
            var units = CompilerData.Instance.GetUnits();
            foreach (UnitValue element in units)
            {
                list.Add(element.Name);
            }
            list.Sort();
            unitSearchBox.SetData(list);
        }

        private void RefreshSearchUnitListIncluders()
        {
            List<string> list = new List<string>();
            var values = CompilerData.Instance.GetValues(CompilerData.CompileCategory.Include);
            foreach (CompileValue element in values)
            {
                list.Add(element.Name);
            }
            list.Sort();
            unitSearchBox.SetData(list);
        }

        private void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            //Fix the issue with the colored corner square
            ((Rectangle)scrollViewer.Template.FindName("Corner", scrollViewer)).Fill = scrollViewer.Background;

            SetupCanvas();
            FocusNodeInternal(FocusPending == null? Root : FocusPending);
            FocusPending = null;
            RefreshAll();
        }

        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            restoreScrollX = scrollViewer.HorizontalOffset;
            restoreScrollY = scrollViewer.VerticalOffset;
            RefreshAll();
        }

        private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Root != null)
            {
                double prevRatio = pixelToTimeRatio;
                pixelToTimeRatio = Math.Min(Math.Max(GetMinZoom(),pixelToTimeRatio),GetMaxZoom());

                if (prevRatio != pixelToTimeRatio)
                { 
                    canvas.Width = Root.Duration * pixelToTimeRatio;
                }

                RefreshZoomSlider();
            }
        }

        private void ApplyHorizontalZoom(double targetRatio, double anchorPosX)
        {
            double canvasPosX = anchorPosX + scrollViewer.HorizontalOffset;
            double realTimeOffset = canvasPosX / pixelToTimeRatio;
            double prevRatio = pixelToTimeRatio;

            pixelToTimeRatio = Math.Max(Math.Min(targetRatio, GetMaxZoom()), GetMinZoom());

            if (Root != null && prevRatio != pixelToTimeRatio)
            {
                double nextOffset = realTimeOffset * pixelToTimeRatio;
                double scrollOffset = nextOffset - anchorPosX;

                canvas.Width = Root.Duration * pixelToTimeRatio;
                scrollViewer.ScrollToHorizontalOffset(scrollOffset);
            }
        }           

        private void OnScrollViewerControlMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double targetRatio = e.Delta > 0 ? pixelToTimeRatio * zoomIncreaseRatio : pixelToTimeRatio / zoomIncreaseRatio;           
            double anchorPosX = e.GetPosition(scrollViewer).X;

            ApplyHorizontalZoom(targetRatio, anchorPosX);

            RefreshZoomSlider();
            e.Handled = true;
        }

        private void OnScrollView2DMouseScroll(object sender, Mouse2DScrollEventArgs e)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta.X);
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta.Y);
        }

        private void OnScrollViewerMouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(canvas);
            SetHoverNode(Root == null? null : GetNodeAtPosition(Root,PixelToTime(p.X),PixelToDepth(p.Y)));
        }

        private void OnScrollViewerMouseLeave(object sender, MouseEventArgs e)
        {
            SetHoverNode(null);
        }

        private void OnScrollViewerDoubleClick(object sender, MouseButtonEventArgs e)
        {             
            if (Root != null && e.ChangedButton == MouseButton.Left)
            {
                Point p = e.GetPosition(canvas);
                FocusNodeInternal(GetNodeAtPosition(Root, PixelToTime(p.X), PixelToDepth(p.Y)));
            }
        }
        public IncludersDisplayMode GetSelectedDisplayMode()
        {
            return displayModeComboBox.SelectedItem != null ? (IncludersDisplayMode)displayModeComboBox.SelectedItem : IncludersDisplayMode.Once;
        }

        private void DisplayModeComboBox_SelectionChanged(object sender, object e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if ( CurrentMode == Mode.Includers && IncludersValue != null )
            {
                SetIncluders(IncludersValue, SourcePath);
            }
        }

        private void CreateContextualMenu(TimelineNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            System.Windows.Forms.ContextMenuStrip contextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            AppendContextualMenuValue(contextMenuStrip, node, node.Value);
            contextMenuStrip.Show(System.Windows.Forms.Control.MousePosition);
        }

        private void AppendContextualMenuValue(System.Windows.Forms.ContextMenuStrip contextMenuStrip, TimelineNode node, object nodeValue )
        {           
            ThreadHelper.ThrowIfNotOnUIThread();

            if (nodeValue is CompileValue)
            {
                var value = nodeValue as CompileValue;

                if (node.Category == CompilerData.CompileCategory.Include)
                {
                    if (CurrentMode != Mode.Timeline)
                    {
                        //outside timeline
                        contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Locate Max Timeline", (a, b) => CompilerTimeline.Instance.DisplayTimeline(value.MaxUnit, value)));
                        contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Locate Max Self Timeline", (a, b) => CompilerTimeline.Instance.DisplayTimeline(value.SelfMaxUnit, value)));
                    }
                    
                    if (CurrentMode != Mode.Includers)
                    {
                        //in timeline
                        contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Show Includers Graph", (a, b) => Includers.CompilerIncluders.Instance.DisplayIncluders(value)));
                    }

                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Open File", (sender, e) => EditorUtils.OpenFile(value)));
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Copy Full Path", (a, b) => Clipboard.SetText(CompilerData.Instance.Folders.GetValuePathSafe(value))));
                }

                if (value.Name.Length > 0)
                {
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Copy Name", (sender, e) => Clipboard.SetText(value.Name)));
                }
                
            }
            else if (nodeValue is UnitValue)
            {
                var value = nodeValue as UnitValue;

                contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Open File", (sender, e) => EditorUtils.OpenFile(value)));
                contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Copy Full Path", (a, b) => Clipboard.SetText(CompilerData.Instance.Folders.GetUnitPathSafe(value))));

                if (value.Name.Length > 0)
                {
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Copy Name", (sender, e) => Clipboard.SetText(value.Name)));
                }

            }
            else if ((nodeValue is IncluderTreeLink))
            {
                var value = nodeValue as IncluderTreeLink;
                if ( value.Value != null && value.Value is IncludersInclValue )
                {
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Locate Incl Max Timeline", (a, b) => CompilerTimeline.Instance.DisplayTimeline(CompilerData.Instance.GetUnitByIndex((value.Value as IncludersInclValue).MaxId), value.Includee)));
                }
                
                if (value.Includer is UnitValue)
                {
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Locate Incl Timeline", (a, b) => CompilerTimeline.Instance.DisplayTimeline(value.Includer as UnitValue, value.Includee) ) );
                    contextMenuStrip.Items.Add(Common.UIHelpers.CreateContextItem("Open Unit Timeline", (a, b) => CompilerTimeline.Instance.DisplayTimeline(value.Includer as UnitValue) ) );
                }

                AppendContextualMenuValue(contextMenuStrip, node, value.Includer);
            }
        }

        private void OnScrollViewerContextMenu(object sender, MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Hover != null)
            {
                CreateContextualMenu(Hover);
            }
        }

        private void ShowTooltip(Object a, object b)
        {
            tooltipTimer.Stop();
            (tooltip.Content as TimelineNodeTooltip).ReferenceNode = Hover;
            tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse;
            tooltip.IsOpen = true;
            tooltip.PlacementTarget = this;
        }

        private void SetHoverNode(TimelineNode node)
        {
            if (node != Hover)
            {
                //Close Tooltip 
                tooltip.IsOpen = false;
                tooltipTimer.Stop();

                Hover = node;

                //Start Tooltip if applicable
                if (Hover != null)
                {
                    tooltipTimer.Start();
                }

                RenderOverlay();
            }
        }

        private double GetMinZoom()
        {
            return Root != null && Root.Duration > 0 && scrollViewer.ViewportWidth > 0 ? scrollViewer.ViewportWidth/ Root.Duration : 1;
        }
        
        private double GetMaxZoom()
        {
            return 10.0;
        }

        private double PixelToTime(double pixels) { return pixels / pixelToTimeRatio; }

        private double TimeToPixel(double time) { return time * pixelToTimeRatio; }

        private uint PixelToDepth(double pixels) { return (uint)(pixels/NodeHeight); }
        private double DepthToPixel(uint depth) { return depth * NodeHeight;  }

        private void SetupCanvas()
        {
            if (Root != null)
            {
                pixelToTimeRatio = pixelToTimeRatio > 0? pixelToTimeRatio : GetMinZoom();
                canvas.Width = Root.Duration*pixelToTimeRatio;
                canvas.Height = (Root.MaxDepthLevel+2) * NodeHeight;

                if (restoreScrollX >= 0)
                {
                    scrollViewer.ScrollToHorizontalOffset(restoreScrollX);
                }

                if (restoreScrollY >= 0)
                {
                    scrollViewer.ScrollToVerticalOffset(restoreScrollY);
                }

                RefreshZoomSlider();
            }
        }

        private void RenderNodeRecursive(DrawingContext drawingContext, TimelineNode node, double clipTimeStart, double clipTimeEnd, double clipDepth, double fakeDurationThreshold)
        {
            //Clipping and LODs
            if (node.Duration == 0 || node.Start > clipTimeEnd || (node.Start + node.Duration) < clipTimeStart || node.DepthLevel > clipDepth)
            {
                return;
            }
            else if (node.Duration < fakeDurationThreshold)
            {
                RenderFake(drawingContext, node);
            }
            else
            {
                RenderNodeSingle(drawingContext, node, node.UIColor, clipTimeStart, clipTimeEnd);

                foreach (TimelineNode child in node.Children)
                {
                    RenderNodeRecursive(drawingContext, child, clipTimeStart, clipTimeEnd, clipDepth, fakeDurationThreshold);
                }
            }

        }

        private void RenderFake(DrawingContext drawingContext, TimelineNode node)
        {
            double posX = TimeToPixel(node.Start);
            double width = TimeToPixel(node.Duration);
            double posY = DepthToPixel(node.DepthLevel);

            drawingContext.DrawRectangle(this.Foreground, null, new Rect(posX, posY, width, NodeHeight * (1 + node.MaxDepthLevel - node.DepthLevel)));
        }

        private void RenderNodeSingle(DrawingContext drawingContext, TimelineNode node, Brush brush, double clipTimeStart, double clipTimeEnd)
        {
            double posY        = DepthToPixel(node.DepthLevel);
            double pixelStart  = TimeToPixel(Math.Max(clipTimeStart, node.Start));
            double pixelEnd    = TimeToPixel(Math.Min(clipTimeEnd, node.Start + node.Duration));
            double screenWidth = pixelEnd - pixelStart;

            drawingContext.DrawRectangle(brush, borderPen, new Rect(pixelStart, posY, screenWidth, NodeHeight));

            //Render text
            if (screenWidth >= textRenderMinWidth)
            {
                var UIText = new FormattedText(node.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 12, Common.Colors.GetCategoryForeground(), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                UIText.MaxTextWidth = Math.Min(screenWidth, UIText.Width);
                UIText.MaxTextHeight = NodeHeight;

                double textPosX = (pixelEnd + pixelStart - UIText.Width) * 0.5;
                double textPosY = posY + (NodeHeight - UIText.Height) * 0.5;

                drawingContext.DrawText(UIText, new Point(textPosX, textPosY));

            }
        }

        private void RefreshCanvasVisual(VisualHost visual)
        {
            canvas.Children.Remove(visual);
            canvas.Children.Add(visual);
        }

        private void RefreshAll()
        {
            RenderBase();
            RenderOverlay();
        }

        private void RenderBase()
        {
            if (Root == null)
            {
                //Clear the canvas
                using (DrawingContext drawingContext = baseVisual.Visual.RenderOpen()){}
                RefreshCanvasVisual(baseVisual);
            }
            else
            {
                borderPen.Brush = this.Foreground;

                double clipTimeStart = PixelToTime(scrollViewer.HorizontalOffset);
                double clipTimeEnd   = PixelToTime(scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth);
                uint   clipDepth     = PixelToDepth(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);
                double fakeDurationThreshold = PixelToTime(FakeWidth);

                //Setup 
                using (DrawingContext drawingContext = baseVisual.Visual.RenderOpen())
                {
                    RenderNodeRecursive(drawingContext, Root, clipTimeStart, clipTimeEnd, clipDepth, fakeDurationThreshold);
                }

                //force a canvas redraw
                RefreshCanvasVisual(baseVisual);
            }
        }

        private void RenderOverlay()
        {
            using (DrawingContext drawingContext = overlayVisual.Visual.RenderOpen())
            {
                double clipTimeStart = PixelToTime(scrollViewer.HorizontalOffset);
                double clipTimeEnd = PixelToTime(scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth);

                //perform clipping here
                if (Hover != null && Hover.Start < clipTimeEnd && (Hover.Start + Hover.Duration) > clipTimeStart)
                {
                    RenderNodeSingle(drawingContext, Hover, overlayBrush, clipTimeStart, clipTimeEnd);
                }
            }

            RefreshCanvasVisual(overlayVisual);
        }

        private TimelineNode GetNodeAtPosition(TimelineNode node, double time, uint depth)
        {
            if (time >= node.Start && time <= (node.Start+node.Duration))
            {
                if (depth == node.DepthLevel)
                {
                    return node;
                }
                else
                {
                    foreach (TimelineNode child in node.Children)
                    {
                        TimelineNode found = GetNodeAtPosition(child, time, depth);
                        if (found != null) return found;                        
                    }
                }
            }

            return null;
        }

        private void RefreshZoomSlider()
        {
            sliderZoom.Minimum = 0;
            sliderZoom.Maximum = 1;

            double minZoom = Math.Log(GetMinZoom());
            double maxZoom = Math.Log(GetMaxZoom());
            double range = maxZoom - minZoom;
            double current = Math.Log(pixelToTimeRatio);

            zoomSliderLock = true;
            sliderZoom.Value = range != 0? (current-minZoom)/ range : 0;
            zoomSliderLock = false;
        }

        private void OnSliderZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!zoomSliderLock)
            {               
                double minZoom = Math.Log(GetMinZoom());
                double maxZoom = Math.Log(GetMaxZoom());
                double targetRatio = Math.Exp(e.NewValue * (maxZoom - minZoom) + minZoom);
                double anchorPosX = scrollViewer.ViewportWidth * 0.5;
                ApplyHorizontalZoom(targetRatio, anchorPosX);
            }
        }

        private void OnSearchUnitSelected(object sender, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            switch(CurrentMode)
            {
                case Mode.Timeline:
                {
                    if (Unit == null || name != Unit.Name)
                    {
                        SetUnit(CompilerData.Instance.GetUnitByName(name));
                    }
                    break;
                }
                case Mode.Includers:
                {
                    if (IncludersValue == null || name != IncludersValue.Name)
                    {
                        SetIncluders(CompilerData.Instance.GetValueByName(CompilerData.CompileCategory.Include, name));
                    }
                    break;
                }
            }
            
        }

        private void OnSearchNodeSelected(object sender, string name)
        {
            if (Root != null && !string.IsNullOrEmpty(name))
            {
                FocusNodeInternal(FindNodeByNameRecursive(Root, name));
            }
        } 
        
        TimelineNode FindNodeByNameRecursive(TimelineNode node, string name)
        {
            if (node.Label == name)
            {
                return node; 
            }

            foreach ( TimelineNode child in node.Children)
            {
                TimelineNode found = FindNodeByNameRecursive(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

    }
}
*/