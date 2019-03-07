﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Gen;
using Microsoft.Templates.Fakes;

namespace Microsoft.Templates.Test
{
    public abstract class BaseGenAndBuildFixture
    {
        private const string Platform = "x86";
        private const string Config = "Debug";
        private readonly string _emptyBackendFramework = string.Empty;

        public abstract string GetTestRunPath();

        public abstract void InitializeFixture(IContextProvider contextProvider, string framework = "");

        public string TestProjectsPath => Path.GetFullPath(Path.Combine(GetTestRunPath(), "Proj"));

        public string TestNewItemPath => Path.GetFullPath(Path.Combine(GetTestRunPath(), "RightClick"));

        public IEnumerable<ITemplateInfo> Templates() => GenContext.ToolBox.Repo.GetAll();

        public IEnumerable<ITemplateInfo> GetTemplates(string framework, string platform)
        {
            return GenContext.ToolBox.Repo.GetAll().Where(t => t.GetFrontEndFrameworkList().Contains(framework) && t.GetPlatform() == platform);
        }

        public UserSelection SetupProject(string projectType, string framework, string platform, string language, Func<ITemplateInfo, string> getName = null)
        {
            var userSelection = new UserSelection(projectType, framework, _emptyBackendFramework, platform, language);

            var layouts = GenComposer.GetLayoutTemplates(userSelection.ProjectType, userSelection.FrontEndFramework, userSelection.BackEndFramework, userSelection.Platform);

            foreach (var item in layouts)
            {
                if (getName == BaseGenAndBuildFixture.GetDefaultName || getName == null)
                {
                    AddItem(userSelection, item.Layout.Name, item.Template);
                }
                else
                {
                    AddItem(userSelection, item.Template, getName);
                }
            }

            userSelection.HomeName = userSelection.Pages.FirstOrDefault().Name;

            return userSelection;
        }

        public void AddItems(UserSelection userSelection, IEnumerable<ITemplateInfo> templates, Func<ITemplateInfo, string> getName)
        {
            foreach (var template in templates)
            {
                AddItem(userSelection, template, getName);
            }
        }

        public void AddItem(UserSelection userSelection, ITemplateInfo template, Func<ITemplateInfo, string> getName)
        {
            if (template.GetMultipleInstance() || !AlreadyAdded(userSelection, template))
            {
                var itemName = getName(template);
                var usedNames = userSelection.Pages.Select(p => p.Name).Concat(userSelection.Features.Select(f => f.Name));
                var validators = new List<Validator>()
                    {
                        new ExistingNamesValidator(usedNames),
                        new ReservedNamesValidator(),
                    };
                if (template.GetItemNameEditable())
                {
                    validators.Add(new DefaultNamesValidator());
                }

                itemName = Naming.Infer(itemName, validators);
                AddItem(userSelection, itemName, template);
            }
        }

        public void AddItem(UserSelection userSelection, string itemName, ITemplateInfo template)
        {
            var templateInfo = new TemplateInfo { Name = itemName, Template = template };
            switch (template.GetTemplateType())
            {
                case TemplateType.Page:
                    userSelection.Pages.Add(templateInfo);
                    break;
                case TemplateType.Feature:
                    userSelection.Features.Add(templateInfo);
                    break;
            }

            var dependencies = GenComposer.GetAllDependencies(template, userSelection.FrontEndFramework, userSelection.BackEndFramework, userSelection.Platform);

            foreach (var item in dependencies)
            {
                if (!AlreadyAdded(userSelection, item))
                {
                    AddItem(userSelection, item.GetDefaultName(), item);
                }
            }
        }

