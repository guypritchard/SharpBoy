# GB.Sdk

Reference-only .NET 10 declarations for SharpBoy's restricted C#-to-Game-Boy
language. Install this package in a project for C# validation and IntelliSense.
Host method bodies deliberately throw guard exceptions; only `gbc` translates
their exact SDK symbols into native ROM instructions. No sample assets are in
this assembly. See https://github.com/guypritchard/SharpBoy for documentation.
