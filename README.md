This project is dedicated to creating a backend for highly realistic g effects.

While it's developed for the [KSA GEffects mod](https://github.com/AMPW-german/KSAGEffects) it is inteded as a general framework for simulating g effects not limited to any specific project.\
It is inspired by the [KSP g effects mod](https://forum.kerbalspaceprogram.com/topic/113341-130-122-g-effects-blackouts-redouts-g-locs-v042-2017-jun-25/).

Currently there are only Gz+ forces modeled. The response curves not perfect but good enough for a first implementation in KSA.

This project uses a highly simplifyed blod flow/pooling simulation which should give better results compared to the KSP G Effects mod which uses cumulative variables and sharp cutoffs.

## Sequence notation

The sequence notation is as follows:
[Axis (Gz default) startG endG duration]
The axis can be ommited if it is Gz. So [0 5 2] is the same as [Gz 0 5 2].\
Multiple sequences can be chained together by separating them with a comma. For example: [1 5 5],[5 1 5]\
For chained sequences the startG value can be ommited for all but the first sequence. This then uses the endG value of the previous sequence as startG value, e.g. [1 5 5], [1 5]\
It's also possible to ommit the startG and endG values. This then adds a plateau phase, e.g. [1 5 5],[5]\
A hyphen can be used as infinite duration for plateau phases, e.g. [1 5 5],[-]
Multi axial sequences are seperated by a semicolon, e.g. [Gz 1 5 5];[Gx 0 5 5]

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

## Contributing

By contributing to this project, you agree to the [Contributor License Agreement](CLA.md). Please read it before submitting any contributions.
