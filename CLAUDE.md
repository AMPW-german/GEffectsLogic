# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GEffectsLogic is a C# (.NET 10) backend framework for simulating realistic G-force effects on humans. It was developed for the KSA GEffects mod but is designed as a general-purpose library. The project is early WIP — currently only the Gz+ (positive vertical G) model is partially implemented.

## Commands

```bash
dotnet restore          # Restore NuGet dependencies
dotnet build            # Build all projects in the solution
dotnet test             # Run all xUnit tests
```

To run a single test by name:
```bash
dotnet test --filter "FullyQualifiedName~Test1"
```

To run the WPF GUI test app (interactive visualization, Windows only):
```bash
dotnet run --project GraphicLogicTest
```

## Architecture

The solution (`GEffectsLogic.slnx`) contains three projects:

**GEffectsLogic** (core library) — The only production-relevant project. After build, the output is automatically copied to `$(SolutionDir)Content\KSAGEffects\` (post-build step in the csproj).

**GEffectLogicTests** — xUnit tests. Each test constructs a `GEffectsLogic` instance and calls `Update(deltaTime, gx, gy, gz)` in a loop to simulate a G-force scenario.

**GraphicLogicTest** — WPF app (OxyPlot graphs + sliders) for visually tuning the algorithm. Not part of CI.

### Core Logic (`GEffectsLogic/GEffectsLogic.cs`)

`GEffectsLogic` is a per-entity state object (one instance per "vessel/pilot"). Instances are tracked in a static `Dictionary<int, GEffectsLogic>` with auto-generated integer IDs.

The main entry point is `Update(double deltaTime, double gx, double gy, double gz)`. Currently only `gz` is used. The algorithm:
1. Accumulates `gz²× deltaTime` into `cummulatedGz`
2. Applies exponential decay: `cummulatedGz -= e^(tolerance × cummulatedGz) × deltaTime`
3. Maps `cummulatedGz` to output fields: `ConsiousnessLevel`, `ConfusionLevel`, `TunnelVisionLevel`, `GreyScaleLevel`, `PrimaryColor`

Tolerance constants (`GzPTolerance`, `GzMTolerance`, etc.) live in the static `LogicSettings` class and control how quickly effects build up and decay.

### Logging Pattern

`Logger` (in `GEffectsLogic/Logging/Logger.cs`) is an abstract class with a static `Instance` singleton. Every project that references the core library must supply a concrete subclass and assign it to `Logger.Instance` before calling `Update`. The test project uses an xUnit `ITestOutputHelper`-backed implementation; the WPF app uses a colored console implementation. This is the standard extension point for new consumers of the library.

### Algorithm Specification

`GEffectsLogic/Logic.md` is the authoritative spec for the intended physics model (blackout/redout, Push-Pull, GLoC, ALoC, G-induced death). Read it before modifying the `Update` algorithm or adding new G-axis support.
