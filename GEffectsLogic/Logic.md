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
- Cumulated Gx (double)
- Cumulated Gz (double)
- Cumulated Gy (double)
- Time

## Settings:
- Gx+ tolerance
- Gx- tolerance
- Gz+ tolerance reduction through push/pull effect (Gz is vertical axis, Gx is front-back axis, Gy is side-side axis)
- Gz+ tolerance
- Gz- tolerance
- Gy tolerance (humans are pretty much identical on both sides so only one tolerance is necessary)
- GLoC Onset time
  - Although irl for very high g forces GLoC can happen without any visual warnings this won't be implemented for now

Unlike the KSP G-Effects mod I don't want hard g limits where the g forces below it are just ignored