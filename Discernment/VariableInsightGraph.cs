using System.Collections.Generic;

namespace Discernment
{
    /// <summary>
    /// Represents a node in the variable insight graph.
    /// </summary>
    internal class InsightNode
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string SourceCodeLine { get; set; } = string.Empty;
        public InsightNodeKind Kind { get; set; }
        public HashSet<InsightEdge> Edges { get; set; } = new();
        
        // Graph layout properties
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Represents an edge in the variable insight graph showing how one element affects another.
    /// </summary>
    internal class InsightEdge
    {
        public InsightNode Target { get; set; } = null!;
        public string RelationKind { get; set; } = string.Empty;
        public string SourceLocation { get; set; } = string.Empty;
    }

    /// <summary>
    /// The kind of node in the insight graph.
    /// </summary>
    internal enum InsightNodeKind
    {
        Variable,
        Parameter,
        Field,
        Property,
        Method,
        Expression
    }

    /// <summary>
    /// Represents the complete variable insight graph.
    /// </summary>
    internal class VariableInsightGraph
    {
        public InsightNode RootNode { get; set; } = null!;
        public HashSet<InsightNode> AllNodes { get; set; } = new();
        public int TotalReferences { get; set; }
    }
}

