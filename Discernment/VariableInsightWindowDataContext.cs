using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.Extensibility.UI;

namespace Discernment
{
    /// <summary>
    /// Data context for the Variable Insight tool window.
    /// </summary>
    [DataContract]
    internal class VariableInsightWindowDataContext : NotifyPropertyChangedObject
    {
        private string rootVariableName = "No variable analyzed";
        private string rootVariableType = "";
        private string rootVariableLocation = "";
        private int totalReferences = 0;
        private ObservableCollection<InsightNodeViewModel> nodes = new();
        private ObservableCollection<InsightEdgeViewModel> edges = new();
        private string filePath = "";
        private InsightNodeViewModel? selectedNode = null;

        public VariableInsightWindowDataContext()
        {
            SelectNodeCommand = new AsyncCommand((parameter, cancellationToken) => 
                SelectNodeAsync(parameter as InsightNodeViewModel, cancellationToken));
        }

        [DataMember]
        public AsyncCommand SelectNodeCommand { get; }

        private async Task SelectNodeAsync(InsightNodeViewModel? node, CancellationToken cancellationToken)
        {
            if (node == null)
                return;

            SelectedNode = node;
            
            // Notify the window to navigate to the source
            if (VariableInsightWindow.Instance != null)
            {
                await VariableInsightWindow.Instance.NavigateToSourceAsync(node.FilePath, node.LineNumber, cancellationToken);
            }
        }

        [DataMember]
        public InsightNodeViewModel? SelectedNode
        {
            get => selectedNode;
            set
            {
                if (SetProperty(ref selectedNode, value))
                {
                    // Update all nodes' IsSelected property
                    foreach (var node in Nodes)
                    {
                        node.IsSelected = node == value;
                    }
                    
                    // Update all edges' IsHighlighted property
                    // Highlight edges where the selected node is either source or target
                    foreach (var edge in Edges)
                    {
                        edge.IsHighlighted = value != null && 
                            (edge.SourceNodeName == value.Name || edge.TargetNodeName == value.Name);
                    }
                }
            }
        }

        [DataMember]
        public ObservableCollection<InsightEdgeViewModel> Edges
        {
            get => edges;
            set => SetProperty(ref edges, value);
        }

        [DataMember]
        public string RootVariableName
        {
            get => rootVariableName;
            set => SetProperty(ref rootVariableName, value);
        }

        [DataMember]
        public string RootVariableType
        {
            get => rootVariableType;
            set => SetProperty(ref rootVariableType, value);
        }

        [DataMember]
        public string RootVariableLocation
        {
            get => rootVariableLocation;
            set => SetProperty(ref rootVariableLocation, value);
        }

        [DataMember]
        public int TotalReferences
        {
            get => totalReferences;
            set => SetProperty(ref totalReferences, value);
        }

        [DataMember]
        public ObservableCollection<InsightNodeViewModel> Nodes
        {
            get => nodes;
            set => SetProperty(ref nodes, value);
        }

        [DataMember]
        public string FilePath
        {
            get => filePath;
            set => SetProperty(ref filePath, value);
        }

        public void UpdateGraph(VariableInsightGraph graph, string path)
        {
            FilePath = path;
            RootVariableName = graph.RootNode.Name;
            RootVariableType = graph.RootNode.Type;
            RootVariableLocation = $"{System.IO.Path.GetFileName(path)}:{graph.RootNode.Location}";
            TotalReferences = graph.TotalReferences;

            // Calculate layout positions
            CalculateLayout(graph);

            // Build node view models
            var nodeList = new List<InsightNodeViewModel>();
            var edgeList = new List<InsightEdgeViewModel>();
            var visited = new HashSet<InsightNode>();
            
            BuildGraphViewModels(graph.RootNode, nodeList, edgeList, visited);
            
            Nodes = new ObservableCollection<InsightNodeViewModel>(nodeList);
            Edges = new ObservableCollection<InsightEdgeViewModel>(edgeList);
        }

