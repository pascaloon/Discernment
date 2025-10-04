# Graph results examples

When a assignment or declaration uses a method, the input parameters aren't counted as direct affectant, they have to be resolved down the chain of affectants from the return statement (so input parameters not contributing to return arent counted)

## Example 1: Method Callsite Mapping

Take for example this code:
```cs
int a = 2;
int b = 3;
int c = 4;
int d = 5;
int r = Method(a, b, c) + c + d;

int Method(int p1, int p2, int p3)
{
    SomeGlobalVariable = p1 * p2 * p3;
    int temp1 = p2 * 4;
    int temp2 = p2 * 5;
    return temp2 * 2;
}
```

The resulting graph should look something like this:
```
r (root) -> Method // Direct usage of the method
r (root) -> c // Direct usage of c for the add
r (root) -> d // Direct usage of d for the add

Method -> temp2 // temp2 is part of the return value
temp2 -> p2 // p2 is part of the return value
p2 -> b // b2 is passed as the corresponding parameter 
```

Note that the folling affectants are discarded:
- `a` is sued in the method but isn't affecting `r` because it doesn't affect the return statement, so it's discarded.
- `c` as parameter is discarded because it doesn't affect the return statement BUT is kept because it is affecting `r`

## Example 2: Handling Objects

When a object is a method parameter, don't consider it an affectant of the method. Rather, let's consider the properties / mehtods / fields used instead.

```cs
int expectedCarWheels = 4;
Car c = new Car() {NumWheels = expectedCarWheels};
Truck t= new Truck() {NumBigWheels = 4};
int r = Method(c, t);

int Method(Car p1, Truck p2)
{
    int temp = p2.NumBigWheels;
    // ...
    return p1.NumWheels;
}

class Car
{
    public int NumWheels {get;set;}
}

class Truck
{
    public int NumBigWheels {get;set;}
}

```

Expected graph:
```
r (root) -> Method
Method -> NumWheels
NumWheels -> expectedCarWheels
```


## Example 3: Handling  `this` as implicit object parameters

When calling a method from an object, consider `this` as a hidden object parameter and handle it like Example 2.

Example:

```cs

var p = new Persion() {Name = "Paul"};
int age = 4;

string r = Person.GetGreetings() + p.GetStaticGreetings() + p.GetConsideredAsStatic(age);


class Person
{
    public string Name {get;set;}
    public string GetGreetings()
    {
        return $"Hi! I'm {Name}.";
    }

    public static string GetStaticGreetings()
    {
        return $"Hi! I'm someone.";
    }

    public string GetConsideredAsStatic(int p1)
    {
        string a = Name;
        return $"Hi! I'm {p1} years old.";
    }
}
```

This is a complexe example, but I would expect the following graph:
```
r (root) -> GetGreetings
r (root) -> GetStaticGreetings
r (root) -> GetConsideredAsStatic

GetGreetings -> Name // Name is used for the return statement
Name -> p // Inline assignment of Name during declaration of p

GetConsideredAsStatic -> p1 // parameter used
p1 -> age // callsite mapping for parameter
```

Some Notes:
- `p` isn't directly affecting `r` only indirectly.


## Example 4: Virtual Methods

We want to show dependency links of a call to all its overrides 

```cs
Shape s = new Rectangle() { Width = 2, Height = 3 };
double r = s.GetArea();

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
}
```

Expected Results:
```
r (root) -> Shape.GetArea()
Shape.GetArea() -> Rectangle.GetArea() // Override dependency
Shape.GetArea() -> Circle.GetArea() // Override dependency

Rectangle.GetArea() -> Width
Rectangle.GetArea() -> Height

Width -> s // inline declaration
Height -> s // inline declaration

Circle.GetArea() -> Radius

```