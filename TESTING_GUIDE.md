# Testing Guide for Variable Insight Extension

## Installation

1. **Build** the extension:
   ```bash
   cd Discernment
   dotnet build
   ```
2. **Install** the VSIX:
   - Double-click `Discernment\bin\Debug\net8.0-windows\Discernment.vsix`
3. **Restart** Visual Studio 2022 if it's already running

## How to Use

### Basic Workflow
1. **Open** the test file: `Sample\Program.cs`
2. **Select** a variable name (or place cursor on it)
3. **Run** the command using one of these methods:
   - **Quick Launch**: Press `Ctrl+Q`, type "Variable Insight", Enter ⚡ (fastest!)
   - **Extensions Menu**: `Extensions` → `Variable Insight`
   - **Tools Menu**: `Tools` → `Variable Insight`
4. **Explore** the interactive 2D dependency graph

### Interacting with the Graph
- **Pan**: Use scrollbars to navigate around the graph
- **Select Nodes**: Click any node to select it (blue border appears)
- **View Connections**: Selected nodes show brightened edges
- **Read Code**: Each node displays the actual source code line
- **Follow Arrows**: Directional edges show data flow from contributors
- **Observe Colors**:
  - Gray nodes: Variables and parameters
  - Orange nodes: Fields and properties
  - Teal nodes: Methods
  - Dimmed edges: Unselected connections
  - Bright edges: Connected to selected node

### Understanding the Display

#### Node Structure
```
┌─────────────────────────────┐
│ [Name]              [Color] │ ← Header (color-coded by type)
├─────────────────────────────┤
│ Type: [type info]           │
│ ┌────────────────────────┐  │
│ │ [source code line]     │  │ ← Actual code
│ └────────────────────────┘  │
│ Location: [file:line]       │
└─────────────────────────────┘
```

#### Edge Features
- **Arrows**: Point from contributor to affected variable
- **Colors**: Match target node kind (but dimmer)
- **Labels**: Special relationships (e.g., "Override")
- **Highlighting**: Brighten when node is selected

#### Header Information
- **Variable**: Name of the analyzed variable
- **Type**: Full type information
- **Location**: File and line number
- **Total Affecting Elements**: Count of dependencies

## Test Cases

### Test Case 1: Simple Variable Chains

**Location**: Line 51  
**Variable to Select**: `result`

```csharp
int base1 = 10;
int base2 = 20;
int doubled = base1 * 2;
int sum = doubled + base2;
int result = sum + 5;  // ← Select 'result' here
```

**Expected Graph**:
```
result (Level 0)
  ↓
sum (Level 1)
  ↓           ↘
doubled    base2 (Level 2)
  ↓
base1 (Level 3)
```

**What to Observe**:
- Linear chain from `result` back to `base1`
- Branching at `sum` to show both `doubled` and `base2`
- Each node shows its assignment line

---

### Test Case 2: Field Mutation Tracking

**Location**: Line 75  
**Variable to Select**: `globalCounter`

```csharp
globalCounter = 0;  // ← Select here
IncrementCounter();
globalCounter += 10;
ModifyCounterBy(5);
```

**Expected Graph**:
- Shows all places where `globalCounter` is modified
- Method calls that mutate the field
- Parameter `delta` from `ModifyCounterBy` affecting the counter

**What to Observe**:
- Multiple paths affecting the same variable
- Method nodes for `IncrementCounter` and `ModifyCounterBy`
- Field mutation tracking across method boundaries

---

### Test Case 3: Parameter Flow Through Methods

**Location**: Line 103  
**Variable to Select**: `final`

```csharp
int value = 100;
int processed = ProcessValue(value);
int final = ProcessValue(processed) + 10;  // ← Select 'final' here
```

**Expected Graph**:
```
final
  ↓
ProcessValue (method node)
  ↓
input (parameter)
  ↓
processed
  ↓
ProcessValue (method node)
  ↓
input (parameter)
  ↓
value
```

**What to Observe**:
- Method nodes showing parameter-to-argument mapping
- Chained method calls with proper flow
- Gray color for parameters

---

### Test Case 4: Property Dependencies

**Location**: Line 132  
**Variable to Select**: `greeting`

```csharp
var user = new User { FirstName = "John", LastName = "Doe" };
string fullName = user.GetFullName();
string greeting = $"Hello, {fullName}! Logged in as: {userName}";  // ← Select here
```

**Expected Graph**:
- `greeting` depends on `fullName` and `userName` field
- `fullName` depends on `GetFullName` method
- Method depends on `FirstName` and `LastName` properties
- Orange color for properties and fields

**What to Observe**:
- Property nodes in orange
- Method node in teal
- Multiple dependencies at the root level

---

### Test Case 5: Complex Multi-Level Dependencies

**Location**: Line 160  
**Variable to Select**: `finalScore`

```csharp
int baseScore = 100;
int bonus = 50;
double multiplier = 1.5;
int totalScore = baseScore + bonus;
double adjustedScore = totalScore * multiplier;
double finalScore = adjustedScore + (bonus * 0.5);  // ← Select here
```

**Expected Graph**:
```
finalScore
  ↓                  ↘
adjustedScore      bonus
  ↓             ↘
totalScore   multiplier
  ↓        ↘
baseScore  bonus
```

**What to Observe**:
- `bonus` appears at TWO different levels
- Complex branching structure
- Multiple contributing factors at each level

---

### Test Case 6: Collection Operations

**Location**: Line 181  
**Variable to Select**: `sum`

```csharp
var numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
var filtered = numbers.Where(n => n > 5).ToList();
var doubled = filtered.Select(n => n * 2).ToList();
int sum = doubled.Sum();  // ← Select 'sum' here
```

