﻿using RazorEngine.Compilation.ImpromptuInterface;
using NUnit.Framework;
using RazorEngine.Compilation;
using RazorEngine.Compilation.ReferenceResolver;
using RazorEngine.Configuration;
using RazorEngine.Templating;
using RazorEngine.Tests.TestTypes;
using RazorEngine.Text;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Test.RazorEngine.TestTypes.BaseTypes;

namespace Test.RazorEngine
{
    [TestFixture]
    public class RazorEngineServiceTestFixture
    {
        public static void RunTestHelper(Action<IRazorEngineService> test, Action<TemplateServiceConfiguration> withConfig = null)
        {
            if (withConfig == null)
            {
                withConfig = (config) => { config.Debug = true; };
            }
            try
            {
                var config = new TemplateServiceConfiguration();
                withConfig(config);
                using (var service = RazorEngineService.Create(config))
                {
                    test(service);
                }
            }
            catch (TemplateCompilationException e)
            {
                var source = e.CompilationData.SourceCode;
                Console.WriteLine("Generated source file: \n\n{0}", source ?? "SOURCE CODE NOT AVAILABLE!");
                e.CompilationData.DeleteAll();
                throw;
            }
        }

        public interface IMyInterface
        {
            string Test();
        }
        public class MyClass : IMyInterface
        {
            public string Test()
            {
                return "test";
            }
            public string More()
            {
                return "more";
            }
        }

        /// <summary>
        /// Test that DynamicActLike also gives access to all previous method.
        /// This is a remainder that we changed the Impromptu code and make 
        /// ActLikeProxy inherit from ImpromptuForwarder (and setting the Target property).
        /// If this test ever fails make sure to fix that because we need this behavior.
        /// </summary>
        [Test]
        public void RazorEngineService_ActLikeTest()
        {
            dynamic m = new ExpandoObject();
            m.Test = new Func<string>(() => "mytest");
            m.More = new Func<string>(() => "mymore");
            dynamic _m = Impromptu.DynamicActLike(m, typeof(IMyInterface));
            Assert.AreEqual("mytest", _m.Test());
            var __m = (IMyInterface)_m;
            Assert.AreEqual("mytest", __m.Test());


            dynamic o = new MyClass();
            dynamic _o = Impromptu.DynamicActLike(o, typeof(IMyInterface));
            Assert.AreEqual("test", _o.Test());
            var __o = (IMyInterface)_o;
            Assert.AreEqual("test", __o.Test());

            Assert.AreEqual("more", _o.More());
            Assert.AreEqual("mymore", _m.More());
        }

        /// <summary>
        /// Tests that the fluent configuration can configure a template service with a specific encoding.
        /// </summary>
        [Test]
        public void RazorEngineService_DynamicIEnumerable()
        {
            RunTestHelper(service =>
            {
                const string template = @"@Enumerable.Count(Model.Data)";
                const string expected = "3";
                var anonArray = new[] { new { InnerData = 1 }, new { InnerData = 2 }, new { InnerData = 3 } };
                var model = new { Data = anonArray.Select(a => a) };
                string result = service.RunCompile(template, "test", null, model, null);

                Assert.That(result == expected, "Result does not match expected: " + result);
            });
        }

        /// <summary>
        /// Test that anonymous types within the template work.
        /// </summary>
        [Test]
        public void RazorEngineService_AnonymousTypeWithinTemplate_2()
        {
            RunTestHelper(service =>
            {
                const string template_child = "@Enumerable.Count(Model.Data)";
                const string template_parent = @"@Include(""Child"", new { Data = Model.Animals})";
                const string expected = "3";
                service.Compile(template_child, "Child", null);
                var anonArray = new[] { new Animal { Type = "1" }, new Animal { Type = "2" }, new Animal { Type = "3" } };
                var model = new AnimalViewModel { Animals = anonArray };
                string result = service.RunCompile(template_parent, "test", typeof(AnimalViewModel), model, null);

                Assert.That(result == expected, "Result does not match expected: " + result);
            });
        }

        /// <summary>
        /// Test that anonymous types within the template work.
        /// </summary>
        [Test]
        public void RazorEngineService_AnonymousTypeWithinTemplate()
        {
            RunTestHelper(service =>
            {
                const string template_child = "@Enumerable.Count(Model.Data)";
                const string template_parent = @"@Include(""Child"", new { Data = Model.Data})";
                const string expected = "3";
                service.Compile(template_child, "Child", null);
                var anonArray = new[] { new { InnerData = 1 }, new { InnerData = 2 }, new { InnerData = 3 } };
                var model = new { Data = anonArray.Select(a => a) };
                string result = service.RunCompile(template_parent, "test", null, model, null);

                Assert.That(result == expected, "Result does not match expected: " + result);
            });
        }

