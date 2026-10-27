"""Runner-only P1-D orchestration. Importing/analyzing this module runs no workloads."""
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
DEV = "b3c4406231908c6ca29f6d4b2769aa08351d6fc2"
WRITE_BASE = "7b71bc4dd45b48a1df4418ddb4cd245832273b35"
WRITE_PROTO = "0538d2b95de61d6a4d1932f240e51ffeef40ef30"
CONFIGS = ["v5", "dev-fast", "dev-legacy", "dev-pages", "dev-cache", "dev-throughput",
           "dev-legacy-pages-cache", "v5-no-checkpoint", "dev-no-checkpoint",
           "write-base-fast", "write-proto-fast"]
DIAGNOSTICS = ["v5", "dev-fast", "dev-legacy", "dev-pages", "write-base-fast", "write-proto-fast"]


def command(args, log, env=None, timeout=300, required=True, cwd=None):
    print("RUN", " ".join(map(str, args)), flush=True)
    with Path(log).open("w", encoding="utf-8") as handle:
        result = subprocess.run(list(map(str, args)), stdout=handle, stderr=subprocess.STDOUT,
                                env=env, timeout=timeout, cwd=cwd)
    if required and result.returncode:
        print(Path(log).read_text(encoding="utf-8", errors="replace")[-12000:], flush=True)
        raise RuntimeError(f"Command failed ({result.returncode}): {args}; see {log}")
    return result.returncode


def variant(config):
    if config.startswith("v5"):
        return "v5", "V5", "nuget:5.0.21"
    if config.startswith("write-base"):
        return "write-base", "V6", WRITE_BASE
    if config.startswith("write-proto"):
        return "write-proto", "V6", WRITE_PROTO
    return "dev", "V6", DEV


