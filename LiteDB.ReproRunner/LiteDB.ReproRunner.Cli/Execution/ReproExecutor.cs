using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using LiteDB.ReproRunner.Cli.Manifests;
using LiteDB.ReproRunner.Shared;
using LiteDB.ReproRunner.Shared.Messaging;
using System.Globalization;
using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace LiteDB.ReproRunner.Cli.Execution;

/// <summary>
/// Executes built repro assemblies and relays their structured output.
/// </summary>
internal sealed class ReproExecutor
{
    private const int CapturedOutputLimit = 200;
    private const string DefaultLinuxSdkImage = "mcr.microsoft.com/dotnet/sdk:8.0";

    private readonly object _dockerProbeLock = new();
    private bool? _dockerCliAvailable;

    private readonly TextWriter _standardOut;
    private readonly TextWriter _standardError;
    private readonly object _writeLock = new();
    private readonly object _configurationLock = new();
    private readonly Dictionary<int, ConfigurationState> _configurationStates = new();
    private ConfigurationExpectation? _configurationExpectation;
    private bool _configurationMismatchDetected;
    private int _expectedConfigurationInstances;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReproExecutor"/> class using the console streams.
    /// </summary>
    public ReproExecutor()
        : this(Console.Out, Console.Error)
    {
    }

    internal ReproExecutor(TextWriter? standardOut, TextWriter? standardError)
    {
        _standardOut = standardOut ?? Console.Out;
        _standardError = standardError ?? Console.Error;
    }

    internal Action<int, ReproHostMessageEnvelope>? StructuredMessageObserver { get; set; }

    internal Action<ReproExecutionLogEntry>? LogObserver { get; set; }

    internal bool SuppressConsoleLogOutput { get; set; }

    internal void ConfigureExpectedConfiguration(bool useProjectReference, string? liteDbPackageVersion, int instanceCount)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(liteDbPackageVersion)
            ? null
            : liteDbPackageVersion.Trim();