        /// <summary>
        /// Tests that the fluent configuration can configure a template service with a specific encoding.
        /// </summary>
        [Test]
        public void RazorEngineService_WithSpecificEncoding()
        {
            RunTestHelper(service =>
            {
                const string template = "<h1>Hello @Model.String</h1>";
                const string expected = "<h1>Hello Matt & World</h1>";

                var model = new { String = "Matt & World" };
                string result = service.RunCompile(template, "test", null, model, null);

                Assert.That(result == expected, "Result does not match expected: " + result);
            }, (c) => c.EncodedStringFactory = new RawStringFactory());
        }

        /// <summary>
        /// Tests that a simple template with an iterator model can be parsed.
        /// </summary>
        [Test]
        public void RazorEngineService_GetInformativeErrorMessage()
        {
            RunTestHelper(service =>
            {
                const string template = "@foreach (var i in Model.Unknown) { @i }";
                var exn = Assert.Throws<TemplateCompilationException>(() =>
                {
                    string result = service.RunCompile(template, "test", typeof(object), new object());
                });
                exn.CompilationData.DeleteAll();
                var msg = exn.Message;
                var errorMessage = 
                    string.Format(
                        "An expected substring ({{0}}) was not found in: {0}",
                        msg.Replace("{", "{{").Replace("}", "}}"));
                
                // Compiler error
                Assert.IsTrue(
                    msg.Contains("does not contain a definition for"),
                    string.Format(errorMessage, "compiler error"));
                // Template
                Assert.IsTrue(msg.Contains(template),
                    string.Format(errorMessage, "template"));
                // Temp files
                Assert.IsTrue(msg.Contains("Temporary files of the compilation can be found"),
                    string.Format(errorMessage, "temp files"));
                // C# source code
                Assert.IsTrue(msg.Contains("namespace " + CompilerServiceBase.DynamicTemplateNamespace),
                    string.Format(errorMessage, "C# source"));
            });
        }