        public (int exitCode, string outputFile) BuildAppxBundle(string projectName, string outputPath, string projectExtension)
        {
            var outputFile = Path.Combine(outputPath, $"_buildOutput_{projectName}.txt");

            var solutionFile = Path.GetFullPath(outputPath + @"\" + projectName + ".sln");
            var projectFile = Path.GetFullPath(outputPath + @"\" + projectName + @"\" + projectName + $".{projectExtension}");

            Console.Out.WriteLine();
            Console.Out.WriteLine($"### > Ready to start building");
            Console.Out.Write($"### > Running following command: {GetPath("RestoreAndBuildAppx.bat")} \"{projectFile}\"");

            var startInfo = new ProcessStartInfo(GetPath("RestoreAndBuildAppx.bat"))
            {
                Arguments = $"\"{solutionFile}\" \"{projectFile}\" ",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = false,
                WorkingDirectory = outputPath,
            };

            var process = Process.Start(startInfo);

            File.WriteAllText(outputFile, process.StandardOutput.ReadToEnd(), Encoding.UTF8);

            process.WaitForExit();

            return (process.ExitCode, outputFile);
        }

        public (int exitCode, string outputFile, string resultFile) RunWackTestOnAppxBundle(string bundleFilePath, string outputPath)
        {
            var outputFile = Path.Combine(outputPath, $"_wackOutput_{Path.GetFileName(bundleFilePath)}.txt");
            var resultFile = Path.Combine(outputPath, "_wackresults.xml");

            Console.Out.WriteLine();
            Console.Out.WriteLine("### > Ready to run WACK test");
            Console.Out.Write($"### > Running following command: {GetPath("RunWackTest.bat")} \"{bundleFilePath}\" \"{resultFile}\"");

            var startInfo = new ProcessStartInfo(GetPath("RunWackTest.bat"))
            {
                Arguments = $"\"{bundleFilePath}\" \"{resultFile}\" ",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = false,
                WorkingDirectory = outputPath,
            };

            var process = Process.Start(startInfo);

            File.WriteAllText(outputFile, process.StandardOutput.ReadToEnd(), Encoding.UTF8);

            process.WaitForExit();

            return (process.ExitCode, outputFile, resultFile);
        }

        private bool AlreadyAdded(UserSelection userSelection, ITemplateInfo item)
        {
            return userSelection.Pages.Any(p => p.Template.Identity == item.Identity) || userSelection.Features.Any(f => f.Template.Identity == item.Identity);
        }

        public static string GetDefaultName(ITemplateInfo template)
        {
            return template.GetDefaultName();
        }

#pragma warning disable RECS0154 // Parameter is never used - but used by method which takes an action which is passed a template
        public static string GetRandomName(ITemplateInfo template)
#pragma warning restore RECS0154 // Parameter is never used
        {
            for (int i = 0; i < 10; i++)
            {
                var validators = new List<Validator>()
                {
                    new EmptyNameValidator(),
                    new BadFormatValidator(),
                };
                var randomName = Path.GetRandomFileName().Replace(".", string.Empty);
                if (Naming.Validate(randomName, validators).IsValid)
                {
                    return randomName;
                }
            }

            throw new ApplicationException("No valid randomName could be generated");
        }

        public (int exitCode, string outputFile) BuildSolution(string solutionName, string outputPath, string platform)
        {
            var outputFile = Path.Combine(outputPath, $"_buildOutput_{solutionName}.txt");

            // Build
            var solutionFile = Path.GetFullPath(outputPath + @"\" + solutionName + ".sln");

            var batFile = "RestoreAndBuild.bat";

            var batPath = Path.GetDirectoryName(GetPath(batFile));

            Console.Out.WriteLine();
            Console.Out.WriteLine($"### > Ready to start building");
            Console.Out.Write($"### > Running following command: {GetPath(batFile)} \"{solutionFile}\" {Platform} {Config}");

            var startInfo = new ProcessStartInfo(GetPath(batFile))
            {
                Arguments = $"\"{solutionFile}\" {Platform} {Config} {batPath}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = false,
                WorkingDirectory = outputPath,
            };

            var process = Process.Start(startInfo);

            File.WriteAllText(outputFile, process.StandardOutput.ReadToEnd(), Encoding.UTF8);

            process.WaitForExit();

            return (process.ExitCode, outputFile);
        }

        public (int exitCode, string outputFile) RunTests(string projectName, string outputPath)
        {
            var outputFile = Path.Combine(outputPath, $"_testOutput_{projectName}.txt");

            var solutionFile = Path.GetFullPath(outputPath + @"\" + projectName + ".sln");

            const string batFile = "RunTests.bat";

            // Just run the tests against code in the core library. Can't run UI related/dependent code from the cmd line / on the server
            var mstestPath = $"\"{outputPath}\\{projectName}.Core.Tests.MSTest\\bin\\Debug\\netcoreapp2.1\\{projectName}.Core.Tests.MSTest.dll\" ";
            var nunitPath = $"\"{outputPath}\\{projectName}.Core.Tests.NUnit\\bin\\Debug\\netcoreapp2.1\\{projectName}.Core.Tests.NUnit.dll\" ";
            var xunitPath = $"\"{outputPath}\\{projectName}.Core.Tests.xUnit\\bin\\Debug\\netcoreapp2.1\\{projectName}.Core.Tests.xUnit.dll\" ";

            var batPath = Path.GetDirectoryName(GetPath(batFile));

            var startInfo = new ProcessStartInfo(GetPath(batFile))
            {
                Arguments = $"{mstestPath} {nunitPath} {xunitPath}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = false,
                WorkingDirectory = outputPath,
            };

            var process = Process.Start(startInfo);

            File.WriteAllText(outputFile, process.StandardOutput.ReadToEnd(), Encoding.UTF8);

            process.WaitForExit();

            return (process.ExitCode, outputFile);
        }

        public string GetErrorLines(string filePath)
        {
            Regex re = new Regex(@"^.*error .*$", RegexOptions.Multiline & RegexOptions.IgnoreCase);
            var outputLines = File.ReadAllLines(filePath);
            var errorLines = outputLines.Where(l => re.IsMatch(l));

            return errorLines.Any() ? errorLines.Aggregate((i, j) => i + Environment.NewLine + j) : string.Empty;
        }

        public string GetTestSummary(string filePath)
        {
            var outputLines = File.ReadAllLines(filePath);
            var summaryLines = outputLines.Where(l => l.StartsWith("Total tests") || l.StartsWith("Test "));

            return summaryLines.Any() ? summaryLines.Aggregate((i, j) => i + Environment.NewLine + j) : string.Empty;
        }

        private static string GetPath(string fileName)
        {
            string path = Path.Combine(new FileInfo(Assembly.GetExecutingAssembly().Location).DirectoryName, fileName);

            if (!File.Exists(path))
            {
                path = Path.GetFullPath($@".\{fileName}");

                if (!File.Exists(path))
                {
                    throw new ApplicationException($"Can not find {fileName}");
                }
            }

            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(GetTestRunPath()))
            {
                CleanUpOldTests();

                if ((!Directory.Exists(TestProjectsPath) || !Directory.EnumerateDirectories(TestProjectsPath).Any())
                 && (!Directory.Exists(TestNewItemPath) || !Directory.EnumerateDirectories(TestNewItemPath).Any()))
                {
                    Directory.Delete(GetTestRunPath(), true);
                }
            }
        }

        private void CleanUpOldTests()
        {
            var rootDir = new DirectoryInfo(GetTestRunPath()).Parent;

            var oldDirectories = rootDir.EnumerateDirectories().Where(d => d.CreationTime < DateTime.Now.AddDays(-7));
            foreach (var dir in oldDirectories)
            {
                dir.Delete(true);
            }
        }

        public static void SetCurrentLanguage(string language)
        {
            GenContext.SetCurrentLanguage(language);
            var fakeShell = GenContext.ToolBox.Shell as FakeGenShell;
            fakeShell.SetCurrentLanguage(language);
        }

        public static void SetCurrentPlatform(string platform)
        {
            var fakeShell = GenContext.ToolBox.Shell as FakeGenShell;
            fakeShell.SetCurrentPlatform(platform);
        }

        protected static IEnumerable<object[]> GetPageAndFeatureTemplates(string frameworkFilter)
        {
            List<object[]> result = new List<object[]>();
            foreach (var language in ProgrammingLanguages.GetAllLanguages())
            {
                SetCurrentLanguage(language);
                foreach (var platform in Platforms.GetAllPlatforms())
                {
                    SetCurrentPlatform(platform);
                    var templateProjectTypes = GenComposer.GetSupportedProjectTypes(platform);

                    var projectTypes = GenContext.ToolBox.Repo.GetProjectTypes(platform)
                                .Where(m => templateProjectTypes.Contains(m.Name) && !string.IsNullOrEmpty(m.Description))
                                .Select(m => m.Name);

                    foreach (var projectType in projectTypes)
                    {
                        var projectFrameworks = GenComposer.GetSupportedFx(projectType, platform);

                        var targetFrameworks = GenContext.ToolBox.Repo.GetFrontEndFrameworks(platform)
                                                    .Where(m => projectFrameworks.Any(f => f.Type == FrameworkTypes.FrontEnd && f.Name == m.Name) && m.Name == frameworkFilter)
                                                    .Select(m => m.Name).ToList();

                        foreach (var framework in targetFrameworks)
                        {
                            var itemTemplates = GenContext.ToolBox.Repo.GetAll().Where(t => t.GetFrontEndFrameworkList().Contains(framework)
                                                                 && (t.GetTemplateType() == TemplateType.Page || t.GetTemplateType() == TemplateType.Feature)
                                                                 && t.GetPlatform() == platform
                                                                 && t.GetLanguage() == language
                                                                 && !t.GetIsHidden());

                            foreach (var itemTemplate in itemTemplates)
                            {
                                result.Add(new object[]
                                {
                                    itemTemplate.Name,
                                    projectType,
                                    framework,
                                    platform,
                                    itemTemplate.Identity,
                                    language,
                                });
                            }
                        }
                    }
                }
            }

            return result;
        }

        protected static IEnumerable<object[]> GetVBProjectTemplates()
        {
            List<object[]> result = new List<object[]>();

            var platform = Platforms.Uwp;

            var projectTemplates =
               GenContext.ToolBox.Repo.GetAll().Where(
                   t => t.GetTemplateType() == TemplateType.Project
                    && t.GetLanguage() == ProgrammingLanguages.VisualBasic);

            foreach (var projectTemplate in projectTemplates)
            {
                var projectTypeList = projectTemplate.GetProjectTypeList();

                foreach (var projectType in projectTypeList)
                {
                    var frameworks = GenComposer.GetSupportedFx(projectType, platform);

                    foreach (var framework in frameworks)
                    {
                        result.Add(new object[] { projectType, framework.Name, platform });
                    }
                }
            }

            return result;
        }

        protected static IEnumerable<object[]> GetAllProjectTemplates()
        {
            List<object[]> result = new List<object[]>();
            foreach (var language in ProgrammingLanguages.GetAllLanguages())
            {
                SetCurrentLanguage(language);

                foreach (var platform in Platforms.GetAllPlatforms())
                {
                    SetCurrentPlatform(platform);
                    var templateProjectTypes = GenComposer.GetSupportedProjectTypes(platform);

                    var projectTypes = GenContext.ToolBox.Repo.GetProjectTypes(platform)
                                .Where(m => templateProjectTypes.Contains(m.Name) && !string.IsNullOrEmpty(m.Description))
                                .Select(m => m.Name);

                    foreach (var projectType in projectTypes)
                    {
                        var projectFrameworks = GenComposer.GetSupportedFx(projectType, platform);

                        var targetFrameworks = GenContext.ToolBox.Repo.GetFrontEndFrameworks(platform)
                                                    .Where(m => projectFrameworks.Any(f => f.Type == FrameworkTypes.FrontEnd && f.Name == m.Name))
                                                    .Select(m => m.Name).ToList();

                        foreach (var framework in targetFrameworks)
                        {
                            result.Add(new object[] { projectType, framework, platform, language });
                        }
                    }
                }
            }

            return result;
        }
    }
}
