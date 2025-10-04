using System;

namespace Sample
{
    public class TestVariable
    {
        private int counter;

        public void TestMethod()
        {
            // Test case 1: Simple variable assignment chain
            int value1 = 10;
            int value2 = value1 * 2;
            int result = value2 + 5;

            Console.WriteLine(result);

            // Test case 2: Field mutation
            counter = 0;
            IncrementCounter();
            counter += 10;
            
            // Test case 3: Parameter flow
            ProcessData(counter);
        }

        private void IncrementCounter()
        {
            counter++;
        }

        private void ProcessData(int input)
        {
            int processed = input * 2;
            Console.WriteLine($"Processed: {processed}");
        }

        public void ComplexExample()
        {
            // Test case 4: More complex dependency chain
            string name = "Test";
            string fullName = BuildFullName(name, "User");
            LogMessage(fullName);
        }

        private string BuildFullName(string first, string last)
        {
            return $"{first} {last}";
        }

        private void LogMessage(string message)
        {
            Console.WriteLine($"Log: {message}");
        }
    }
}

