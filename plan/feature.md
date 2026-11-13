# Feature: net10-dependency-lines (#479)

4.6.6 raised the net10.0 dependency groups to the .NET 11 package line by a dependency sweep. Put them back on 10.x, and guard against a repeat.

- net10.0 references back on 10.x (10.0.15, Microsoft.Extensions.AI and AI.OpenAI 10.11.0, the latest 10.x).
- `Directory.Build.targets` fails restore/build when a net10.0 project references an 11.x Microsoft.* or System.* package.
- Release as 4.6.7 after merge (the owner triggers it).
