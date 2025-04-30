# Requirements on the code of unit tests
* Every unit test shall have a comment that references the corresponding requirement ID from the user requirements, when possible.
* Every unit test shall be be declared as `[TestMethod] public void func()`. That is, the `[TestMetod]` attribite shall be on the same line as the function declaration.
* Use file-scoped namespace declarations to minimize nesting.