        lock (_configurationLock)
        {
            _configurationExpectation = new ConfigurationExpectation(useProjectReference, normalizedVersion);
            _configurationStates.Clear();
            _expectedConfigurationInstances = Math.Max(instanceCount, 0);
            _configurationMismatchDetected = false;

            for (var index = 0; index < _expectedConfigurationInstances; index++)
            {
                _configurationStates[index] = new ConfigurationState();
            }
        }
    }

    /// <summary>
    /// Executes the provided repro build across the requested number of instances.
    /// </summary>
    /// <param name="build">The build to execute.</param>
    /// <param name="instances">The number of instances to launch.</param>
    /// <param name="timeoutSeconds">The timeout applied to the execution.</param>
    /// <param name="cancellationToken">The token used to observe cancellation requests.</param>
    /// <returns>The execution result for the run.</returns>
    public async Task<ReproExecutionResult> ExecuteAsync(
        ReproBuildResult build,
        int instances,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (build is null)
        {
            throw new ArgumentNullException(nameof(build));
        }

        if (!build.Succeeded || string.IsNullOrWhiteSpace(build.AssemblyPath))
        {
            return new ReproExecutionResult(build.Plan.UseProjectReference, false, build.ExitCode, TimeSpan.Zero, Array.Empty<ReproExecutionCapturedLine>(), null);
        }

        var repro = build.Plan.Repro;

        if (repro.ProjectPath is null)
        {
            return new ReproExecutionResult(build.Plan.UseProjectReference, false, build.ExitCode, TimeSpan.Zero, Array.Empty<ReproExecutionCapturedLine>(), null);
        }

        var manifest = repro.Manifest ?? throw new InvalidOperationException("Manifest is required to execute a repro.");
        var environment = await ResolveExecutionEnvironmentAsync(manifest, cancellationToken).ConfigureAwait(false);

        if (!environment.Supported)
        {
            if (!string.IsNullOrWhiteSpace(environment.FailureReason))
            {
                WriteErrorLine(environment.FailureReason);
            }

            return CreateSkippedResult(build.Plan.UseProjectReference, environment.FailureReason);
        }

        ConfigureExpectedConfiguration(build.Plan.UseProjectReference, build.Plan.LiteDBPackageVersion, instances);
        var projectDirectory = Path.GetDirectoryName(repro.ProjectPath)!;
        var stopwatch = Stopwatch.StartNew();

        var sharedKey = !string.IsNullOrWhiteSpace(manifest.SharedDatabaseKey)
            ? manifest.SharedDatabaseKey!
            : manifest.Id;

        var runIdentifier = Guid.NewGuid().ToString("N");
        var sharedRoot = Path.Combine(build.Plan.ExecutionRootDirectory, Sanitize(sharedKey), runIdentifier);
        Directory.CreateDirectory(sharedRoot);

        var capturedOutput = new BoundedLogBuffer(CapturedOutputLimit);

        try
        {
            var runResult = await RunInstancesAsync(
                environment,
                manifest,
                projectDirectory,
                build.AssemblyPath,
                instances,
                timeoutSeconds,
                build.Plan.ExecutionRootDirectory,
                sharedRoot,
                runIdentifier,
                capturedOutput,
                cancellationToken).ConfigureAwait(false);

            FinalizeConfigurationValidation();
            var configurationMismatch = HasConfigurationMismatch();

            var exitCode = runResult.ExitCode;

            if (configurationMismatch && exitCode == 0)
            {
                exitCode = -2;
            }

            stopwatch.Stop();
            return new ReproExecutionResult(
                build.Plan.UseProjectReference,
                exitCode == 0 && !configurationMismatch,
                exitCode,
                stopwatch.Elapsed,
                capturedOutput.ToSnapshot(),
                runResult.FailureReason);
        }
        finally
        {
            ResetConfigurationExpectation();
        }
    }


    private Task<ExecutionRunResult> RunInstancesAsync(
        ExecutionEnvironmentResolution environment,
        ReproManifest manifest,
        string projectDirectory,
        string assemblyPath,
        int instances,
        int timeoutSeconds,
        string executionRoot,
        string sharedRoot,
        string runIdentifier,
        BoundedLogBuffer capturedOutput,
        CancellationToken cancellationToken)
    {
        return environment.Kind switch
        {
            ExecutionEnvironmentKind.Local => RunInstancesLocallyAsync(
                manifest,
                projectDirectory,
                assemblyPath,
                instances,
                timeoutSeconds,
                sharedRoot,
                runIdentifier,
                capturedOutput,
                cancellationToken),
            ExecutionEnvironmentKind.LinuxContainer => RunInstancesInDockerAsync(
                environment,
                manifest,
                projectDirectory,
                assemblyPath,
                instances,
                timeoutSeconds,
                executionRoot,
                sharedRoot,
                runIdentifier,
                capturedOutput,
                cancellationToken),
            _ => RunInstancesLocallyAsync(
                manifest,
                projectDirectory,
                assemblyPath,
                instances,
                timeoutSeconds,
                sharedRoot,
                runIdentifier,
                capturedOutput,
                cancellationToken)
        };
    }


    private async Task<ExecutionRunResult> RunInstancesLocallyAsync(
        ReproManifest manifest,
        string projectDirectory,
        string assemblyPath,
        int instances,
        int timeoutSeconds,
        string sharedRoot,
        string runIdentifier,
        BoundedLogBuffer capturedOutput,
        CancellationToken cancellationToken)
    {
        var manifestArgs = manifest.Args;
        var processes = new List<Process>();
        var outputTasks = new List<Task>();
        var errorTasks = new List<Task>();

        try
        {
            for (var index = 0; index < instances; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startInfo = CreateStartInfo(projectDirectory, assemblyPath, manifestArgs);
                startInfo.Environment["LITEDB_RR_SHARED_DB"] = sharedRoot;
                startInfo.Environment["LITEDB_RR_INSTANCE_INDEX"] = index.ToString();
                startInfo.Environment["LITEDB_RR_TOTAL_INSTANCES"] = instances.ToString();
                startInfo.Environment["LITEDB_RR_RUN_IDENTIFIER"] = runIdentifier;

                var process = Process.Start(startInfo);
                if (process is null)
                {
                    throw new InvalidOperationException("Failed to start repro process.");
                }

                processes.Add(process);
                outputTasks.Add(PumpStandardOutputAsync(process, index, capturedOutput, cancellationToken));
                errorTasks.Add(PumpStandardErrorAsync(process, index, capturedOutput, cancellationToken));

                await SendHostHandshakeAsync(process, manifest, sharedRoot, runIdentifier, index, instances, cancellationToken).ConfigureAwait(false);
            }

            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var waitTasks = processes.Select(p => p.WaitForExitAsync(cancellationToken)).ToList();
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var allProcessesTask = Task.WhenAll(waitTasks);
            var completed = await Task.WhenAny(allProcessesTask, timeoutTask).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (completed == timeoutTask)
            {
                foreach (var process in processes)
                {
                    TryKill(process);
                }

                return new ExecutionRunResult(1, null);
            }

            await allProcessesTask.ConfigureAwait(false);
            await Task.WhenAll(outputTasks.Concat(errorTasks)).ConfigureAwait(false);

            var exitCode = 0;

            foreach (var process in processes)
            {
                if (process.ExitCode != 0 && exitCode == 0)
                {
                    exitCode = process.ExitCode;
                }
            }

            return new ExecutionRunResult(exitCode, null);
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!process.HasExited)
                {
                    TryKill(process);
                }

                process.Dispose();
            }
        }
    }

    private async Task<ExecutionRunResult> RunInstancesInDockerAsync(
        ExecutionEnvironmentResolution environment,
        ReproManifest manifest,
        string projectDirectory,
        string assemblyPath,
        int instances,
        int timeoutSeconds,
        string executionRoot,
        string sharedRoot,
        string runIdentifier,
        BoundedLogBuffer capturedOutput,
        CancellationToken cancellationToken)
    {
        var hostProjectDirectory = Path.GetFullPath(projectDirectory);
        var hostBuildDirectory = Path.GetFullPath(Path.GetDirectoryName(assemblyPath)!);
        var hostExecutionRoot = Path.GetFullPath(executionRoot);

        var containerProjectDirectory = "/workspace/project";
        var containerBuildDirectory = "/workspace/build";
        var containerRunDirectory = "/workspace/run";

        var assemblyFileName = Path.GetFileName(assemblyPath);
        var containerAssemblyPath = CombinePosixPaths(containerBuildDirectory, assemblyFileName);

        var relativeSharedRoot = Path.GetRelativePath(executionRoot, sharedRoot);
        if (string.Equals(relativeSharedRoot, ".", StringComparison.Ordinal))
        {
            relativeSharedRoot = string.Empty;
        }

        var normalizedSharedRoot = NormalizeToPosix(relativeSharedRoot);
        var containerSharedRoot = string.IsNullOrEmpty(normalizedSharedRoot)
            ? containerRunDirectory
            : CombinePosixPaths(containerRunDirectory, normalizedSharedRoot);

        var image = string.IsNullOrWhiteSpace(environment.DockerImage)
            ? DefaultLinuxSdkImage
            : environment.DockerImage!;

        var manifestArgs = manifest.Args;

        using var runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runtimeToken = runtimeCts.Token;

        var tasks = new List<Task<ContainerInstanceResult>>(instances);

        for (var index = 0; index < instances; index++)
        {
            tasks.Add(RunInstanceAsync(index, runtimeToken));
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var timeoutTask = Task.Delay(timeout, runtimeToken);
        var allTask = Task.WhenAll(tasks);

        var completed = await Task.WhenAny(allTask, timeoutTask).ConfigureAwait(false);

        if (completed == timeoutTask)
        {
            runtimeCts.Cancel();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }

            return new ExecutionRunResult(1, $"Docker execution exceeded the timeout of {timeoutSeconds} seconds.");
        }

        var results = await allTask.ConfigureAwait(false);

        var failureReason = results.Select(result => result.FailureReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        var exitCode = 0;

        foreach (var result in results)
        {
            if (result.ExitCode != 0 && exitCode == 0)
            {
                exitCode = result.ExitCode;
            }
        }

        return new ExecutionRunResult(exitCode, failureReason);

        async Task<ContainerInstanceResult> RunInstanceAsync(int index, CancellationToken ct)
        {
            var stdout = new MemoryStream();
            var stderr = new MemoryStream();
            var outputConsumer = Consume.RedirectStdoutAndStderrToStream(stdout, stderr);
            TestcontainersContainer? container = null;

            try
            {
                var command = new List<string> { "dotnet", containerAssemblyPath };
                foreach (var argument in manifestArgs)
                {
                    command.Add(argument);
                }

                container = new TestcontainersBuilder<TestcontainersContainer>()
                    .WithImage(image)
                    .WithWorkingDirectory(containerProjectDirectory)
                    .WithCommand(command.ToArray())
                    .WithBindMount(hostProjectDirectory, containerProjectDirectory)
                    .WithBindMount(hostBuildDirectory, containerBuildDirectory)
                    .WithBindMount(hostExecutionRoot, containerRunDirectory)
                    .WithEnvironment("LITEDB_RR_SHARED_DB", containerSharedRoot)
                    .WithEnvironment("LITEDB_RR_INSTANCE_INDEX", index.ToString(CultureInfo.InvariantCulture))
                    .WithEnvironment("LITEDB_RR_TOTAL_INSTANCES", instances.ToString(CultureInfo.InvariantCulture))
                    .WithEnvironment("LITEDB_RR_RUN_IDENTIFIER", runIdentifier)
                    .WithOutputConsumer(outputConsumer)
                    .WithCleanUp(true)
                    .Build();

                await container.StartAsync(ct).ConfigureAwait(false);
                var exitCodeValue = await container.GetExitCode(ct).ConfigureAwait(false);
                var exitCode = (int)exitCodeValue;

                ProcessContainerOutput(index, stdout, ReproExecutionStream.StandardOutput, capturedOutput);
                ProcessContainerOutput(index, stderr, ReproExecutionStream.StandardError, capturedOutput);

                return new ContainerInstanceResult(exitCode, null);
            }
            catch (OperationCanceledException)
            {
                return new ContainerInstanceResult(1, "Docker execution cancelled.");
            }
            catch (Exception ex)
            {
                ProcessContainerOutput(index, stdout, ReproExecutionStream.StandardOutput, capturedOutput);
                ProcessContainerOutput(index, stderr, ReproExecutionStream.StandardError, capturedOutput);
                return new ContainerInstanceResult(1, $"Docker execution failed: {ex.Message}");
            }
            finally
            {
                if (container is not null)
                {
                    try
                    {
                        await container.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                stdout.Dispose();
                stderr.Dispose();
            }
        }
    }

    private static string NormalizeToPosix(string path)
    {
        return string.IsNullOrEmpty(path)
            ? path
            : path.Replace('\\', '/');
    }

    private static string CombinePosixPaths(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return NormalizeToPosix(right);
        }

        if (string.IsNullOrEmpty(right))
        {
            return NormalizeToPosix(left);
        }

        var normalizedLeft = NormalizeToPosix(left).TrimEnd('/');
        var normalizedRight = NormalizeToPosix(right).TrimStart('/');
        if (normalizedLeft.Length == 0)
        {
            return "/" + normalizedRight;
        }

        return normalizedLeft + "/" + normalizedRight;
    }

    private void ProcessContainerOutput(int instanceIndex, Stream source, ReproExecutionStream stream, BoundedLogBuffer capturedOutput)
    {
        if (source is null)
        {
            return;
        }

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            capturedOutput.Add(stream, line);
            if (stream == ReproExecutionStream.StandardOutput)
            {
                if (!TryProcessStructuredLine(line, instanceIndex))
                {
                    WriteOutputLine($"[{instanceIndex}] {line}");
                }
            }
            else
            {
                WriteErrorLine($"[{instanceIndex}] {line}");
            }
        }
    }

    private static ReproExecutionResult CreateSkippedResult(bool useProjectReference, string? failureReason)
    {
        return new ReproExecutionResult(
            useProjectReference,
            false,
            int.MinValue,
            TimeSpan.Zero,
            Array.Empty<ReproExecutionCapturedLine>(),
            failureReason ?? "Required operating system is not available.");
    }

    private async Task<ExecutionEnvironmentResolution> ResolveExecutionEnvironmentAsync(ReproManifest manifest, CancellationToken cancellationToken)
    {
        return manifest.RequiredOperatingSystem switch
        {
            ReproOperatingSystem.Any => new ExecutionEnvironmentResolution(true, ExecutionEnvironmentKind.Local, null, null),
            ReproOperatingSystem.Windows => OperatingSystem.IsWindows()
                ? new ExecutionEnvironmentResolution(true, ExecutionEnvironmentKind.Local, null, null)
                : new ExecutionEnvironmentResolution(false, ExecutionEnvironmentKind.Local, null, $"Repro requires Windows but the current platform is {GetCurrentPlatformName()}."),
            ReproOperatingSystem.Linux => await ResolveLinuxRequirementAsync(cancellationToken).ConfigureAwait(false),
            _ => new ExecutionEnvironmentResolution(true, ExecutionEnvironmentKind.Local, null, null)
        };
    }

    private async Task<ExecutionEnvironmentResolution> ResolveLinuxRequirementAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            return new ExecutionEnvironmentResolution(true, ExecutionEnvironmentKind.Local, null, null);
        }

        if (OperatingSystem.IsWindows())
        {
            var available = await IsDockerCliAvailableAsync(cancellationToken).ConfigureAwait(false);
            if (available)
            {
                return new ExecutionEnvironmentResolution(true, ExecutionEnvironmentKind.LinuxContainer, null, null);
            }

            return new ExecutionEnvironmentResolution(false, ExecutionEnvironmentKind.LinuxContainer, null, "Docker is not available. Install and start Docker Desktop to run Linux repros.");
        }

        return new ExecutionEnvironmentResolution(false, ExecutionEnvironmentKind.LinuxContainer, null, $"Repro requires Linux but the current platform is {GetCurrentPlatformName()}.");
    }

    private async Task<bool> IsDockerCliAvailableAsync(CancellationToken cancellationToken)
    {
        lock (_dockerProbeLock)
        {
            if (_dockerCliAvailable.HasValue)
            {
                return _dockerCliAvailable.Value;
            }
        }

        var available = await ProbeDockerCliAsync(cancellationToken).ConfigureAwait(false);

        lock (_dockerProbeLock)
        {
            _dockerCliAvailable = available;
        }

        return available;
    }

    private static async Task<bool> ProbeDockerCliAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("info");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentPlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return RuntimeInformation.OSDescription;
    }

    private static ProcessStartInfo CreateStartInfo(string workingDirectory, string assemblyPath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add(assemblyPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private async Task PumpStandardOutputAsync(Process process, int instanceIndex, BoundedLogBuffer capturedOutput, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                capturedOutput.Add(ReproExecutionStream.StandardOutput, line);
                if (!TryProcessStructuredLine(line, instanceIndex))
                {
                    WriteOutputLine($"[{instanceIndex}] {line}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private async Task PumpStandardErrorAsync(Process process, int instanceIndex, BoundedLogBuffer capturedOutput, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                capturedOutput.Add(ReproExecutionStream.StandardError, line);
                WriteErrorLine($"[{instanceIndex}] {line}");
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    internal bool TryProcessStructuredLine(string line, int instanceIndex)
    {
        if (!ReproHostMessageEnvelope.TryParse(line, out var envelope, out _))
        {
            return false;
        }

        StructuredMessageObserver?.Invoke(instanceIndex, envelope!);

        if (!HandleConfigurationHandshake(instanceIndex, envelope!))
        {
            return true;
        }

        HandleStructuredMessage(instanceIndex, envelope!);
        return true;
    }

    private void HandleStructuredMessage(int instanceIndex, ReproHostMessageEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case ReproHostMessageTypes.Log:
                WriteLogMessage(instanceIndex, envelope);
                break;
            case ReproHostMessageTypes.Result:
                WriteResultMessage(instanceIndex, envelope);
                break;
            case ReproHostMessageTypes.Lifecycle:
                WriteOutputLine($"[{instanceIndex}] lifecycle: {envelope.Event ?? "(unknown)"}");
                break;
            case ReproHostMessageTypes.Progress:
                var suffix = envelope.Progress is double progress
                    ? $" ({progress:0.##}%)"
                    : string.Empty;
                WriteOutputLine($"[{instanceIndex}] progress: {envelope.Event ?? "(unknown)"}{suffix}");
                break;
            case ReproHostMessageTypes.Configuration:
                break;
            default:
                WriteOutputLine($"[{instanceIndex}] {envelope.Type}: {envelope.Text ?? string.Empty}");
                break;
        }
    }

    private bool HandleConfigurationHandshake(int instanceIndex, ReproHostMessageEnvelope envelope)
    {
        string? errorMessage = null;
        var shouldProcess = true;

        lock (_configurationLock)
        {
            if (_configurationExpectation is not { } expectation)
            {
                if (string.Equals(envelope.Type, ReproHostMessageTypes.Configuration, StringComparison.Ordinal))
                {
                    shouldProcess = false;
                }

                return shouldProcess;
            }

            if (!_configurationStates.TryGetValue(instanceIndex, out var state))
            {
                state = new ConfigurationState();
                _configurationStates[instanceIndex] = state;
            }

            if (!state.Received)
            {
                if (!string.Equals(envelope.Type, ReproHostMessageTypes.Configuration, StringComparison.Ordinal))
                {
                    errorMessage = "expected configuration handshake before other messages.";
                    state.Received = true;
                    state.IsValid = false;
                    _configurationMismatchDetected = true;
                    shouldProcess = false;
                }
                else
                {
                    var payload = envelope.DeserializePayload<ReproHostConfigurationPayload>();
                    if (payload is null)
                    {
                        errorMessage = "reported configuration without a payload.";
                        state.Received = true;
                        state.IsValid = false;
                        _configurationMismatchDetected = true;
                        shouldProcess = false;
                    }
                    else
                    {
                        var actualVersion = string.IsNullOrWhiteSpace(payload.LiteDBPackageVersion)
                            ? null
                            : payload.LiteDBPackageVersion.Trim();

                        var expectedVersion = expectation.LiteDbPackageVersion;
                        var versionMatches = string.Equals(
                            actualVersion ?? string.Empty,
                            expectedVersion ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase);

                        if (payload.UseProjectReference != expectation.UseProjectReference || !versionMatches)
                        {
                            var expectedVersionDisplay = expectedVersion ?? "(unspecified)";
                            var actualVersionDisplay = actualVersion ?? "(unspecified)";
                            errorMessage = $"reported configuration UseProjectReference={payload.UseProjectReference}, LiteDBPackageVersion={actualVersionDisplay} but expected UseProjectReference={expectation.UseProjectReference}, LiteDBPackageVersion={expectedVersionDisplay}.";
                            state.IsValid = false;
                            _configurationMismatchDetected = true;
                        }
                        else
                        {
                            state.IsValid = true;
                        }

                        state.Received = true;
                        shouldProcess = false;
                    }
                }
            }
            else if (!state.IsValid)
            {
                shouldProcess = false;
            }
            else if (string.Equals(envelope.Type, ReproHostMessageTypes.Configuration, StringComparison.Ordinal))
            {
                shouldProcess = false;
            }
        }

        if (errorMessage is not null)
        {
            WriteConfigurationError(instanceIndex, errorMessage);
        }

        return shouldProcess;
    }

    private void FinalizeConfigurationValidation()
    {
        List<int>? missingInstances = null;

        lock (_configurationLock)
        {
            if (_configurationExpectation is null)
            {
                return;
            }

            for (var index = 0; index < _expectedConfigurationInstances; index++)
            {
                if (!_configurationStates.TryGetValue(index, out var state))
                {
                    state = new ConfigurationState();
                    _configurationStates[index] = state;
                }

                if (!state.Received)
                {
                    state.Received = true;
                    state.IsValid = false;
                    _configurationMismatchDetected = true;
                    missingInstances ??= new List<int>();
                    missingInstances.Add(index);
                }
            }
        }

        if (missingInstances is null)
        {
            return;
        }

        foreach (var instanceIndex in missingInstances)
        {
            WriteConfigurationError(instanceIndex, "did not report configuration handshake.");
        }
    }

    private bool HasConfigurationMismatch()
    {
        lock (_configurationLock)
        {
            if (_configurationExpectation is null)
            {
                return false;
            }

            if (_configurationMismatchDetected)
            {
                return true;
            }

            foreach (var state in _configurationStates.Values)
            {
                if (!state.IsValid)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void ResetConfigurationExpectation()
    {
        lock (_configurationLock)
        {
            _configurationExpectation = null;
            _configurationStates.Clear();
            _configurationMismatchDetected = false;
            _expectedConfigurationInstances = 0;
        }
    }

    private void WriteConfigurationError(int instanceIndex, string message)
    {
        LogObserver?.Invoke(new ReproExecutionLogEntry(instanceIndex, $"configuration error: {message}", ReproHostLogLevel.Error));

        if (SuppressConsoleLogOutput)
        {
            return;
        }

        WriteErrorLine($"[{instanceIndex}] configuration error: {message}");
    }

    private void WriteLogMessage(int instanceIndex, ReproHostMessageEnvelope envelope)
    {
        var message = envelope.Text ?? string.Empty;
        var level = envelope.Level ?? ReproHostLogLevel.Information;
        var formatted = $"[{instanceIndex}] {message}";

        LogObserver?.Invoke(new ReproExecutionLogEntry(instanceIndex, message, level));

        if (SuppressConsoleLogOutput)
        {
            return;
        }

        switch (level)
        {
            case ReproHostLogLevel.Error:
            case ReproHostLogLevel.Critical:
                WriteErrorLine(formatted);
                break;
            case ReproHostLogLevel.Warning:
                WriteErrorLine(formatted);
                break;
            default:
                WriteOutputLine(formatted);
                break;
        }
    }

    private void WriteResultMessage(int instanceIndex, ReproHostMessageEnvelope envelope)
    {
        var success = envelope.Success is true;
        var status = success ? "succeeded" : "completed";
        var summary = envelope.Text ?? $"Repro {status}.";
        WriteOutputLine($"[{instanceIndex}] {summary}");
    }

    private async Task SendHostHandshakeAsync(
        Process process,
        ReproManifest manifest,
        string sharedRoot,
        string runIdentifier,
        int instanceIndex,
        int totalInstances,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = ReproInputEnvelope.CreateHostReady(runIdentifier, sharedRoot, instanceIndex, totalInstances, manifest.Id);
            var json = JsonSerializer.Serialize(envelope, ReproJsonOptions.Default);
            var writer = process.StandardInput;
            writer.AutoFlush = true;
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(json).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private sealed class BoundedLogBuffer
    {
        private readonly int _capacity;
        private readonly Queue<ReproExecutionCapturedLine> _buffer;
        private readonly object _sync = new();

        public BoundedLogBuffer(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _buffer = new Queue<ReproExecutionCapturedLine>(_capacity);
        }

        public void Add(ReproExecutionStream stream, string text)
        {
            if (text is null)
            {
                return;
            }

            var entry = new ReproExecutionCapturedLine(stream, text);

            lock (_sync)
            {
                _buffer.Enqueue(entry);
                while (_buffer.Count > _capacity)
                {
                    _buffer.Dequeue();
                }
            }
        }

        public IReadOnlyList<ReproExecutionCapturedLine> ToSnapshot()
        {
            lock (_sync)
            {
                return _buffer.ToArray();
            }
        }
    }

    private void WriteOutputLine(string message)
    {
        if (SuppressConsoleLogOutput)
        {
            return;
        }

        lock (_writeLock)
        {
            _standardOut.WriteLine(message);
            _standardOut.Flush();
        }
    }

    private void WriteErrorLine(string message)
    {
        if (SuppressConsoleLogOutput)
        {
            return;
        }

        lock (_writeLock)
        {
            _standardError.WriteLine(message);
            _standardError.Flush();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.Length == 0 ? "shared" : builder.ToString();
    }

    private enum ExecutionEnvironmentKind
    {
        Local,
        LinuxContainer
    }

    private readonly record struct ExecutionEnvironmentResolution(bool Supported, ExecutionEnvironmentKind Kind, string? DockerImage, string? FailureReason);

    private readonly record struct ExecutionRunResult(int ExitCode, string? FailureReason);

    private readonly record struct ContainerInstanceResult(int ExitCode, string? FailureReason);

    private sealed class ConfigurationState
    {
        public bool Received { get; set; }

        public bool IsValid { get; set; } = true;
    }

    private readonly record struct ConfigurationExpectation(bool UseProjectReference, string? LiteDbPackageVersion);
}

/// <summary>
/// Represents the result of executing a repro variant.
/// </summary>
/// <param name="UseProjectReference">Indicates whether the run targeted the source project build.</param>
/// <param name="Reproduced">Indicates whether the repro successfully reproduced the issue.</param>
/// <param name="ExitCode">The exit code reported by the repro host.</param>
/// <param name="Duration">The elapsed time for the execution.</param>
/// <param name="CapturedOutput">The captured standard output and error lines.</param>
internal readonly record struct ReproExecutionResult(bool UseProjectReference, bool Reproduced, int ExitCode, TimeSpan Duration, IReadOnlyList<ReproExecutionCapturedLine> CapturedOutput, string? FailureReason);

/// <summary>
/// Represents a structured log entry emitted during repro execution.
/// </summary>
/// <param name="InstanceIndex">The zero-based instance index originating the log entry.</param>
/// <param name="Message">The log message text.</param>
/// <param name="Level">The severity associated with the log entry.</param>
internal readonly record struct ReproExecutionLogEntry(int InstanceIndex, string Message, ReproHostLogLevel Level);

/// <summary>
/// Identifies the stream that produced a captured line of output.
/// </summary>
internal enum ReproExecutionStream
{
    StandardOutput,
    StandardError
}

/// <summary>
/// Represents a captured line of standard output or error for report generation.
/// </summary>
/// <param name="Stream">The source stream for the line.</param>
/// <param name="Text">The raw text captured from the process.</param>
internal readonly record struct ReproExecutionCapturedLine(ReproExecutionStream Stream, string Text);
