# Requirements on the code of unit tests
* Every unit test shall have a comment that references the corresponding requirement ID from the user requirements, when possible.
* Every unit test shall be be declared as `[TestMethod] public void func()`. That is, the `[TestMetod]` attribite shall be on the same line as the function declaration.
* Also, [TestClass] and [TestInitialize] shall be on the same line.
* Use file-scoped namespace declarations to minimize nesting.
* Test classes that inherit from MainViewModelTestBase will use the ImmediateSynchronizationContext. That means that there is no need for the construction "_testContext.Send(_ => { }, null);".
* Use the comments "Arrange", "Act" and "Assert" to describe the steps in each [TestMethod].