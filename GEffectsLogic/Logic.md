# GEffectsLogic - Internal Design Documentation

## Overview

A physiological simulation engine that models G-force effects on the human body. Uses a 3-compartment blood flow model to compute consciousness, vision effects, and other physiological responses to acceleration.

## Design Philosophy

### 3-Compartment Blood Model

Three distinct blood compartments:
1. Head: Critical for consciousness and vision
2. Core (thorax/heart): Central circulation hub
3. Lower Body (abdomen + legs): Primary reservoir for blood pooling under +Gz

G-forces create hydrostatic pressure gradients that shift blood between compartments. Higher G-forces pool blood away from the head, reducing brain perfusion and oxygen delivery.

Blood volume is conserved across updates: bloodHead + bloodCore + bloodLower = 1.0

## Core Physiological Processes

### Hydrostatic Blood Shift (Gz Primary)

When positive Gz is applied, hydrostatic pressure pushes blood toward the lower body. Modeled as a power function:

shift = hydrostaticShiftRate × Gz^hydrostaticShiftExponent

Higher exponent means early G-levels have less effect, but high-G effects are severe (non-linear tolerance).

Parameters:
- HydrostaticShiftRate: Base rate (approximately 0.0053)
- HydrostaticShiftExponent: Non-linearity (approximately 2.2)

### Autoregulation and Passive Return

The body responds to blood loss from the head:
- Passive return: Natural circulatory pressure returns blood (approximately 27% rate)
- Cerebral autoregulation: Vessel dilation/constriction maintains head perfusion above a threshold, limited to approximately 0.65G additional tolerance beyond resting 1G baseline
- Baroreceptor reflex: Detects low head perfusion and increases heart rate to boost circulation

Parameters:
- PassiveReturnRate: Return fraction (approximately 0.27)
- CerebralAutoregulationGzTolerance: Autoregulation limit (approximately 0.65G)
- BaroreceptorTimeConstant: Reflex response speed (approximately 3.8s)

### Straining and G-Suit

Pilot effort and equipment provide protection:
- Straining: Anti-G maneuvers increase with sustained G-force
  - Starts ramping at StrainingStartGz (approximately 1.5G)
  - Reaches max at StrainingFullGz (approximately 2.5G)
- G-Suit: Mechanical compression reduces blood pooling
  - Effectiveness scales with straining level
  - Reduces blood shift by up to GSuitCoreLowerReductionMax (approximately 60%)
  - Effectiveness: GSuitEffectiveness (approximately 0.3 = 30% perfect compression)

### Brain Oxygen Model

Consciousness depends on oxygen delivery to the brain, not just blood perfusion.

#### Perfusion to O2 Delivery (Non-Linear)

Higher perfusion delivers O2 nonlinearly with diminishing returns at high perfusion. Formula: o2Delivery = perfusionLevel ^ o2PerfusionExponent

Hypoperfusion penalty: Sustained mild perfusion loss (0.5–0.9) incurs additional penalty to prevent false consciousness plateau.

#### O2 Depletion Dynamics

- Depletion: Brain O2 drops with time constant based on perfusion severity
  - Mild loss: BrainO2DepletionTauMild (approximately 12.5s)
  - Severe loss: BrainO2DepletionTauSevere (approximately 4.5s)
  - Floor: BrainO2Floor (approximately 0.18) prevents complete collapse
- Recovery: When perfusion improves, O2 recovers with BrainO2RecoveryTau (approximately 9.0s)

### Consciousness Mapping

Brain O2 saturation maps to consciousness level through a non-linear curve:

- High O2 (>0.8): Fully conscious (level = 1.0)
- Mid O2 (0.3–0.8): Consciousness drops dynamically
  - Incorporates perfusion state, not just O2
  - Non-linear loss: o2Deficit ^ consciousnessLossSeverityExponent (approximately 2.9)
  - Subtractive bias prevents artificial plateau at mid-G
- Low O2 (<0.3): Critical collapse gate
  - Rapid loss when both O2 and perfusion are critically low
  - Tau multiplier drops dramatically to speed unconsciousness

Key thresholds:
- ConsciousnessLossTauMin: approximately 5s (fastest loss at critical conditions)
- ConsciousnessLossTauMax: approximately 24s (slowest loss at mild conditions)
- ConsciousnessCriticalO2Norm: approximately 0.28 (critical O2 threshold)

### Vision Effects

Vision loss occurs in stages as perfusion degrades.

#### Grey-Scale Vision

Onset when perfusion drops below approximately 0.7. Increases with further perfusion loss.

#### Tunnel Vision

- Onset when perfusion drops below approximately 0.5
- Semantic: Level 1.0 = complete blackout, 0.5 = 50% field still visible
- Faster buildup than recovery: VisualInTau (approximately 2.0s) vs VisualOutTau (approximately 7.5s)
  - Short perfusion rebounds do not immediately restore vision
  - Mimics physiology where vision recovery lags circulation recovery

#### Color Inversion (Blackout vs Redout)

