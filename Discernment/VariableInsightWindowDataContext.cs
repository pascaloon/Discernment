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
                Y = node.Y
            });
            
            // Add edges and recursively process children
            foreach (var edge in node.Edges)
            {
                const double nodeWidth = 300;
                const double nodeVisualHeight = 100; // More accurate estimate of actual rendered height
                const double edgePadding = 10; // Small padding from node edges
                
                edgeList.Add(new InsightEdgeViewModel
                {
                    StartX = node.X + nodeWidth / 2, // Center of node
                    StartY = node.Y + nodeVisualHeight + edgePadding,    // Bottom of node + padding
                    EndX = edge.Target.X + nodeWidth / 2, // Center of target
                    EndY = edge.Target.Y - edgePadding,   // Top of target - padding
                    RelationKind = edge.RelationKind
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
        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }
    }

    /// <summary>
    /// View model for an edge in the graph.
    /// </summary>
    [DataContract]
    internal class InsightEdgeViewModel
    {
        [DataMember]
        public double StartX { get; set; }
        
        [DataMember]
        public double StartY { get; set; }
        
        [DataMember]
        public double EndX { get; set; }
        
        [DataMember]
        public double EndY { get; set; }
        
        [DataMember]
        public string RelationKind { get; set; } = "";
    }
}

