using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Discernment;

namespace Discernment.Tests;

public class VariableInsightAnalyzerTests
{
    private async Task<(Document, int)> CreateTestDocumentAsync(string code, string variableName)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            metadataReferences: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            });

        var project = workspace.AddProject(projectInfo);
        var document = project.AddDocument("Test.cs", SourceText.From(code));

        // Find the position of the variable
        var position = code.IndexOf(variableName);
        if (position == -1)
        {
            throw new ArgumentException($"Variable '{variableName}' not found in code");
        }

        return (document, position);
    }

    private async Task<VariableInsightGraph?> AnalyzeAsync(string code, string variableName)
    {
        var (document, position) = await CreateTestDocumentAsync(code, variableName);
        var analyzer = new VariableInsightAnalyzer();
        return await analyzer.AnalyzeAsync(document, position, CancellationToken.None);
    }

    [Fact]
    public async Task Example1_MethodParameterMapping()
    {
        var code = @"
using System;

class Program
{
    static int someGlobalVariable = 0;

    static void Main()
    {
        int a = 2;
        int b = 3;
        int c = 4;
        int d = 5;
        int r = Method(a, b, c) + c + d;
    }

    static int Method(int p1, int p2, int p3)
    {
        someGlobalVariable = p1 * p2 * p3;
        int temp1 = p2 * 4;
        int temp2 = p2 * 5;
        return temp2 * 2;
    }
}";

        var graph = await AnalyzeAsync(code, "int r");
        
        Assert.NotNull(graph);
        Assert.NotNull(graph.RootNode);
        Assert.Equal("r", graph.RootNode.Name);

        // Expected edges from root:
        // r -> Method (direct usage)
        // r -> c (direct usage in + c)
        // r -> d (direct usage in + d)
        var rootEdges = graph.RootNode.Edges.ToList();
        Assert.Contains(rootEdges, e => e.Target.Name == "Method");
        Assert.Contains(rootEdges, e => e.Target.Name == "c");
        Assert.Contains(rootEdges, e => e.Target.Name == "d");

        // Find Method node
        var methodNode = rootEdges.First(e => e.Target.Name == "Method").Target;
        
        // Method -> temp2
        var methodEdges = methodNode.Edges.ToList();
        Assert.Contains(methodEdges, e => e.Target.Name == "temp2");

        // temp2 -> p2
        var temp2Node = methodEdges.First(e => e.Target.Name == "temp2").Target;
        var temp2Edges = temp2Node.Edges.ToList();
        Assert.Contains(temp2Edges, e => e.Target.Name == "p2");

        // p2 -> b (parameter mapping)
        var p2Node = temp2Edges.First(e => e.Target.Name == "p2").Target;
        var p2Edges = p2Node.Edges.ToList();
        Assert.Contains(p2Edges, e => e.Target.Name == "b");

        // Verify a is NOT in the graph (doesn't affect return)
        Assert.DoesNotContain(graph.AllNodes, n => n.Name == "a");
    }

    [Fact]
    public async Task Example2_HandlingObjects()
    {
        var code = @"
using System;

class Program
{
    static void Main()
    {
        int expectedCarWheels = 4;
        Car c = new Car() { NumWheels = expectedCarWheels };
        Truck t = new Truck() { NumBigWheels = 4 };
        int r = Method(c, t);
    }

    static int Method(Car p1, Truck p2)
    {
        int temp = p2.NumBigWheels;
        return p1.NumWheels;
    }
}

class Car
{
    public int NumWheels { get; set; }
}

class Truck
{
    public int NumBigWheels { get; set; }
}";

        var graph = await AnalyzeAsync(code, "int r");
        
        Assert.NotNull(graph);
        
        // r -> Method
        var rootEdges = graph.RootNode.Edges.ToList();
        Assert.Contains(rootEdges, e => e.Target.Name == "Method");

        // Method -> NumWheels (not NumBigWheels, as that's not in return)
        var methodNode = rootEdges.First(e => e.Target.Name == "Method").Target;
        var methodEdges = methodNode.Edges.ToList();
        Assert.Contains(methodEdges, e => e.Target.Name == "NumWheels");

        // NumWheels -> expectedCarWheels
        var numWheelsNode = methodEdges.First(e => e.Target.Name == "NumWheels").Target;
        var numWheelsEdges = numWheelsNode.Edges.ToList();
        Assert.Contains(numWheelsEdges, e => e.Target.Name == "expectedCarWheels");
    }

    [Fact]
    public async Task Example3_HandlingThisParameter()
    {
        var code = @"
using System;

class Program
{
    static void Main()
    {
        string someName = ""Paul"";
        var p = new Person() { Name = someName };
        int age = 4;
        string r = p.GetGreetings() + Person.GetStaticGreetings() + p.GetConsideredAsStatic(age);
    }
}

class Person
{
    public string Name { get; set; }
    
    public string GetGreetings()
    {
        return $""Hi! I'm {Name}."";
    }

    public static string GetStaticGreetings()
    {
        return $""Hi! I'm someone."";
    }

    public string GetConsideredAsStatic(int p1)
    {
        string a = Name;
        return $""Hi! I'm {p1} years old."";
    }
}";

        var graph = await AnalyzeAsync(code, "string r");
        
        Assert.NotNull(graph);
        
        var rootEdges = graph.RootNode.Edges.ToList();
        
        // r -> GetGreetings
        Assert.Contains(rootEdges, e => e.Target.Name == "GetGreetings");
        
        // r -> GetStaticGreetings
        Assert.Contains(rootEdges, e => e.Target.Name == "GetStaticGreetings");
        
        // r -> GetConsideredAsStatic
        Assert.Contains(rootEdges, e => e.Target.Name == "GetConsideredAsStatic");

        // GetGreetings -> Name
        var getGreetingsNode = rootEdges.First(e => e.Target.Name == "GetGreetings").Target;
        var getGreetingsEdges = getGreetingsNode.Edges.ToList();
        Assert.Contains(getGreetingsEdges, e => e.Target.Name == "Name");

        // Name -> someName
        var nameNode = getGreetingsEdges.First(e => e.Target.Name == "Name").Target;
        var nameEdges = nameNode.Edges.ToList();
        Assert.Contains(nameEdges, e => e.Target.Name == "someName");

        // GetConsideredAsStatic -> p1
        var getConsideredNode = rootEdges.First(e => e.Target.Name == "GetConsideredAsStatic").Target;
        var getConsideredEdges = getConsideredNode.Edges.ToList();
        Assert.Contains(getConsideredEdges, e => e.Target.Name == "p1");

        // p1 -> age
        var p1Node = getConsideredEdges.First(e => e.Target.Name == "p1").Target;
        var p1Edges = p1Node.Edges.ToList();
        Assert.Contains(p1Edges, e => e.Target.Name == "age");
    }

    [Fact]
    public async Task Example4_VirtualMethods()
    {
        var code = @"
using System;

class Program
{
    static void Main()
    {
        Shape s = new Rectangle() { Width = 2, Height = 3 };
        double r = s.GetArea();
    }
}

abstract class Shape
{
    public abstract double GetArea();
}

class Rectangle : Shape
{
    public double Width { get; set; }
    public double Height { get; set; }
    public override double GetArea() => Width * Height;
}

class Circle : Shape
{
    public double Radius { get; set; }
    public override double GetArea() => 3.14 * Radius * Radius;
}";

        var graph = await AnalyzeAsync(code, "double r");
        
        Assert.NotNull(graph);
        
        // r -> Shape.GetArea()
        var rootEdges = graph.RootNode.Edges.ToList();
        Assert.Contains(rootEdges, e => e.Target.Name == "GetArea");

        var getAreaNode = rootEdges.First(e => e.Target.Name == "GetArea").Target;
        var getAreaEdges = getAreaNode.Edges.ToList();
        
        // Shape.GetArea() -> Rectangle.GetArea() (Override)
        Assert.Contains(getAreaEdges, e => e.Target.Name == "GetArea" && e.RelationKind == "Override");
        
        // Shape.GetArea() -> Circle.GetArea() (Override)
        Assert.True(getAreaEdges.Count(e => e.Target.Name == "GetArea" && e.RelationKind == "Override") >= 1);

        // Find Rectangle.GetArea node
        var rectangleGetAreaNode = getAreaEdges.First(e => e.RelationKind == "Override").Target;
        var rectangleEdges = rectangleGetAreaNode.Edges.ToList();
        
        // Rectangle.GetArea() -> Width
        Assert.Contains(rectangleEdges, e => e.Target.Name == "Width");
        
        // Rectangle.GetArea() -> Height
        Assert.Contains(rectangleEdges, e => e.Target.Name == "Height");
    }

    [Fact]
    public async Task Example5_RootReferencesAndExternalAPIs()
    {
        var code = @"
using System;
using System.Collections.Generic;

class Program
{
    static int someGlobalVariable = 0;

    static void Main()
    {
        var list = new List<string>();
        Append(list, 5);
        list.Add(""Hello"");
        var r = new List<string>();
        r.AddRange(list);
    }

    static void Append(IList<string> l, int count)
    {
        for (int i = 0; i < count; i++)
        {
            l.Add(i.ToString());
            someGlobalVariable++;
        }
    }
}";

        var graph = await AnalyzeAsync(code, "var r =");
        
        Assert.NotNull(graph);
        Assert.Equal("r", graph.RootNode.Name);
        
        // r -> AddRange (method call on r)
        var rootEdges = graph.RootNode.Edges.ToList();
        Assert.Contains(rootEdges, e => e.Target.Name == "AddRange");

        // AddRange -> list (parameter to external API)
        var addRangeNode = rootEdges.First(e => e.Target.Name == "AddRange").Target;
        var addRangeEdges = addRangeNode.Edges.ToList();
        Assert.Contains(addRangeEdges, e => e.Target.Name == "list");

        // list should have edges to both List.Add and Program.Append
        var listNode = addRangeEdges.First(e => e.Target.Name == "list").Target;
        var listEdges = listNode.Edges.ToList();
        
        // list -> List.Add (from list.Add("Hello"))
        Assert.Contains(listEdges, e => e.Target.Name == "Add");
        
        // list -> Program.Append (from Append(list, 5))
        Assert.Contains(listEdges, e => e.Target.Name == "Append");

        // Program.Append -> Add (l.Add inside Append)
        var appendNode = listEdges.First(e => e.Target.Name == "Append").Target;
        var appendEdges = appendNode.Edges.ToList();
        Assert.Contains(appendEdges, e => e.Target.Name == "Add");
        
        // One of the Add nodes should have edges to i and l
        var addInAppendNode = appendEdges.First(e => e.Target.Name == "Add").Target;
        var addInAppendEdges = addInAppendNode.Edges.ToList();
        
        // List.Add -> i
        Assert.Contains(addInAppendEdges, e => e.Target.Name == "i");
        
        // List.Add -> l
        Assert.Contains(addInAppendEdges, e => e.Target.Name == "l");

        // i -> count (loop boundary)
        var iNode = addInAppendEdges.First(e => e.Target.Name == "i").Target;
        var iEdges = iNode.Edges.ToList();
        Assert.Contains(iEdges, e => e.Target.Name == "count");

        // l -> list (parameter mapping)
        var lNode = addInAppendEdges.First(e => e.Target.Name == "l").Target;
        var lEdges = lNode.Edges.ToList();
        Assert.Contains(lEdges, e => e.Target.Name == "list");
    }
}
