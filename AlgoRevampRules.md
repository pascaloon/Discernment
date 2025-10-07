# Variable Insight Graph Algorithm Revamp Proposal

Given a selected varaible/class field/class property, the algorithm needs to generate the graph of all variables/fields/properties/methods that affect the selected variable, named "affectants".

To do so, here are the steps:
1. The user selects the variable/field/property to analyze. This becomes the "conernced variable" for the rest of this recusive algorithm.
2. The algo finds all the references to the conernced variable in the code (including the declaration)
3. For each reference, the algo finds all the variables/fields that are part of that statement/expression and do the same thing recursively for them (new concerned variables are created for each of them). However, properties and methods are a bit different.
4. Methods and properties affectants are handled differently. They are added as affectants to the graph, but we also need to dig down inside the method body to find what variables/fields/properties/methods are considered affectants nodes to add. To do so, we need to distiguish how the method affect the concerned variable. If the concerned variable was affected by the return value of the method, we need to find all variables/fields/properties/methods that will affect the return statement. Each of them will become a new concerned variable to analyze recursively. If the concerned variable was passed in as a parameter and the variable is an object (not a primitive type), we need to find all variables/fields/properties/methods that will affect the parameter. Each of them will become a new concerned variable to analyze recursively. We also consider an implicit affectant link between a given method parameter and the variable used at the call site.
5. If methods are virtual or abstract, we need to find all the override methods and add them as affectants to the graph. (they are considered as affectant nodes to the base method).
6. For Method invocation at call site, we do not consider do not consider the parameter (or owning object if object method call) as affectants unless the paramter is linked as concerned variable when analyzing the method body (see step 4).
7. The algorithm continues recursively until all concerned variables are analyzed.
8. The algorithm returns the graph of all concerned variables and their affectants.
9. the edges between the nodes are labeled with the type of relation between the nodes.
10. If the concerned variable is a parameter of a method and that method hasn't been listed as a node before it means that the affectants aren't bound to a specific call of that method, therefore, we most go through all references of that method, map the parameter at the call site and then considered all passed parameters as concerned variables too.

All the examples listed in GraphResultsExamples.md are still valid and should be the source of the unit tests.