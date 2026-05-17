# Copilot Instructions

## Project Guidelines
- For this G-effects model, the desired behavior is that a 1→5 Gz+ ramp over 5 seconds should not reach full unconsciousness in ~10 seconds; target loss-of-consciousness timing should be closer to 20–30 seconds (without fatigue modeled yet).

## Visibility Semantics
- TunnelVisionLevel semantics: 1 means no visibility left, and 0.5 means half of the field of view is still free. TunnelVisionLevel should reach 1 when ConsciousnessLevel is close to 0, and tunnel vision should not be driven too directly by short perfusion recovery dips.