**Expected Graph**:
- Shows LINQ chain: `sum → doubled → filtered → numbers`
- Method nodes for `.Sum()`, `.Select()`, `.Where()`
- Collection dependencies

**What to Observe**:
- Method chaining properly traced
- Each LINQ operation as a separate node

---

### Test Case 7: Method Parameter Mapping

**Location**: Line 215  
**Variable to Select**: `r`

```csharp
int a = 2;
int b = 3;
int c = 4;
int d = 5;
int r = Method(a, b, c) + c + d;  // ← Select 'r' here
```

**Expected Graph**:
```
r
├─→ Method
│   └─→ temp2
│       └─→ someGlobalVariable
│           └─→ p1, p2, p3
│               └─→ a, b, c (at call site)
├─→ c (direct use)
└─→ d (direct use)
```

**What to Observe**:
- `c` and `d` appear as direct dependencies (used outside method)
- `a`, `b`, `c` do NOT appear as direct dependencies (they're inside `Method(...)`)
- Method internals traced through return value analysis
- Only symbols affecting return value are shown

---

### Test Case 8: Object Method Calls

**Location**: Line 275  
**Variable to Select**: `r`

```csharp
string someName = "Paul";
var p = new Person() { Name = someName };
int age = 4;
string r = p.GetGreetings() + Person.GetStaticGreetings() + p.GetConsideredAsStatic(age);
```

**Expected Graph**:
```
r
├─→ GetGreetings
│   └─→ Name
│       └─→ someName
├─→ GetStaticGreetings (no dependencies - static)
└─→ GetConsideredAsStatic
    └─→ p1
        └─→ age
```

**What to Observe**:
- Direct tracing from `Name` to `someName` (no intermediate `this` node)
- Static method has no dependencies
- Parameter mapping: `p1 → age`
- `p` is NOT a direct contributor to `r`

---

### Test Case 9: Virtual/Abstract Methods ⭐ Advanced

**Location**: Line 325  
**Variable to Select**: `r`

```csharp
Shape s = new Rectangle() { Width = 2, Height = 3 };
double r = s.GetArea();  // ← Select 'r' here
```

**Expected Graph**:
```
r
└─→ Shape.GetArea()
    ├─→ Rectangle.GetArea() [Override] ← Label visible
    │   ├─→ Width
    │   │   └─→ s
    │   └─→ Height
    │       └─→ s
    └─→ Circle.GetArea() [Override] ← Label visible
        └─→ Radius (leaf node - no trace to s)
```

**What to Observe**:
- **Override Labels**: Edges to override implementations show "Override" label
- **All Overrides Shown**: Both `Rectangle.GetArea()` and `Circle.GetArea()` appear
- **Type-Specific Tracing**: 
  - `Width` and `Height` trace to `s` (because `s` is a Rectangle)
  - `Radius` does NOT trace to `s` (because `s` is not a Circle)
- **Literal Initialization**: `Width → s` and `Height → s` show where they're initialized

**Key Insight**: The analyzer shows ALL possible override paths, but only traces instance members to compatible object types!

---

## Advanced Testing Techniques

### Test with Custom Code
1. Open any of your C# projects
2. Find a complex variable with multiple dependencies
3. Select it and run Variable Insight
4. Explore the graph to understand data flow

### Performance Testing
- Test with deeply nested dependencies (15+ levels)
- Test with wide graphs (many siblings)
- Test with large files (500+ lines)

### Edge Cases
- **Circular dependencies**: Analyzer stops at recursion limit
- **Self-referencing**: Variables that reference themselves
- **Multiple assignments**: Variables assigned multiple times
- **Conditional assignments**: Variables in if/else blocks

## Tips for Best Results

### Good Variables to Analyze
✅ Local variables with clear dependencies  
✅ Variables computed from multiple sources  
✅ Method return values  
✅ Fields modified in multiple places  
✅ Parameters passed through method chains  

### Less Useful to Analyze
❌ Constants (no dependencies)  
❌ Variables never assigned after declaration  
❌ Method names or class names  
❌ Loop iterators with complex control flow  

## Troubleshooting

### "No active editor window"
**Solution**: Make sure a C# file is open and has focus

### "No text selected or cursor position invalid"
**Solution**: Place cursor directly on a variable name, or select it

### "Could not analyze the selected symbol"
**Solution**: Ensure you selected a variable, field, parameter, or property (not a method, class, or namespace)

### "This extension only works with C# files"
**Solution**: Open a `.cs` file (not `.txt`, `.json`, etc.)

### Graph is too large to navigate
**Solutions**:
- Use scrollbars to pan around
- Dock the window to give it more space
- Focus on specific branches of interest

### Edges are hard to see
**Solutions**:
- Click nodes to highlight their connections
- Look for the arrow heads to understand direction
- Use the color coding to match edges to target nodes

## Performance Tips

- First analysis on a file may be slower (semantic model building)
- Subsequent analyses on the same file are faster
- Very deep recursion (15+ levels) takes longer
- Wide graphs (many siblings) render faster than deep ones

## Feedback and Debugging

- Error messages appear in dialogs
- Check Visual Studio's Output window for detailed logs
- Test with simpler cases first to isolate issues
- Use the test cases in `Sample\Program.cs` as references

## Quick Reference

| Action | Result |
|--------|--------|
| Click node | Select node, highlight edges |
| Gray node | Variable or parameter |
| Orange node | Field or property |
| Teal node | Method |
| Dimmed edge | Not connected to selection |
| Bright edge | Connected to selected node |
| "Override" label | Virtual/abstract method override |
| Arrow direction | Data flow (contributor → affected) |
