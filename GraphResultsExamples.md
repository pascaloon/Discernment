# Graph results examples

When a assignment or declaration uses a method, the input parameters aren't counted as direct affectant, they have to be resolved down the chain of affectants from the return statement (so input parameters not contributing to return arent counted)

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