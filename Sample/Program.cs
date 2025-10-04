using System;
using System.Collections.Generic;
using System.Linq;

namespace Sample
{
    /// <summary>
    /// Test cases for Variable Insight extension.
    /// To test: Select a variable name and run "Variable Insight" from Extensions menu.
    /// </summary>
    class Program
    {
        // Fields for testing
        private static int globalCounter = 0;
        private static string userName = "DefaultUser";
        private static int SomeGlobalVariable = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("=== Variable Insight Test Cases ===\n");

            TestCase1_SimpleChain();
            TestCase2_FieldMutation();
            TestCase3_ParameterFlow();
            TestCase4_PropertyChain();
            TestCase5_ComplexDependencies();
            TestCase6_CollectionOperations();
            TestCase7_MethodParameterMapping();
            TestCase8_ObjectMethodCalls();
        }

        /// <summary>
        /// TEST CASE 1: Simple variable dependency chain
        /// Try selecting: result, sum, doubled, or base1
        /// Expected: Shows the chain of dependencies
        /// </summary>
        static void TestCase1_SimpleChain()
        {
            Console.WriteLine("Test Case 1: Simple Chain");
            
            int base1 = 10;
            int base2 = 20;
            
            // Select 'doubled' to see it depends on base1
            int doubled = base1 * 2;
            
            // Select 'sum' to see it depends on doubled and base2
            int sum = doubled + base2;
            
            // Select 'result' to see the full chain: result <- sum <- doubled <- base1
            int result = sum + 5;
            
            Console.WriteLine($"Result: {result}\n");
        }

        /// <summary>
        /// TEST CASE 2: Field mutation and tracking
        /// Try selecting: globalCounter (at different locations)
        /// Expected: Shows all assignments and method calls affecting the field
        /// </summary>
        static void TestCase2_FieldMutation()
        {
            Console.WriteLine("Test Case 2: Field Mutation");
            
            // Select 'globalCounter' here to see all the places it's affected
            globalCounter = 0;
            
            IncrementCounter();
            IncrementCounter();
            
            globalCounter += 10;
            
            ModifyCounterBy(5);
            
            Console.WriteLine($"Counter: {globalCounter}\n");
        }

        static void IncrementCounter()
        {
            globalCounter++;
        }

        static void ModifyCounterBy(int delta)
        {
            globalCounter += delta;
        }

        /// <summary>
        /// TEST CASE 3: Parameter flow through methods
        /// Try selecting: processed, input, or value
        /// Expected: Shows how values flow through parameters
        /// </summary>
        static void TestCase3_ParameterFlow()
        {
            Console.WriteLine("Test Case 3: Parameter Flow");
            
            int value = 100;
            
            // Select 'processed' to see it depends on value through ProcessValue
            int processed = ProcessValue(value);
            
            // Select 'final' to see the complete flow
            int final = ProcessValue(processed) + 10;
            
            Console.WriteLine($"Final: {final}\n");
        }

        static int ProcessValue(int input)
        {
            // Select 'result' here to see it depends on 'input' parameter
            int result = input * 2;
            return result;
        }

        /// <summary>
        /// TEST CASE 4: Property dependencies
        /// Try selecting: fullName, greeting, or userName
        /// Expected: Shows property and field dependencies
        /// </summary>
        static void TestCase4_PropertyChain()
        {
            Console.WriteLine("Test Case 4: Property Chain");
            
            var user = new User { FirstName = "John", LastName = "Doe" };
            
            // Select 'fullName' to see it depends on FirstName and LastName
            string fullName = user.GetFullName();
            
            // Select 'greeting' to see it depends on fullName and userName field
            string greeting = $"Hello, {fullName}! Logged in as: {userName}";
            
            Console.WriteLine(greeting + "\n");
        }

        /// <summary>
        /// TEST CASE 5: Complex multi-level dependencies
        /// Try selecting: finalScore, adjustedScore, or totalScore
        /// Expected: Shows complex dependency tree
        /// </summary>
        static void TestCase5_ComplexDependencies()
        {
            Console.WriteLine("Test Case 5: Complex Dependencies");
            
            int baseScore = 100;
            int bonus = 50;
            double multiplier = 1.5;
            
            // First level dependency
            int totalScore = baseScore + bonus;
            
            // Second level: depends on totalScore
            double adjustedScore = totalScore * multiplier;
            
            // Third level: depends on adjustedScore and bonus
            double finalScore = adjustedScore + (bonus * 0.5);
            
            // Select 'finalScore' to see: finalScore <- adjustedScore <- totalScore <- baseScore, bonus
            // It should also show bonus is used directly in finalScore
            
            Console.WriteLine($"Final Score: {finalScore}\n");
        }

