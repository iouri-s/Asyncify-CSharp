using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Asyncify.Test
{
    [TestClass]
    public class InvocationAnalyzerFixTest : BaseAnalyzerFixTest
    {
        //No diagnostics expected to show up
        [TestMethod]
        public void DoesNotViolateOnCorrectUseOfTap()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        await CallAsync();
    }", EmptyExpectedResults);
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        CallAsync();
    }", EmptyExpectedResults);
        }

        //No diagnostics expected to show up
        [TestMethod]
        public void DoesNotViolateOnNonTapUseWithinLock()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var obj = new object();
        lock(obj)
        {
            var result = CallAsync().Result;
        }
    }", EmptyExpectedResults);
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var obj = new object();
        lock(obj)
        {
            CallAsync();
        }
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void CanFindMethodNotUsingTap()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", GetResultWithLocation(11, 22));
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", @"
    public async Task Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var result = await CallAsync(cancellationToken);
    }", GetResultWithLocation(11, 22));
        }

        [TestMethod]
        public void CanFindViolationInMethodUsingTap()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var temp = await CallAsync();
        var result = CallAsync().Result;
    }", GetResultWithLocation(12, 22));
        }

        [TestMethod]
        public void DoesNotViolateOnMethodsWithOutOrRef()
        {
            VerifyCodeWithReturn(@"
    public void Test(out string test)
    {
        test = string.Empty;
        var result = CallAsync().Result;
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void DoesNotAddAwaitToVoidMethods()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        CallAsync().Wait();
    }", EmptyExpectedResults);
        }
        
        [TestMethod]
        public void CanFindMethodWhenUsingBraces()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", GetResultWithLocation(11, 23));
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", @"
    public async Task Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var result = await CallAsync(cancellationToken);
    }", GetResultWithLocation(11, 23));
        }

        [TestMethod]
        public void NoViolationOnAsyncMethodsWrappedInVoidCall()
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, @"
    public void FirstLevelUp()
    {
        Test().Wait();
    }

    public Task Test()
    {
        return Task.FromResult(0);
    }", string.Empty), EmptyExpectedResults);
        }

        [TestMethod]
        public void FixIsAppliedUpCallTree()
        {
            var oldSource = string.Format(FormatCode, @"
    public int SecondLevelUp()
    {
        return FirstLevelUp();
    }

    public int FirstLevelUp()
    {
        return Test();
    }

    public int Test()
    {
        var test = new AsyncClass();
        return test.CallAsync().Result;
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public async Task<int> SecondLevelUp(CancellationToken cancellationToken = default(CancellationToken))
    {
        return await FirstLevelUp(cancellationToken);
    }

    public async Task<int> FirstLevelUp(CancellationToken cancellationToken = default(CancellationToken))
    {
        return await Test(cancellationToken);
    }

    public async Task<int> Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        return await test.CallAsync(cancellationToken);
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void FixIsAppliedUpCallTreeStopsAtOutRefParams()
        {
            var oldSource = string.Format(FormatCode, @"
    public int SecondLevelUp(out string test)
    {
        test = string.Empty;
        return FirstLevelUp();
    }

    public int FirstLevelUp()
    {
        return Test();
    }

    public int Test()
    {
        var test = new AsyncClass();
        return test.CallAsync().Result;
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public int SecondLevelUp(out string test)
    {
        test = string.Empty;
        return FirstLevelUp().Result;
    }

    public async Task<int> FirstLevelUp(CancellationToken cancellationToken = default(CancellationToken))
    {
        return await Test(cancellationToken);
    }

    public async Task<int> Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        return await test.CallAsync(cancellationToken);
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void TestCodeFixWithReturnType()
        {
            var oldSource = string.Format(FormatCode, @"
    public int Test()
    {
        var test = new AsyncClass();
        return test.CallAsync().Result;
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public async Task<int> Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        return await test.CallAsync(cancellationToken);
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void TestCodeFixWithInstanceCall()
        {
            var oldSource = string.Format(FormatCode, @"
    public void Test()
    {
        var test = new AsyncClass();
        var result = test.CallAsync().Result;
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public async Task Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        var result = await test.CallAsync(cancellationToken);
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        // [damancia] - Looks like a regression, we currently are trying to convert FromResult->FromResultAsync
        [TestMethod]
        public void TestCodeFixWithinLambda()
        {
            var oldSource = string.Format(FormatCode, @"
    public void Test()
    {
        int[] bla = null;
        bla.Select(x => Task.FromResult(100).Result);
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public void Test()
    {
        int[] bla = null;
        bla.Select(async x => await Task.FromResult(100));
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        // [damancia] - Not sure about this failure.
        [TestMethod]
        public void TestCodeFixWithinParenthesizedLambda()
        {
            var oldSource = string.Format(FormatCode, @"
    public void Test()
    {
        System.Action a = () => CallAsync();
    }

    public int CallAsync()
    {
        return Task.FromResult(0).Result;
    }
    ", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public void Test()
    {
        System.Action a = async () => await CallAsync();
    }

    public async Task<int> CallAsync()
    {
        return await Task.FromResult(0);
    }
    ", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void FixWillWrapInParenthesesIfNeeded()
        {
            var oldSource = string.Format(FormatCode, @"
    public void Test()
    {
        var test = new AsyncClass();
        var result = test.CallAsync().Result.ToString();
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public async Task Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        var result = (await test.CallAsync(cancellationToken)).ToString();
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void WillAddAsyncToVoidMethodInCodefix()
        {
            var oldSource = string.Format(FormatCode, @"
    public void VoidCallingMethod()
    {
        Test();
    }

    public void Test()
    {
        var test = new AsyncClass();
        var result = test.CallAsync().Result;
    }", string.Empty);
            var newSource = string.Format(FormatCode, @"
    public async Task VoidCallingMethod(CancellationToken cancellationToken = default(CancellationToken))
    {
        await Test(cancellationToken);
    }

    public async Task Test(CancellationToken cancellationToken = default(CancellationToken))
    {
        var test = new AsyncClass();
        var result = await test.CallAsync(cancellationToken);
    }", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }
        
        // [damancia] - Regression, not sure what the correct behavior here should be
        [TestMethod]
        public void TestRefactoringOverInterfaces()
        {
            VerifyCSharpFix(@"
using System.Threading;
using System.Threading.Tasks;

public class ConsumingClass
{
    public int Test(IInterface i)
    {
        return i.Call();
    }
}

public interface IInterface
{
    int Call();
}


public class DerivedClass : IInterface
{
    public int Call()
    {
        return AsyncMethod().Result;
    }

    public Task<int> AsyncMethod(CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.FromResult(0);
    }
}
", @"
using System.Threading;
using System.Threading.Tasks;

public class ConsumingClass
{
    public async Task<int> Test(IInterface i)
    {
        return await i.Call();
    }
}

public interface IInterface
{
    Task<int> Call(CancellationToken cancellationToken = default(CancellationToken)));
}


public class DerivedClass : IInterface
{
    public async Task<int> Call(CancellationToken cancellationToken = default(CancellationToken))
    {
        return await AsyncMethod(cancellationToken);
    }

    public Task<int> AsyncMethod(CancellationToken cancellationToken = default(CancellationToken))
    {
        return Task.FromResult(0);
    }
}
");
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new InvocationFixProvider();
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new InvocationAnalyzer();
        }

        public override string DiagnosticId => InvocationAnalyzer.DiagnosticId;
    }
}