- Positive Gz (+5G): Blood pools in legs, eyes down, blackout (black vision)
- Negative Gz (-3G): Blood pools in head, eyes up, redout (red/inverted vision)

### Stability Optimization

For long-duration steady states 「e.g., orbital flight」:

Stabilization Conditions:
1. G-force components remain within ±0.05 of stabilized values
2. All physiological values remain within ±0.025 of recorded values
3. Both conditions hold for ≥30 seconds

When Stable:
- Physics updates are skipped (no computation)
- Values are cached
- Automatically re-entered on deviation

Purpose: Enables high time-warp without instability or computational waste

## Settings Overview

All behavior is controlled through LogicSettings static properties.

### Hydrostatic and Circulation
- HydrostaticShiftRate, HydrostaticShiftExponent: G-force to blood shift
- PassiveReturnRate: Natural circulation return speed
- BaroreceptorTimeConstant: Heart rate response delay
- CerebralAutoregulationGzTolerance: Autoregulation G-capacity

### Straining and G-Suit
- StrainingStartGz, StrainingFullGz, StrainingTau: Straining ramp
- GSuitEffectiveness: G-suit compression quality (0–1)
- GSuitCoreLowerReductionMax, GSuitLowerReturnBoostMax: Suit benefits

### Brain O2 Dynamics
- O2PerfusionCurveStrength, O2PerfusionCurvePivot: Non-linear delivery
- BrainO2DepletionTauMild, BrainO2DepletionTauSevere: Depletion speed
- BrainO2RecoveryTau: Recovery speed
- BrainO2Floor: Minimum O2 level

### Consciousness Mapping
- BrainO2Blackout, BrainO2Full: O2 thresholds for loss/recovery
- ConsciousnessLossTauMin, ConsciousnessLossTauMax: Loss speed range
- ConsciousnessRecoveryTau: Recovery speed
- ConsciousnessLossSeverityExponent: Non-linearity of loss
- ConsciousnessCriticalPerfusionNorm, ConsciousnessCriticalO2Norm: Critical thresholds
- ConsciousnessDeficitBias: Bias against mid-G plateau

### Vision Effects
- VisualInTau: Greying/tunnel buildup speed (approximately 2.0s, fast)
- VisualOutTau: Greying/tunnel recovery speed (approximately 7.5s, slow)

## Key Behaviors and Tuning Notes

### Target GLoC Timing

Per project goals:
- 1→5 Gz+ ramp over 5 seconds should NOT reach full unconsciousness in approximately 10 seconds
- Target: Loss of consciousness at 20–30 seconds
- Achieved through:
  - Brain O2 recovery tau (approximately 9s) prevents instant drop
  - Consciousness loss tau max (approximately 24s) slows decline at mild perfusion loss
  - Subtractive bias prevents over-aggressive mid-G response

### Tunnel Vision Semantics

- TunnelVisionLevel = 1.0 indicates complete blackout (no visibility)
- TunnelVisionLevel = 0.5 indicates 50% field of view remains visible
- Should reach 1.0 when ConsciousnessLevel is approximately 0
- Should not be driven too directly by short perfusion recovery dips (hence VisualOutTau > VisualInTau)

### Non-Linear Dynamics

Real human physiology exhibits threshold effects: autoregulation limits, critical perfusion collapse. Non-linear curves match medical literature better than linear models. Parameters are calibrated to match known G-tolerance studies.

## Testing Strategy

### Unit Tests 「GLoCDurationTests」

- Validate GLoC timing across various G-force profiles
- Verify consciousness level thresholds
- Check vision effect timing and semantics
- Confirm multi-axis interactions

### Manual Testing

- Use sequence notation to simulate realistic maneuvers
- Verify visual feedback and consciousness transitions
- Validate parameter changes produce expected behavioral shifts

## Future Enhancements

- Gx/Gy full modeling (currently stubbed)
- Fatigue accumulation over extended high-G exposure
- Pilot-specific tolerance variations (age, fitness, training)
- Anti-G maneuver effectiveness variations
- Long-term cardiovascular stress effects

## Architecture Notes

### Class Hierarchy

GEffectsLogicInstance: Public API for each character/vessel
- Manages per-instance state (time, last G-forces)
- Delegates physics to PhysiologicalModel
- Implements stability optimization
- Manages instance registry for multi-character scenarios

PhysiologicalModel: Core physics engine
- All blood dynamics, O2, consciousness calculations
- Reads from LogicSettings for parameters
- Emits Logger events for debugging

LogicSettings: Centralized configuration
- Static properties for all tunable parameters
- Enables hot-tuning during play/testing
- Source of truth for physiological constants

### Logging and Debugging

- Built-in Logger for event tracking
- Optional performance profiling via #if PERFDEBUG
- DebugMode and SuppressInfoLogs flags in LogicSettings

## Performance Characteristics

- Per-frame cost: approximately 0.05-0.1ms per instance on modern hardware
- Stability: Robust to time-steps 0.01–1s (auto-subdivides if larger)
- Scaling: Linear with number of instances (100+ characters feasible)