        /// <summary>
        /// Tests that a simple template with an iterator model can be parsed.
        /// </summary>
        [Test]
        public void RazorEngineService_GetInformativeRuntimeErrorMessage()
        {
            RunTestHelper(service =>
            {
                const string template = "@foreach (var i in Model.Unknown) { @i }";
                string file = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(file, template);
                    var source = new LoadedTemplateSource(template, file);
                    var exn = Assert.Throws<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() =>
                    {
                        string result = service.RunCompile(source, "test", null, new object());
                    });
                    // We now have a reference to our template in the stacktrace
                    var stack = exn.StackTrace.ToLowerInvariant();
                    var expected = file.ToLowerInvariant();
                    Assert.IsTrue(
                        stack.Contains(expected),
                        "Could not find reference to template (" + expected + ") in stacktrace: \n" + 
                        stack);
                }
                finally
                {
                    File.Delete(file);
                }
            });
        }


        class TestHelperReferenceResolver : IReferenceResolver
        {
            public IEnumerable<CompilerReference> GetReferences
            (
                TypeContext context,
                IEnumerable<CompilerReference> includeAssemblies = null
            )
            {
                // We need to return this standard set or even simple views blow up on
                // a missing reference to System.Linq.
                var loadedAssemblies = (new UseCurrentAssembliesReferenceResolver()).GetReferences(null);
                foreach (var reference in loadedAssemblies)
                    yield return reference;
                yield return CompilerReference.From("test/TestHelper.dll");
            }
        }

        /// <summary>
        /// Tests that we can use types from other assemblies in templates.
        /// </summary>
        [Test]
        public void RazorEngineService_CheckThatWeCanUseUnknownTypes()
        {
            RunTestHelper(service =>
            {
                var assembly = Assembly.LoadFrom("test/TestHelper.dll");
                Type modelType = assembly.GetType("TestHelper.TestClass", true);
                var model = Activator.CreateInstance(modelType);
                var template = @"
@{
    var t = new TestHelper.TestClass();
}
@t.TestProperty";
                string compiled = service.RunCompile(template, Guid.NewGuid().ToString(), modelType, model);
                
            }, config =>
            {
                config.ReferenceResolver = new TestHelperReferenceResolver();
            });
        }


        /// <summary>
        /// Tests that we can use types from other assemblies in templates.
        /// Even when the type can be loaded.
        /// </summary>
        [Test]
        public void RazorEngineService_CheckThatWeCanUseUnknownTypesAtExecuteTime()
        {
            RunTestHelper(service =>
            {
                var template = @"
@{
    var t = new TestHelper.TestClass();
}
@t.TestProperty";
                string compiled = service.RunCompile(template, Guid.NewGuid().ToString());

            }, config =>
            {
                config.ReferenceResolver = new TestHelperReferenceResolver();
            });
        }

        /// <summary>
        /// Tests that we fail with the right exception
        /// </summary>
        [Test]
        public void RazorEngineService_CheckParsingFails()
        {
            RunTestHelper(service =>
            {
                // Tag must be closed!
                var template = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
@if (true)
{
    <Compile>
}";
                Assert.Throws<TemplateParsingException>(() =>
                {
                    string compiled = service.RunCompile(template, Guid.NewGuid().ToString());
                });
            });
        }

        /// <summary>
        /// Tests that we fail with the right exception
        /// </summary>
        [Test]
        public void RazorEngineService_CustomLocalizerHelper_OverrideModelType()
        {
            RunTestHelper(service =>
            {
                // Tag must be closed!
                var model = new TemplateViewData() { Language = "lang", Model = new Person() { Forename = "forname", Surname = "surname" } };
                var template = @"@Model.Forename @Include(""test"") @Localizer.Language";

                service.Compile("@Model.Surname", "test", typeof(Person));
                string result = service.RunCompile(template, Guid.NewGuid().ToString(), typeof(Person), model);
                Assert.AreEqual("forname surname lang", result);
            }, config =>
            {
                config.BaseTemplateType = typeof(AddLanguageInfo_OverrideModelType<>);
            });
        }


        /// <summary>
        /// Tests that overriding Include and ResolveLayout can be used to hook custom data into a custom base class.
        /// </summary>
        [Test]
        public void RazorEngineService_CustomLocalizerHelper_OverrideInclude()
        {
            RunTestHelper(service =>
            {
                // Tag must be closed!
                var model = new TemplateViewData() { Language = "lang", Model = new Person() { Forename = "forname", Surname = "surname" } };
                var template = @"@Model.Forename @Include(""test"") @Localizer.Language";

                service.Compile("@Model.Surname", "test", typeof(Person));
                string result = service.RunCompile(template, Guid.NewGuid().ToString(), typeof(Person), model);
                Assert.AreEqual("forname surname lang", result);
            }, config =>
            {
                config.BaseTemplateType = typeof(AddLanguageInfo_OverrideInclude<>);
            });
        }


        /// <summary>
        /// Tests that we can use ViewBag to hook new data into a custom TemplateBase class.
        /// </summary>
        [Test]
        public void RazorEngineService_CustomLocalizerHelper_ViewBag()
        {
            RunTestHelper(service =>
            {
                var model = new Person() { Forename = "forname", Surname = "surname" };
                var template = @"@Model.Forename @Include(""test"") @Localizer.Language";

                service.Compile("@Model.Surname", "test", typeof(Person));
                dynamic viewbag = new DynamicViewBag();
                viewbag.Language = "lang";
                string result = service.RunCompile(template, Guid.NewGuid().ToString(), typeof(Person), model, (DynamicViewBag) viewbag);
                Assert.AreEqual("forname surname lang", result);
            }, config =>
            {
                config.BaseTemplateType = typeof(AddLanguageInfo_Viewbag<>);
            });
        }

        /// <summary>
        /// Tests that we can access the Viewbag from within the SetModel method.
        /// </summary>
        [Test]
        public void RazorEngineService_CheckViewbagAccessFromSetModel()
        {
            RunTestHelper(service =>
            {
                var model = new Person() { Forename = "forname", Surname = "surname" };
                var template = @"@Model.Forename @Include(""test"") @Localizer.Language";

                service.Compile("@Model.Surname", "test", typeof(Person));
                dynamic viewbag = new DynamicViewBag();
                viewbag.Language = "lang";
                string result = service.RunCompile(template, Guid.NewGuid().ToString(), typeof(Person), model, (DynamicViewBag)viewbag);
                Assert.AreEqual("forname surname lang", result);
            }, config =>
            {
                config.BaseTemplateType = typeof(AddLanguageInfo_Viewbag_SetModel<>);
            });
        }

        /// <summary>
        /// Tests that nested base classes work.
        /// </summary>
        [Test]
        public void RazorEngineService_TestNestedBaseClass()
        {
            RunTestHelper(service =>
            {
                var model = new Person() { Forename = "forname", Surname = "surname" };
                var template = @"@TestProperty";

                string result = service.RunCompile(template, Guid.NewGuid().ToString(), typeof(Person), model);
                Assert.AreEqual("mytest", result);
            }, config =>
            {
                config.BaseTemplateType = typeof(HostingClass.NestedBaseClass<>);
            });
        }

        /// <summary>
        /// Tests that nested base classes work.
        /// </summary>
        [Test]
        public void RazorEngineService_TestNestedModelClass()
        {
            RunTestHelper(service =>
            {
                var template = @"@Model.TestProperty";
                string result = service.RunCompile(template, "key", typeof(HostingClass.NestedClass), 
                    new HostingClass.NestedClass() { TestProperty = "test" });
                Assert.AreEqual("test", result);
            });
        }

        /// <summary>
        /// Tests that nested base classes work.
        /// </summary>
        [Test]
        public void RazorEngineService_TestNestedGenericModelClass()
        {
            RunTestHelper(service =>
            {
                var template = @"@Model.TestProperty";
                string result = service.RunCompile(template, "key", typeof(HostingClass.GenericNestedClass<string>),
                    new HostingClass.GenericNestedClass<string>() { TestProperty = "test" });
                Assert.AreEqual("test", result);
            });
        }
    }
}