def build(framework, output):
    roots = {"dev": REPO, "v5": REPO}
    command(["git", "diff", "--exit-code", DEV, "--", "LiteDB", "Directory.Build.props",
             "Directory.Build.targets"], output / "verify-pinned-dev.log", cwd=REPO)
    for name, ref in [("write-base", WRITE_BASE), ("write-proto", WRITE_PROTO)]:
        root = output / (name + "-source")
        command(["git", "worktree", "add", "--detach", root, ref], output / (name + "-checkout.log"), cwd=REPO)
        destination = root / "tools" / "Ab5021Benchmarks"
        shutil.copytree(HERE, destination, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
        roots[name] = root
    for name, root in roots.items():
        project = "V5" if name == "v5" else "V6"
        command(["dotnet", "build", root / "tools/Ab5021Benchmarks" / project / (project + ".csproj"),
                 "-c", "Release", "-f", framework, f"-p:TargetFrameworks={framework}",
                 "-p:TestingEnabled=false", "-p:GitVersionEnabled=false", "--nologo"],
                output / (name + "-build.log"), timeout=600, cwd=root)
    return roots


def child(config, round_number, result, roots, framework):
    name, project, ref = variant(config)
    dll = roots[name] / "tools/Ab5021Benchmarks" / project / "bin/Release" / framework / (project + ".dll")
    env = dict(os.environ, DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               P1D_LIBRARY_REF=ref)
    return ["dotnet", dll, config, str(round_number), result], env


def diagnostics(roots, framework, output):
    failures = []
    profiles = subprocess.check_output(["dotnet-trace", "list-profiles"], text=True)
    (output / "trace-profiles.txt").write_text(profiles)
    profile = "dotnet-sampled-thread-time" if "dotnet-sampled-thread-time" in profiles else "cpu-sampling"
    for config in DIAGNOSTICS:
        stem = output / ("trace-" + config)
        args, env = child(config, 0, stem.with_suffix(".json"), roots, framework)
        env["P1D_REPETITIONS"] = "3"
        # EventPipe samples runnable AND blocked threads. Never label these CPU-only shares.
        trace = ["dotnet-trace", "collect", "--profile", profile,
                 "--providers", "Microsoft-Windows-DotNETRuntime:0x1:5", "--buffersize", "256",
                 "--output", str(stem) + ".nettrace", "--", *args]
        if command(trace, str(stem) + ".log", env, timeout=420, required=False):
            failures.append("trace:" + config)
        else:
            if command(["dotnet-trace", "convert", str(stem) + ".nettrace", "--format", "Speedscope",
                        "--output", str(stem)], str(stem) + "-convert.log", timeout=180, required=False):
                failures.append("convert:" + config)
        env.pop("P1D_REPETITIONS")
        args, env = child(config, 0, output / ("counter-" + config + ".json"), roots, framework)
        counters = ["dotnet-counters", "collect", "--counters", "System.Runtime", "--refresh-interval", "1",
                    "--format", "csv", "--output", output / ("counters-" + config + ".csv"), "--", *args]
        if command(counters, output / ("counters-" + config + ".log"), env, timeout=240, required=False):
            failures.append("counters:" + config)
        if sys.platform.startswith("linux"):
            for mode in ["summary", "detail"]:
                args, env = child(config, 0, output / (f"strace-{mode}-{config}.json"), roots, framework)
                options = ["-f", "-c"] if mode == "summary" else [
                    "-f", "-ttt", "-T", "-yy", "-s", "256", "-e",
                    "trace=write,pwrite64,writev,pwritev,pwritev2,fsync,fdatasync,ftruncate,fallocate,lseek"]
                trace = ["strace", *options, "-o", output / (f"strace-{mode}-{config}.txt"), *args]
                if command(trace, output / (f"strace-{mode}-{config}.log"), env, required=False):
                    failures.append(f"strace-{mode}:{config}")
    if sys.platform.startswith("linux") and shutil.which("perf"):
        for config in ["v5", "dev-fast"]:
            args, env = child(config, 0, output / ("perf-" + config + ".json"), roots, framework)
            env.update(DOTNET_PerfMapEnabled="1", COMPlus_PerfMapEnabled="1")
            path = output / ("perf-" + config + ".data")
            code = command(["perf", "record", "-F", "499", "-e", "cpu-clock", "-g",
                            "--call-graph", "dwarf,16384", "-o", path, "--", *args],
                           output / ("perf-" + config + ".log"), env, required=False)
            if code == 0:
                command(["perf", "script", "-i", path], output / ("perf-" + config + ".stacks"), required=False)
                for symbol_map in Path("/tmp").glob("perf-*.map"):
                    shutil.copy2(symbol_map, output / symbol_map.name)
            else:
                failures.append("kernel-cpu-sampling:" + config)
    (output / "diagnostic-failures.json").write_text(json.dumps(failures, indent=2))
    return failures


def main():
    if os.environ.get("GITHUB_ACTIONS") != "true":
        raise RuntimeError("P1-D runs only on GitHub Actions; no local workload execution")
    framework, destination = sys.argv[1:]
    output = Path(destination).resolve()
    output.mkdir(parents=True, exist_ok=True)
    metadata = dict(framework=framework, platform=platform.platform(), cpuCount=os.cpu_count(),
                    dev=DEV, prototype=WRITE_PROTO, prototypeBase=WRITE_BASE,
                    runner={k: os.environ.get(k) for k in ["RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion", "GITHUB_SHA"]},
                    dotnetInfo=subprocess.check_output(["dotnet", "--info"], text=True),
                    hashes={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.glob("*.cs")},
                    order=[])
    if os.environ.get("RUNNER_OS") == "Linux":
        metadata["diagnosticVersions"] = {
            tool: subprocess.check_output([tool, "--version"], text=True, stderr=subprocess.STDOUT)
            for tool in ["dotnet-trace", "dotnet-counters", "strace", "perf"]}
    roots = build(framework, output)
    started = time.monotonic()
    for round_number in range(1, 7):
        for config in CONFIGS if round_number % 2 else reversed(CONFIGS):
            if time.monotonic() - started > 2400:
                raise TimeoutError("40 minute uninstrumented measurement budget exceeded")
            stem = output / f"round-{round_number}-{config}"
            args, env = child(config, round_number, stem.with_suffix(".json"), roots, framework)
            command(args, stem.with_suffix(".log"), env)
            metadata["order"].append(dict(round=round_number, config=config))
            (output / "environment.json").write_text(json.dumps(metadata, indent=2))
    # Save the useful uninstrumented evidence even if a diagnostic tool later fails.
    from analyze_attribution import analyze
    analyze(output)
    failures = diagnostics(roots, framework, output) if os.environ.get("RUNNER_OS") == "Linux" else []
    analyze(output)
    # Worktrees/build outputs are not uploaded; only identities, measurements and diagnostics.
    for name in ["write-base", "write-proto"]:
        command(["git", "worktree", "remove", "--force", roots[name]], output / (name + "-cleanup.log"), cwd=REPO)
    if failures:
        raise RuntimeError("Diagnostic gaps (raw data preserved): " + ", ".join(failures))


if __name__ == "__main__":
    main()
