# Goal:
Creating an independet logic system for simulating highly realistic g effects on humans

## Simulated effects:
- Blackout/Redout
- Push-Pull effect
- GLoC
- ALoC
- G induced Death

## Inputs:
- deltaTime: double - Time elapsed since the last update (in seconds)
- gForce vector: Vector3 - The current g-force vector applied to the human body (in g's)
- Human position?: maybe if the human/kitten sits or lies down because that changes the effect of g-forces on the body although it'll be hard to determine that without user input

## Outputs:
- consiousnessLevel: double (0.0 to 1.0, -1.0 for death)
- confusionLevel: double (0.0 to 1.0)
- TunnelVisionLevel: double (0.0 to 1.0)
- GreyScaleLevel: double (0.0 to 1.0)
- Color: bool (black vs red)

## Internal data:
- PerfusionLevel: double (0.0 to 1.0) - The level of blood perfusion in the brain, which affects consciousness and vision
- Time
- FatigueLevel: double (0.0 to 1.0) - The level of fatigue in the body, which can affect tolerance to g-forces (for future implementation)

## Settings:
- Gz+ tolerance reduction through push/pull effect (Gz is vertical axis, Gx is front-back axis, Gy is side-side axis)
- Gz+ tolerance
- Gz- tolerance
- Max Dt timestep for updates (to prevent instability in the simulation)
