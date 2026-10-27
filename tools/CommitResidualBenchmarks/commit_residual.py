"""Fork-CI-only CP0 residual experiment; no workloads at import or outside Actions."""
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import sys

from instrument_commit import instrument

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
PHASE1 = "256cfd0b9953e65028aee864b92850911cdf32cf"
DEV = "b3c4406231908c6ca29f6d4b2769aa08351d6fc2"
# NuGet 5.0.21 nuspec repository commit; the similarly named tag has older csproj version metadata.
V5 = "391cc9318c5be6e56cb71b3ce14b1ed9cb324763"
CONFIGS = ["v5-no-checkpoint", "dev-no-checkpoint", "phase1-no-checkpoint",
           "phase1-legacy-no-checkpoint", "v5-source-no-checkpoint"]
DIAGNOSTICS = ["v5-no-checkpoint", "dev-no-checkpoint", "phase1-no-checkpoint"]


def command(args, log, timeout=600, env=None, required=True, cwd=None):
    print("RUN", " ".join(map(str, args)), flush=True)
    with Path(log).open("w", encoding="utf-8") as handle:
        result = subprocess.run(list(map(str, args)), cwd=cwd, env=env, stdout=handle,
                                stderr=subprocess.STDOUT, timeout=timeout)
    if result.returncode and required:
        print(Path(log).read_text(errors="replace")[-10000:], flush=True)
        raise RuntimeError(f"Exit {result.returncode}: {args}; log {log}")
    return result.returncode


def build(root, framework, project, output, name, legacy=False):
    if legacy:
        # Diagnostic/source parity baseline uses the package's netstandard2.0
        # source shape, with HAVE_SHA1_MANAGED, not a new net8-specific library.
        reference = root / "tools/CommitResidualBenchmarks/V5Source/V5Source.csproj"
        reference.parent.mkdir(parents=True, exist_ok=True)
        command(["dotnet", "build", root / "LiteDB/LiteDB.csproj", "-c", "Release", "-f", "netstandard2.0",
                 "-p:TargetFrameworks=netstandard2.0", "-p:TestingEnabled=false", "-p:GitVersionEnabled=false", "--nologo"],
                output / f"build-{name}-library.log", cwd=root)
        reference.write_text((HERE / "V6/V6.csproj").read_text().replace(
            "<DefineConstants>$(DefineConstants);DEV</DefineConstants>", "").replace(
            '<ProjectReference Include="../../../LiteDB/LiteDB.csproj" />',
            '<Reference Include="LiteDB"><HintPath>../../../LiteDB/bin/Release/netstandard2.0/LiteDB.dll</HintPath></Reference>'))
        project = "V5Source"
    command(["dotnet", "build", root / f"tools/CommitResidualBenchmarks/{project}/{project}.csproj",
             "-c", "Release", "-f", framework, "-p:TestingEnabled=false",
             "-p:GitVersionEnabled=false", "--nologo"], output / f"build-{name}.log", cwd=root)
    return root / f"tools/CommitResidualBenchmarks/{project}/bin/Release/{framework}/{project}.dll"


