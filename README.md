# GEffectsLogic

A physiological model for simulating G-force effects on humans. Originally developed for
the [KSA GEffects mod](https://github.com/AMPW-german/KSAGEffects), designed as a general framework for G-effects
simulation. Inspired by
the [KSP G effects mod](https://forum.kerbalspaceprogram.com/topic/113341-130-122-g-effects-blackouts-redouts-g-locs-v042-2017-jun-25/).

## Simulated Effects

- Blackout/Redout (loss of vision due to blood pooling)
- Push-Pull Effect (frontal/lateral G-force influence on tolerance)
- GLoC (G-induced Loss of Consciousness)
- Vision effects (grayscale, tunnel vision, redout, blur, and film grain)

## Model Structure

The physiological model uses a 3-compartment blood flow simulation with hydrostatic pressure dynamics:

- Head compartment: Critical for consciousness and vision
- Core compartment (thorax/heart): Central circulation hub
- Lower body compartment (abdomen + legs): Primary pooling site for Gz+

Key mechanisms:

- Hydrostatic pressure shifts blood based on applied G-forces
- Baroreceptor reflex increases heart rate to compensate for perfusion deficit
- Cerebral autoregulation provides protection at moderate G-levels
- G-suit simulation reduces blood pooling in lower body
- Straining maneuvers increase effectiveness through compression

## Implementation Status

Gz+ forces are fully modeled with realistic response curves. Consciousness mapping is based on brain oxygen saturation and head blood.\
Visual effects (grayscale, tunnel vision, redout, Gaussian blur, film grain, and the LoC override) are implemented for Gz. Gx/Gy forces are stubbed for expansion.

## Interface

### Input per Frame

- `deltaTime` (double): Time elapsed since last update (in seconds)
- `currentGx` (double): Lateral G-force (front-back axis)
- `currentGy` (double): Side-to-side G-force
- `currentGz` (double): Vertical G-force (primary axis; positive = feet-to-head)

### Output

Physiological parameters produced by each update:

- `ConsciousnessLevel` (0.0–1.0): physiological consciousness reserve
- `IsUnconscious` (bool): hysteretic state entered at 0.1 consciousness and cleared above 0.5
- `VisualTunnelVisionLevel` (0.0–1.0): positive-G hypoperfusion effect; 1.0 indicates complete tunnel closure
- `VisualRedoutLevel` (0.0–1.0): negative-G head-overfill effect
- `VisualLoCLevel` (0.0–1.0): final client-side black-screen override; 1.0 while unconscious
- `VisualGrayscaleLevel` (0.0–1.0): grayscale intensity
- `VisualFilmGrainLevel` (0.0–1.0): film-grain intensity derived from tunnel vision
- `VisualBlurLevel` (0.0–1.0): blur intensity

Clients should render the physiological visual channels independently, then apply `VisualLoCLevel` last as the full-screen black override. Display colors are client-configurable; black tunnel closure and red redout are the intended defaults.

## Configuration

All physiological parameters are configurable through LogicSettings static properties:

- Blood distribution and hydrostatic shift rates
- Brain oxygen thresholds and depletion/recovery rates
- Consciousness mapping parameters
- Straining and G-suit effectiveness
- Vision effect timing
- Stabilization thresholds for optimization

## Sequence Notation

The sequence notation is as follows:\
[Axis (Gz default) startG endG duration]\
The axis can be ommited if it is Gz. So [0 5 2] is the same as [Gz 0 5 2].\
Multiple sequences can be chained together by separating them with a comma. For example: [1 5 5],[5 1 5]\
For chained sequences the startG value can be ommited for all but the first sequence. This then uses the endG value of the previous sequence as startG value, e.g. [1 5 5], [1 5]\
It's also possible to ommit the startG and endG values. This then adds a plateau phase, e.g. [1 5 5],[5]\
A hyphen can be used as infinite duration for plateau phases, e.g. [1 5 5],[-]\
Multi axial sequences are seperated by a semicolon, e.g. [Gz 1 5 5];[Gx 0 5 5]

## Testing

Unit tests in GEffectLogicTests validate G-force response and GLoC timing. GLoCDurationTests verifies loss of consciousness timing across various profiles.\
Target behavior: 1→5 Gz+ ramp over 5 seconds reaches loss of consciousness in 20–30 seconds.

## Coordinate System

Pilot-body-centric frame of reference:

- +Gz: Headward-to-footward (eyeballs down, typical blackout)
- -Gz: Footward-to-headward (eyeballs up, typical redout)
- +Gx: Chest-to-back (eyeballs in, pilot pushed into seat)
- -Gx: Back-to-chest (eyeballs out)
- Gy: Lateral acceleration

## Stability Optimization

The system includes stabilization to reduce computation during steady-state conditions.
When G-forces and physiological values remain within thresholds for 30+ seconds, physics updates are cached and skipped.
Stabilization is automatically re-entered when conditions deviate beyond tolerance.

## Architecture

Main classes:

- GEffectsLogicInstance: Per-instance interface for updates and state management
- PhysiologicalModel: Core simulation engine for blood distribution and consciousness calculation
- LogicSettings: Centralized configuration for all physiological parameters
- Logger: Debugging and event logging system

## Performance

The `GEffectsLogicInstance.Update` execution-time budget is at most 0.1 ms per frame on average, with no measured frame above 0.5 ms. The representative 1,000-frame workload uses smooth mathematical curves for Gz and delta time: 2.5–5% of frames are below 100 ms, at least 50% are between 150 and 300 ms, and 2.5–5% exceed 1 second. The measurement covers the complete non-`PERFDEBUG` update with logging output disabled. This is only the absolute maximum the tests allow per instance update, the actual execution time is typically much lower.

The per-instance memory budget is 1 KB of managed construction allocation for a parameterless `GEffectsLogicInstance` and its owned `PhysiologicalModel`; an externally supplied logger is excluded. This deterministic allocation check runs with the normal tests. Since individual execution times are sensitive to host scheduling, run the timing check explicitly in a Release build:

`dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "Category=Performance"`

Supports high time-warp scenarios with stability detection and scales linearly with the number of instances. Optional performance profiling is available through the `PERFDEBUG` conditional.

## Logging

This library includes an abstract class that needs to be implemented by the host application for logging. It supports multiple log levels and can be configured to output to console, file, or other logging systems.\
An instance of the logger must either be set to the static Logger.Instance property or passed as local override at the GEffectsLogicInstance constructor.\
**There are no internal identifiers set. Use an inherited class to add identifiers for log messages.**

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

## Contributing

By contributing to this project, you agree to the [Contributor License Agreement](CLA.md). Please read it before submitting any contributions.