        private void CalculateLayout(VariableInsightGraph graph)
        {
            // Simple hierarchical layout algorithm
            const double horizontalSpacing = 400;
            const double verticalSpacing = 250; // Increased for better edge visibility
            
            var levelMap = new Dictionary<InsightNode, int>();
            var positionInLevel = new Dictionary<int, int>();
            
            // Assign levels via BFS
            var queue = new Queue<(InsightNode node, int level)>();
            queue.Enqueue((graph.RootNode, 0));
            levelMap[graph.RootNode] = 0;
            
            while (queue.Count > 0)
            {
                var (node, level) = queue.Dequeue();
                
                foreach (var edge in node.Edges)
                {
                    if (!levelMap.ContainsKey(edge.Target))
                    {
                        levelMap[edge.Target] = level + 1;
                        queue.Enqueue((edge.Target, level + 1));
                    }
                }
            }
            
            // Assign positions
            foreach (var kvp in levelMap)
            {
                var node = kvp.Key;
                var level = kvp.Value;
                
                if (!positionInLevel.ContainsKey(level))
                    positionInLevel[level] = 0;
                
                node.X = positionInLevel[level] * horizontalSpacing;
                node.Y = level * verticalSpacing;
                
                positionInLevel[level]++;
            }
        }

        private void BuildGraphViewModels(
            InsightNode node,
            List<InsightNodeViewModel> nodeList,
            List<InsightEdgeViewModel> edgeList,
            HashSet<InsightNode> visited)
        {
            if (visited.Contains(node))
                return;
            
            visited.Add(node);
            
            // Parse location to extract line number
            var lineNumber = 0;
            if (!string.IsNullOrEmpty(node.Location))
            {
                var parts = node.Location.Split(':');
                if (parts.Length > 1 && int.TryParse(parts[1], out var line))
                {
                    lineNumber = line;
                }
            }
            
            // Add this node
            nodeList.Add(new InsightNodeViewModel
            {
                Name = node.Name,
                Type = node.Type,
                Location = node.Location,
                SourceCode = node.SourceCodeLine,
                FilePath = FilePath,
                LineNumber = lineNumber,
                X = node.X,
                Y = node.Y,
                Kind = node.Kind
            });
            
            // Add edges and recursively process children
            foreach (var edge in node.Edges)
            {
                const double nodeWidth = 300;
                const double nodeHeight = 120; // Increased to match actual rendered height
                
                edgeList.Add(new InsightEdgeViewModel
                {
                    SourceX = node.X,
                    SourceY = node.Y,
                    TargetX = edge.Target.X,
                    TargetY = edge.Target.Y,
                    NodeWidth = nodeWidth,
                    NodeHeight = nodeHeight,
                    RelationKind = edge.RelationKind,
                    TargetKind = edge.Target.Kind,
                    SourceNodeName = node.Name,
                    TargetNodeName = edge.Target.Name,
                    IsHighlighted = false
                });
                
                BuildGraphViewModels(edge.Target, nodeList, edgeList, visited);
            }
        }

        public void Clear()
        {
            RootVariableName = "No variable analyzed";
            RootVariableType = "";
            RootVariableLocation = "";
            TotalReferences = 0;
            Nodes = new ObservableCollection<InsightNodeViewModel>();
            Edges = new ObservableCollection<InsightEdgeViewModel>();
            FilePath = "";
        }
    }

    /// <summary>
    /// View model for a single insight node in the graph.
    /// </summary>
    [DataContract]
    internal class InsightNodeViewModel : NotifyPropertyChangedObject
    {
        private bool isSelected;

        [DataMember]
        public string Name { get; set; } = "";
        
        [DataMember]
        public string Type { get; set; } = "";
        
        [DataMember]
        public string Location { get; set; } = "";
        
        [DataMember]
        public string SourceCode { get; set; } = "";
        
        [DataMember]
        public string FilePath { get; set; } = "";
        
        [DataMember]
        public int LineNumber { get; set; }
        
        [DataMember]
        public double X { get; set; }
        
        [DataMember]
        public double Y { get; set; }
        
        [DataMember]
        public InsightNodeKind Kind { get; set; }

        [DataMember]
        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }
        