def checkout(output, name, ref):
    root = output / f"{name}-source"
    command(["git", "worktree", "add", "--detach", root, ref], output / f"checkout-{name}.log", cwd=REPO)
    destination = root / "tools/CommitResidualBenchmarks"
    shutil.copytree(HERE, destination, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
    return root


def run_child(config, round_number, output, binaries, ref, prefix="round", repetitions=1, wrapper=None):
    stem = output / f"{prefix}-{round_number}-{config}"
    env = dict(os.environ, DOTNET_TieredCompilation="0", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               P1D_LIBRARY_REF=ref, P1D_REPETITIONS=str(repetitions))
    args = ["dotnet", binaries[config], config, str(round_number), stem.with_suffix(".json")]
    if wrapper: args = wrapper(stem, args)
    command(args, stem.with_suffix(".log"), env=env)
    return stem


def summarize(output, rounds):
    rows = []
    for prefix in ["round", "component"]:
        grouped = {}
        for path in output.glob(prefix + "-*.json"):
            result = json.loads(path.read_text())
            grouped.setdefault(result["config"], []).append(result)
        for config, results in grouped.items():
            if len(results) != (rounds if prefix == "round" else 6):
                raise ValueError(f"Incomplete {prefix}/{config}: {len(results)}")
            samples = [r["samples"][0] for r in results]
            ms = [s["milliseconds"] / 5000 for s in samples]
            row = dict(kind=prefix, config=config, msPerCommit=statistics.median(ms),
                       min=min(ms), max=max(ms), cpuMsPerCommit=statistics.median(s["cpuMs"] / 5000 for s in samples),
                       allocatedBytesPerCommit=statistics.median(s["allocatedBytes"] / 5000 for s in samples),
                       hashes=sorted(set(r["assemblySha256"] for r in results)))
            if len(row["hashes"]) != 1: raise ValueError("Assembly changed between rounds")
            if prefix == "component":
                parts = [s["components"]["rows"] for s in samples]
                row["components"] = sorted([
                    dict(component=p["component"], msPerCommit=statistics.median(
                        next(v["milliseconds"] for v in sample if v["component"] == p["component"]) / 5000
                        for sample in parts), callsPerCommit=statistics.median(
                        next(v["calls"] for v in sample if v["component"] == p["component"]) / 5000 for sample in parts))
                    for p in parts[0]], key=lambda p: -p["msPerCommit"])
                for sample in samples:
                    measured = sample["components"]
                    if abs(sum(p["milliseconds"] for p in measured["rows"]) - measured["totalMs"]) > .001:
                        raise ValueError("Component partition does not sum to window")
                    forbidden = [p for p in measured["rows"] if p["component"] in ["DEVICE-SYNC", "CHECKPOINT", "GROUP-ACTIVE"] and p["calls"]]
                    if forbidden: raise ValueError("Unexpected cache-only barrier: " + repr(forbidden))
            rows.append(row)
    (output / "commit-summary.json").write_text(json.dumps(rows, indent=2))
    lines = ["# CP0 commit residual", "", "All values ms/commit; 5,000 commits per fresh process.",
             "Component figures are perturbed exclusive wall timers from separately instrumented source trees.",
             "Never add sampled inclusive contexts or treat these diagnostic times as achievable savings.", "",
             "| Kind | Engine | Median | Min | Max | CPU | Allocated B |", "|---|---|---:|---:|---:|---:|---:|"]
    for row in rows:
        lines.append(f"| {row['kind']} | {row['config']} | {row['msPerCommit']:.6f} | {row['min']:.6f} | {row['max']:.6f} | {row['cpuMsPerCommit']:.6f} | {row['allocatedBytesPerCommit']:.1f} |")
        if "components" in row:
            lines += ["", row["config"], "", "| Component | ms/commit | calls/commit |", "|---|---:|---:|"]
            lines += [f"| {p['component']} | {p['msPerCommit']:.6f} | {p['callsPerCommit']:.3f} |" for p in row["components"]]
    (output / "commit-summary.md").write_text("\n".join(lines) + "\n")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        Path(os.environ["GITHUB_STEP_SUMMARY"]).write_text("\n".join(lines) + "\n")


def main():
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("GITHUB_REPOSITORY") != "JKamsker/LiteDB":
        raise RuntimeError("P1-K measurements run on fork CI only")
    framework, destination = sys.argv[1:]
    output = Path(destination).resolve()
    output.mkdir(parents=True, exist_ok=True)
    command(["git", "diff", "--exit-code", PHASE1, "--", "LiteDB", "Directory.Build.props", "Directory.Build.targets"],
            output / "verify-phase1.log", cwd=REPO)
    metadata = dict(phase1=PHASE1, dev=DEV, v5=V5, framework=framework,
                    platform=platform.platform(), cpuCount=os.cpu_count(),
                    dotnetInfo=subprocess.check_output(["dotnet", "--info"], text=True),
                    runner={k: os.environ.get(k) for k in ["RUNNER_OS", "RUNNER_ARCH", "ImageOS", "ImageVersion", "GITHUB_SHA"]},
                    hashes={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.glob("*") if p.is_file()})
    (output / "environment.json").write_text(json.dumps(metadata, indent=2))
    binaries = {}
    phase = build(REPO, framework, "V6", output, "phase1")
    binaries["phase1-no-checkpoint"] = binaries["phase1-legacy-no-checkpoint"] = phase
    binaries["v5-no-checkpoint"] = build(REPO, framework, "V5", output, "v5")
    devroot = checkout(output, "dev", DEV)
    binaries["dev-no-checkpoint"] = build(devroot, framework, "V6", output, "dev")
    v5root = checkout(output, "v5", V5)
    binaries["v5-source-no-checkpoint"] = build(v5root, framework, "V5Source", output, "v5-source", legacy=True)
    refs = {c: DEV if c.startswith("dev") else V5 if "v5" in c else PHASE1 for c in CONFIGS}
    rounds = 12 if os.environ["RUNNER_OS"] == "macOS" else 6
    for round_number in range(1, rounds + 1):
        for config in CONFIGS if round_number % 2 else reversed(CONFIGS):
            run_child(config, round_number, output, binaries, refs[config])
    # Summaries are still written if later diagnostic collection fails.
    summarize(output, rounds)
    diag = {}
    for name, ref in [("v5", V5), ("dev", DEV), ("phase1", PHASE1)]:
        root = checkout(output, "diag-" + name, ref)
        instrument(root, output / f"instrumentation-{name}.json")
        config = name + "-no-checkpoint"
        diag[config] = build(root, framework, "V6", output, "diag-" + name, legacy=name == "v5")
    for round_number in range(1, 7):
        for config in DIAGNOSTICS if round_number % 2 else reversed(DIAGNOSTICS):
            run_child(config, round_number, output, diag, refs[config], prefix="component")
    summarize(output, rounds)
    if os.environ["RUNNER_OS"] == "Linux":
        profiles = subprocess.check_output(["dotnet-trace", "list-profiles"], text=True)
        (output / "trace-profiles.txt").write_text(profiles)
        profile = "dotnet-sampled-thread-time" if "dotnet-sampled-thread-time" in profiles else "cpu-sampling"
        for config in DIAGNOSTICS:
            def trace(stem, args):
                return ["dotnet-trace", "collect", "--profile", profile,
                        "--providers", "Microsoft-Windows-DotNETRuntime:0x1:5", "--buffersize", "256",
                        "--output", str(stem) + ".nettrace", "--", *args]
            # Original, UNINSTRUMENTED libraries. Three independent measured fixtures;
            # stable Measurement.Single frames exclude all warmup/setup/verification.
            stem = run_child(config, 0, output, binaries, refs[config], prefix="trace", repetitions=3, wrapper=trace)
            command(["dotnet-trace", "convert", str(stem) + ".nettrace", "--format", "Speedscope", "--output", stem],
                    str(stem) + "-convert.log")
            def strace(stem, args):
                return ["strace", "-f", "-ttt", "-T", "-yy", "-s", "256", "-e",
                        "trace=write,pwrite64,writev,pwritev,pwritev2,fsync,fdatasync,ftruncate,fallocate,lseek,fstat,newfstatat,fcntl",
                        "-o", str(stem) + ".txt", *args]
            run_child(config, 0, output, binaries, refs[config], prefix="strace-detail", wrapper=strace)
        command(["dotnet", "run", "--project", HERE / "TraceRead/TraceRead.csproj", "-c", "Release", "--", output],
                output / "trace-read.log")
        from analyze_commit import diagnostics
        diagnostics(output)


if __name__ == "__main__":
    main()
