# Discernment - Variable Insight for Visual Studio 2022

A powerful Visual Studio 2022 extension that helps you understand C# code by visualizing variable dependencies through an interactive, color-coded graph.

## Overview

**Discernment** uses Roslyn semantic analysis to trace backward data flow, showing you exactly what influences any variable in your code. Built with `VisualStudio.Extensibility` out-of-process extension model for modern Visual Studio integration.

## Features

### Interactive 2D Dependency Graph

The **Variable Insight** tool window displays a comprehensive, interactive graph showing all direct and indirect dependencies of any variable:

#### Visual Features
- **Color-Coded Nodes**: Different colors for different symbol types
  - Gray: Local variables and parameters
  - Orange: Fields and properties  
  - Teal: Methods
- **Smart Arrows**: Directional edges with arrowheads showing data flow
  - Edges are colored and dimmed to match their target node
  - Selecting a node brightens its connected edges for clarity
- **Interactive Selection**: Click any node to highlight it and its connections
  - Selected nodes have a blue border
  - Connected edges restore full brightness
- **Pan and Zoom**: Navigate large graphs with scrollbars
- **Source Code Display**: Each node shows the actual line of code
- **Dockable Window**: Full Visual Studio tool window that can be docked anywhere

#### Smart Analysis Features

##### 1. Method Dependency Tracing
When methods are called, they appear as dedicated nodes with intelligent analysis:
- **Return Value Analysis**: Traces through return statements to find contributing symbols
- **Parameter Mapping**: Maps parameters back to actual arguments at the call site
- **Smart Filtering**: Only shows variables that actually affect the return value
- **Expression-Bodied Support**: Handles both traditional and `=>` syntax

**Example**: For `int r = Method(a, b, c) + d;`
```
r → Method (the method call)
r → d (direct usage)
Method → returnValue (return contributor)
returnValue → p2 (parameter that affects return)
p2 → b (argument mapping at call site)
```
Note: Arguments inside `Method(a, b, c)` are NOT direct contributors. They only appear if they affect the return value through parameter mapping.

##### 2. Object Initializer Tracing
Directly traces instance members to their object initializer values:
```csharp
var p = new Person() { Name = someName };
string r = p.GetGreetings();
```
Graph shows: `r → GetGreetings → Name → someName` (no intermediate nodes)