        /// <summary>
        /// Gets the background color for the node header based on its kind.
        /// Colors match Visual Studio dark theme.
        /// </summary>
        [DataMember]
        public string HeaderBackgroundColor
        {
            get
            {
                return Kind switch
                {
                    InsightNodeKind.Variable => "#FF4D4D50",    // Gray for local variables
                    InsightNodeKind.Parameter => "#FF4D4D50",   // Gray for parameters
                    InsightNodeKind.Field => "#FFBD632F",       // Orange for fields
                    InsightNodeKind.Property => "#FFBD632F",    // Orange for properties
                    InsightNodeKind.Method => "#FF267F99",      // Teal for methods
                    _ => "#FF3E3E42"                            // Default gray
                };
            }
        }
    }

    /// <summary>
    /// View model for an edge in the graph.
    /// </summary>
    [DataContract]
    internal class InsightEdgeViewModel : NotifyPropertyChangedObject
    {
        private const double ArrowLength = 12;
        private const double ArrowWidth = 8;
        private const double ArrowGap = 3; // Gap between arrow tip and node border to keep arrow visible
        
        private bool isHighlighted;
        private double edgeOpacity = 0.4;
        
        [DataMember]
        public double SourceX { get; set; }
        
        [DataMember]
        public double SourceY { get; set; }
        
        [DataMember]
        public double TargetX { get; set; }
        
        [DataMember]
        public double TargetY { get; set; }
        
        [DataMember]
        public double NodeWidth { get; set; }
        
        [DataMember]
        public double NodeHeight { get; set; }
        
        [DataMember]
        public string RelationKind { get; set; } = "";
        
        [DataMember]
        public InsightNodeKind TargetKind { get; set; }
        
        [DataMember]
        public bool IsHighlighted
        {
            get => isHighlighted;
            set
            {
                if (SetProperty(ref isHighlighted, value))
                {
                    // Also update EdgeOpacity
                    EdgeOpacity = value ? 1.0 : 0.4;
                }
            }
        }
        
        // Store references to source and target node names for selection tracking
        [DataMember]
        public string SourceNodeName { get; set; } = "";
        
        [DataMember]
        public string TargetNodeName { get; set; } = "";
        
        /// <summary>
        /// Gets whether this edge should show a label.
        /// Only show labels for special edge types like "Override".
        /// </summary>
        [DataMember]
        public bool ShowLabel => RelationKind == "Override";
        
        /// <summary>
        /// Gets the X position for the edge label (center of the edge).
        /// </summary>
        [DataMember]
        public double LabelX => (SourceX + TargetX) / 2 + NodeWidth / 2;
        
        /// <summary>
        /// Gets the Y position for the edge label (center of the edge).
        /// </summary>
        [DataMember]
        public double LabelY => (SourceY + TargetY) / 2 + NodeHeight / 2;
        
        /// <summary>
        /// Gets the color for the edge based on target node kind.
        /// Returns a dimmed version of the node kind color.
        /// </summary>
        [DataMember]
        public string EdgeColor
        {
            get
            {
                return TargetKind switch
                {
                    InsightNodeKind.Variable => "#FF6B6B6E",      // Dimmed gray
                    InsightNodeKind.Parameter => "#FF6B6B6E",     // Dimmed gray
                    InsightNodeKind.Field => "#FF8B5A3D",         // Dimmed orange
                    InsightNodeKind.Property => "#FF8B5A3D",      // Dimmed orange
                    InsightNodeKind.Method => "#FF4A9AAE",        // Dimmed teal
                    _ => "#FF5A5A5D"                              // Dimmed default
                };
            }
        }
        
        /// <summary>
        /// Gets the opacity for the edge. Full opacity when highlighted, dimmed otherwise.
        /// </summary>
        [DataMember]
        public double EdgeOpacity
        {
            get => edgeOpacity;
            set => SetProperty(ref edgeOpacity, value);
        }
        
        /// <summary>
        /// Generates the Path data string for drawing the arrow (line + arrowhead).
        /// </summary>
        [DataMember]
        public string PathData
        {
            get
            {
                // Get border intersection points
                var (startX, startY) = GetBorderIntersection(
                    SourceX, SourceY, TargetX, TargetY, NodeWidth, NodeHeight);
                var (endX, endY) = GetBorderIntersection(
                    TargetX, TargetY, SourceX, SourceY, NodeWidth, NodeHeight);
                
                // Calculate direction vector
                double dx = endX - startX;
                double dy = endY - startY;
                double length = Math.Sqrt(dx * dx + dy * dy);
                
                if (length < 0.001)
                {
                    return ""; // No arrow if points are the same
                }
                
                // Unit vector
                double ux = dx / length;
                double uy = dy / length;
                
                // Perpendicular unit vector
                double px = -uy;
                double py = ux;
                
                // Pull arrow tip back from border by ArrowGap to keep it visible
                double tipX = endX - ux * ArrowGap;
                double tipY = endY - uy * ArrowGap;
                
                // Base of arrow (where line ends and triangle starts)
                double baseX = tipX - ux * ArrowLength;
                double baseY = tipY - uy * ArrowLength;
                
                // Two corners of the arrowhead base
                double corner1X = baseX + px * ArrowWidth / 2;
                double corner1Y = baseY + py * ArrowWidth / 2;
                double corner2X = baseX - px * ArrowWidth / 2;
                double corner2Y = baseY - py * ArrowWidth / 2;
                
                // Build the path: Line from start to arrow base, then filled triangle for arrowhead
                var pathBuilder = new System.Text.StringBuilder();
                
                // Line from start to arrow base
                pathBuilder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "M {0:F2},{1:F2} L {2:F2},{3:F2} ",
                    startX, startY, baseX, baseY);
                
                // Arrowhead triangle (filled)
                pathBuilder.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "M {0:F2},{1:F2} L {2:F2},{3:F2} L {4:F2},{5:F2} Z",
                    tipX, tipY, corner1X, corner1Y, corner2X, corner2Y);
                
                return pathBuilder.ToString();
            }
        }
        
        /// <summary>
        /// Calculates the intersection point between a line from the center of nodeRect to targetCenter,
        /// and the border of nodeRect.
        /// </summary>
        private (double x, double y) GetBorderIntersection(
            double nodeX, double nodeY, 
            double targetCenterX, double targetCenterY,
            double width, double height)
        {
            // Calculate center of the node
            double centerX = nodeX + width / 2;
            double centerY = nodeY + height / 2;
            
            // Calculate center of target (for direction)
            double targetX = targetCenterX + width / 2;
            double targetY = targetCenterY + height / 2;
            
            // Direction vector from node center to target center
            double dx = targetX - centerX;
            double dy = targetY - centerY;
            
            // Avoid division by zero
            if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            {
                return (centerX, centerY);
            }
            
            // Normalize to unit vector for simpler calculation
            double length = Math.Sqrt(dx * dx + dy * dy);
            dx /= length;
            dy /= length;
            
            // Rectangle bounds
            double left = nodeX;
            double right = nodeX + width;
            double top = nodeY;
            double bottom = nodeY + height;
            
            // Calculate which edge we'll hit based on the direction
            // Use a simple approach: check which edge the ray hits first
            
            double tMin = double.MaxValue;
            double intersectX = centerX;
            double intersectY = centerY;
            
            // Check intersection with right edge (x = right)
            if (dx > 0.001)
            {
                double t = (right - centerX) / dx;
                double y = centerY + t * dy;
                if (y >= top && y <= bottom && t < tMin)
                {
                    tMin = t;
                    intersectX = right;
                    intersectY = y;
                }
            }
            
            // Check intersection with left edge (x = left)
            if (dx < -0.001)
            {
                double t = (left - centerX) / dx;
                double y = centerY + t * dy;
                if (y >= top && y <= bottom && t < tMin)
                {
                    tMin = t;
                    intersectX = left;
                    intersectY = y;
                }
            }
            
            // Check intersection with bottom edge (y = bottom)
            if (dy > 0.001)
            {
                double t = (bottom - centerY) / dy;
                double x = centerX + t * dx;
                if (x >= left && x <= right && t < tMin)
                {
                    tMin = t;
                    intersectX = x;
                    intersectY = bottom;
                }
            }
            
            // Check intersection with top edge (y = top)
            if (dy < -0.001)
            {
                double t = (top - centerY) / dy;
                double x = centerX + t * dx;
                if (x >= left && x <= right && t < tMin)
                {
                    tMin = t;
                    intersectX = x;
                    intersectY = top;
                }
            }
            
            return (intersectX, intersectY);
        }
    }
}