        /// <summary>
        /// TEST CASE 6: Collection operations
        /// Try selecting: sum, filtered, doubled, or numbers
        /// Expected: Shows LINQ chain dependencies
        /// </summary>
        static void TestCase6_CollectionOperations()
        {
            Console.WriteLine("Test Case 6: Collection Operations");
            
            var numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            
            // Select 'filtered' to see it depends on numbers
            var filtered = numbers.Where(n => n > 5).ToList();
            
            // Select 'doubled' to see it depends on filtered
            var doubled = filtered.Select(n => n * 2).ToList();
            
            // Select 'sum' to see the full chain
            int sum = doubled.Sum();
            
            Console.WriteLine($"Sum: {sum}\n");
        }

        /// <summary>
        /// TEST CASE 7: Method parameter mapping and return value analysis
        /// Try selecting: r
        /// Expected graph:
        ///   r → Method (method call itself)
        ///   r → c (direct usage OUTSIDE method - in the addition)
        ///   r → d (direct usage OUTSIDE method - in the addition)
        ///   Method → temp2 (return contributor)
        ///   temp2 → p2 (parameter used in return)
        ///   p2 → b (argument mapping at call site)
        /// 
        /// Important: Arguments INSIDE method call (a, b, c in Method(...)) are NOT direct contributors!
        /// They only appear if they affect the return value (through parameter mapping).
        /// </summary>
        static void TestCase7_MethodParameterMapping()
        {
            Console.WriteLine("Test Case 7: Method Parameter Mapping");
            
            int a = 2;
            int b = 3;
            int c = 4;
            int d = 5;
            
            // Select 'r' to see:
            // - Direct: Method (method itself), c (in + c), d (in + d)
            // - NOT Direct: a, b (inside method arguments - resolved through parameter mapping)
            // - Method path: Method -> temp2 -> p2 -> b
            // - Discarded: a (doesn't affect return), c as param p3 (doesn't affect return)
            int r = Method(a, b, c) + c + d;
            
            Console.WriteLine($"Result r: {r}\n");
        }

        static int Method(int p1, int p2, int p3)
        {
            // This assignment doesn't affect return value - should be filtered out
            SomeGlobalVariable = p1 * p2 * p3;
            
            // temp1 doesn't affect return - should be filtered out
            int temp1 = p2 * 4;
            
            // temp2 IS part of return - should be in graph
            int temp2 = SomeGlobalVariable * 5;
            
            // Only temp2 (and therefore p2) affects the return value
            return temp2 * 2;
        }

        /// <summary>
        /// Helper class for testing property dependencies
        /// </summary>
        class User
        {
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            
            public string GetFullName()
            {
                // Select 'fullName' to see it depends on FirstName and LastName properties
                string fullName = $"{FirstName} {LastName}";
                return fullName;
            }
        }


        /// <summary>
        /// Test Case 8: Object method calls with object initializers
        /// Demonstrates how instance members trace directly to object initializer values.
        /// Expected graph for 'r':
        /// - GetGreetings -> Name -> someName (direct trace to initializer value, no intermediate 'this' node)
        /// - GetStaticGreetings (no dependencies, it's static)
        /// - GetConsideredAsStatic -> p1 -> age (p1 is used in return statement)
        /// Note: 'p' is NOT a direct contributor to 'r', only appears through Name initializer
        /// </summary>
        static void TestCase8_ObjectMethodCalls()
        {
            TestCase8_ObjectMethodCalls_unused();
            string someName = "Paul";
            var p = new Person() { Name = someName };
            int age = 4;

            // Select 'r' to see: GetGreetings -> Name -> someName (simplified, no 'this' node)
            string r = p.GetGreetings() + Person.GetStaticGreetings() + p.GetConsideredAsStatic(age);
        }

        static void TestCase8_ObjectMethodCalls_unused()
        {
            string someName2 = "Paul";
            var p = new Person() { Name = someName2 };
        }

        class Person
        {
            public string Name { get; set; } = "";

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
    } // End of Program class
}
