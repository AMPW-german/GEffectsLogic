# GEffectsLogic

A physiological model for simulating G-force effects on humans. Originally developed for the [KSA GEffects mod](https://github.com/AMPW-german/KSAGEffects), designed as a general framework for G-effects simulation. Inspired by the [KSP G effects mod](https://forum.kerbalspaceprogram.com/topic/113341-130-122-g-effects-blackouts-redouts-g-locs-v042-2017-jun-25/).

## Simulated Effects

- Blackout/Redout (loss of vision due to blood pooling)
- Push-Pull Effect (frontal/lateral G-force influence on tolerance)
- GLoC (G-induced Loss of Consciousness)
- Vision effects (grey-scale, tunnel vision, color inversion)

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

Gz+ forces are fully modeled with realistic response curves. Consciousness mapping is based on brain oxygen saturation and head blood. Vision effects (grey-scale and tunnel vision) are implemented. Gx/Gy forces are stubbed for expansion. Fatigue modeling is planned for future implementation.

## Interface

### Input per Frame

- `deltaTime` (double): Time elapsed since last update (in seconds)
- `currentGx` (double): Lateral G-force (front-back axis)
- `currentGy` (double): Side-to-side G-force
- `currentGz` (double): Vertical G-force (primary axis; positive = feet-to-head)

### Output

Physiological parameters produced by each update:

- `ConsciousnessLevel` (0.0–1.0, ≤0.0 for death)
- `TunnelVisionLevel` (0.0–1.0): 1.0 indicates complete blackout, 0.5 indicates 50% field still visible
- `GreyScaleLevel` (0.0–1.0): Greying-out intensity
- `PrimaryColor` (bool): true for normal (blackout), false for inverted (redout)

## Configuration

All physiological parameters are configurable through LogicSettings static properties:

- Blood distribution and hydrostatic shift rates
- Brain oxygen thresholds and depletion/recovery rates
- Consciousness mapping parameters
- Straining and G-suit effectiveness
- Vision effect timing
- Stabilization thresholds for optimization

## Sequence Notation

The sequence notation is as follows:
[Axis (Gz default) startG endG duration]
The axis can be ommited if it is Gz. So [0 5 2] is the same as [Gz 0 5 2].\
Multiple sequences can be chained together by separating them with a comma. For example: [1 5 5],[5 1 5]\
For chained sequences the startG value can be ommited for all but the first sequence. This then uses the endG value of the previous sequence as startG value, e.g. [1 5 5], [1 5]\
It's also possible to ommit the startG and endG values. This then adds a plateau phase, e.g. [1 5 5],[5]\
A hyphen can be used as infinite duration for plateau phases, e.g. [1 5 5],[-]
Multi axial sequences are seperated by a semicolon, e.g. [Gz 1 5 5];[Gx 0 5 5]

## Testing

Unit tests in GEffectLogicTests validate G-force response and GLoC timing. GLoCDurationTests verifies loss of consciousness timing across various profiles. Target behavior: 1→5 Gz+ ramp over 5 seconds reaches loss of consciousness in 20–30 seconds.

## Coordinate System

Pilot-body-centric frame of reference:
- +Gz: Headward-to-footward (eyeballs down, typical blackout)
- -Gz: Footward-to-headward (eyeballs up, typical redout)
- +Gx: Chest-to-back (eyeballs in, pilot pushed into seat)
- -Gx: Back-to-chest (eyeballs out)
- Gy: Lateral acceleration

## Stability Optimization

The system includes stabilization to reduce computation during steady-state conditions. When G-forces and physiological values remain within thresholds for 30+ seconds, physics updates are cached and skipped. Stabilization is automatically re-entered when conditions deviate beyond tolerance.

## Architecture

Main classes:
- GEffectsLogicInstance: Per-instance interface for updates and state management
- PhysiologicalModel: Core simulation engine for blood distribution and consciousness calculation
- LogicSettings: Centralized configuration for all physiological parameters
- Logger: Debugging and event logging system

## Performance

Real-time performance characteristics: ~0.1–0.5ms per instance per frame. Per-instance memory: ~1KB. Supports high time-warp scenarios with stability detection. Scales linearly with number of instances. Optional performance profiling via PERFDEBUG conditional.

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

## Contributing

By contributing to this project, you agree to the [Contributor License Agreement](CLA.md). Please read it before submitting any contributions.