When properties are initialized with literals: `Width = 2`
Graph shows: `Width → s` (the object variable where it's initialized)

##### 3. Virtual/Abstract Method Support
Shows all override implementations with labeled edges:
```csharp
Shape s = new Rectangle() { Width = 2, Height = 3 };
double r = s.GetArea();
```
Graph shows:
```
r → Shape.GetArea()
Shape.GetArea() → Rectangle.GetArea() [Override]
Shape.GetArea() → Circle.GetArea() [Override]
Rectangle.GetArea() → Width
Rectangle.GetArea() → Height
Width → s
Height → s
```
**Smart Type Checking**: Only traces instance members to compatible object types. In the example above, `Circle.GetArea() → Radius` appears, but `Radius` doesn't trace to `s` because `s` is actually a `Rectangle`.

## Installation

### From Source
1. Build the extension:
   ```bash
   cd Discernment
   dotnet build
   ```
2. Install the VSIX:
   - Navigate to `bin\Debug\net8.0-windows\Discernment.vsix`
   - Double-click to install
3. Restart Visual Studio 2022 if it's already running

### Requirements
- Visual Studio 2022 version 17.9 or later
- .NET 8.0 SDK

## Usage

### Quick Start
1. **Open** any C# file in Visual Studio 2022
2. **Select** a variable, field, parameter, or property name (or place cursor on it)
3. **Run** the command using one of these methods:
   - **Quick Launch**: Press `Ctrl+Q`, type "Variable Insight", press Enter (fastest!)
   - **Extensions Menu**: `Extensions` → `Variable Insight`
   - **Tools Menu**: `Tools` → `Variable Insight`
4. **Explore** the interactive dependency graph in the tool window
5. **Click** nodes to select them and see their connections highlighted
6. **Pan** the graph using scrollbars to explore all relationships
7. **Dock** the window wherever you prefer (bottom or side panel recommended)

### Graph Navigation
- **Pan**: Use scrollbars or drag to move around large graphs
- **Select Nodes**: Click any node to highlight it with a blue border
- **View Connections**: Selected nodes show brightened edges for their connections
- **Read Code**: Each node displays the actual source code line
- **Follow Flow**: Arrows point from contributors to the variables they affect

## Test Cases

The `Sample\Program.cs` file includes **9 comprehensive test cases** demonstrating all features:

### Test Case 1: Simple Variable Chains
**Try**: `result`, `sum`, `doubled`, `base1`
- Basic variable dependency chains
- Select `result` to see: `result ← sum ← doubled ← base1`

### Test Case 2: Field Mutation Tracking
**Try**: `globalCounter` (at different locations)
- Shows all places where a field is modified
- Tracks assignments, increments, and method calls that mutate fields

### Test Case 3: Parameter Flow Through Methods
**Try**: `processed`, `final`, `input`
- How values flow through method parameters
- Select `final` to see dependencies through multiple method calls

### Test Case 4: Property Dependencies
**Try**: `fullName`, `greeting`, `userName`
- Property and field dependencies across objects
- Select `greeting` to see multiple property dependencies

### Test Case 5: Complex Multi-Level Dependencies
**Try**: `finalScore`, `adjustedScore`, `totalScore`
- Complex dependency trees with multiple levels
- Variables appearing at multiple levels in the graph

### Test Case 6: Collection Operations
**Try**: `sum`, `doubled`, `filtered`, `numbers`
- LINQ chain dependencies
- Demonstrates method chaining analysis

### Test Case 7: Method Parameter Mapping
**Try**: `r` (line 215)
- Smart method analysis with parameter-to-argument mapping
- Shows which parameters affect the return value
- Demonstrates filtering of unused arguments

### Test Case 8: Object Method Calls
**Try**: `r` (line 275)
- Object initializer tracing
- Instance method dependencies
- Direct property-to-initializer-value mapping

### Test Case 9: Virtual/Abstract Methods
**Try**: `r` (line 325)
- Polymorphic method resolution
- Override relationships with labels
- Type-specific instance member tracing

## What to Expect

When you run Variable Insight on a variable, the graph shows:

### Node Information
- **Variable name** (bold, in header)
- **Type** (colored by kind)
- **Source code line** (in gray box)
- **Location** (file:line)

### Edge Information
- **Directional arrows** showing data flow
- **Color coding** matching target node type
- **Special labels** for override relationships
- **Brightness** indicating selection state

### Header Summary
- Root variable name, type, and location
- Total count of affecting elements
- Interactive instructions

## Technical Details

### Architecture
- **Roslyn Analysis**: Semantic model-based backward data flow analysis
- **MVVM Pattern**: Separation of data and presentation
- **Remote UI**: WPF-based out-of-process UI
- **Canvas Rendering**: Custom graph layout with calculated node positions

### Analysis Depth
- Recursion limit: 15 levels deep
- Prevents infinite loops and circular dependencies
- Deduplicates nodes using unique identifiers

### Symbol Support
- ✅ Local variables
- ✅ Parameters
- ✅ Fields (static and instance)
- ✅ Properties (static and instance)
- ✅ Methods (including virtual/abstract/override)
- ✅ Expression-bodied members
- ❌ Classes, interfaces, namespaces (not applicable)

## Limitations

### Known Limitations
1. **Line Navigation**: Due to out-of-process extension limitations, clicking a node doesn't automatically jump to that line. Use the displayed line number with `Ctrl+G` to navigate.
2. **Cross-Document Analysis**: Currently limited to same-document context
3. **Performance**: Very large dependency graphs may take time to calculate and render

### Future Enhancements
- Graph export (image/text)
- Collapsible sub-graphs
- Search/filter within graph
- Custom layout algorithms
- Cross-solution analysis

## Contributing

This is an educational/demonstration project showcasing:
- Visual Studio extensibility with VisualStudio.Extensibility
- Roslyn semantic analysis
- Backward data flow analysis
- WPF Remote UI with MVVM
- Interactive graph visualization

## License

See LICENSE file for details.

## Support

For issues, questions, or suggestions:
- Review the test cases in `Sample\Program.cs`
- Check Visual Studio's Output window for detailed logs
- Test with simpler cases first to isolate issues
