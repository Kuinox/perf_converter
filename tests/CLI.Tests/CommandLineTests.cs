using FluentAssertions;
using System.CommandLine;
using System.CommandLine.Parsing;
using Xunit;

namespace CLI.Tests;

public class CommandLineTests
{
    [Fact]
    public void Should_Parse_Input_File_Argument()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var inputFile = parseResult.GetValueForArgument(GetInputFileArgument(rootCommand));
        inputFile?.Name.Should().Be("test.data");
    }

    [Fact]
    public void Should_Parse_Perf_Args_Option()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data", "--perf-args", "-f --custom-arg" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var perfArgs = parseResult.GetValueForOption(GetPerfArgsOption(rootCommand));
        perfArgs.Should().Be("-f --custom-arg");
    }

    [Fact]
    public void Should_Use_Default_Perf_Args()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var perfArgs = parseResult.GetValueForOption(GetPerfArgsOption(rootCommand));
        perfArgs.Should().Be("-f --itrace=bei0ns");
    }

    [Fact]
    public void Should_Parse_Output_Directory_Option()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data", "--output", "/custom/output" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var outputDir = parseResult.GetValueForOption(GetOutputOption(rootCommand));
        outputDir?.FullName.Should().Be("/custom/output");
    }

    [Fact]
    public void Should_Use_Default_Output_Directory()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var outputDir = parseResult.GetValueForOption(GetOutputOption(rootCommand));
        outputDir?.Name.Should().Be("parquet_output");
    }

    [Fact]
    public void Should_Parse_Dry_Run_Option()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data", "--dry-run" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var dryRun = parseResult.GetValueForOption(GetDryRunOption(rootCommand));
        dryRun.Should().BeTrue();
    }

    [Fact]
    public void Should_Default_Dry_Run_To_False()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = new[] { "test.data" };

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().BeEmpty();
        var dryRun = parseResult.GetValueForOption(GetDryRunOption(rootCommand));
        dryRun.Should().BeFalse();
    }

    [Fact]
    public void Should_Require_Input_File_Argument()
    {
        // Arrange
        var rootCommand = CreateTestRootCommand();
        var args = Array.Empty<string>();

        // Act
        var parseResult = rootCommand.Parse(args);

        // Assert
        parseResult.Errors.Should().NotBeEmpty();
        parseResult.Errors[0].Message.Should().Contain("Required argument missing");
    }

    private static RootCommand CreateTestRootCommand()
    {
        var rootCommand = new RootCommand("PerfConverter CLI - Helper tool for running perf with PerfConverter DLFilter");

        var inputFileArgument = new Argument<FileInfo>(
            name: "input-file",
            description: "Path to the perf data file");

        var perfArgsOption = new Option<string>(
            ["--perf-args", "-p"],
            getDefaultValue: () => "-f --itrace=bei0ns",
            "Additional arguments to pass to perf script");

        var outputOption = new Option<DirectoryInfo>(
            ["--output", "-o"],
            getDefaultValue: () => new DirectoryInfo("parquet_output"),
            "Output directory for Parquet files");

        var dryRunOption = new Option<bool>(
            ["--dry-run", "-n"],
            "Show the command that would be executed without running it");

        rootCommand.AddArgument(inputFileArgument);
        rootCommand.AddOption(perfArgsOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(dryRunOption);

        return rootCommand;
    }

    private static Argument<FileInfo> GetInputFileArgument(RootCommand rootCommand)
    {
        return (Argument<FileInfo>)rootCommand.Arguments.First(arg => arg.Name == "input-file");
    }

    private static Option<string> GetPerfArgsOption(RootCommand rootCommand)
    {
        return (Option<string>)rootCommand.Options.First(opt => opt.Name == "perf-args");
    }

    private static Option<DirectoryInfo> GetOutputOption(RootCommand rootCommand)
    {
        return (Option<DirectoryInfo>)rootCommand.Options.First(opt => opt.Name == "output");
    }

    private static Option<bool> GetDryRunOption(RootCommand rootCommand)
    {
        return (Option<bool>)rootCommand.Options.First(opt => opt.Name == "dry-run");
    }
}