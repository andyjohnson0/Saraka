# Saraka Project – Agent Instructions

This document takes precedence over all other instructions.


## Coding Conventions
- Follow best practices from the .NET Framework Design Guidelines as they pertain to library design.
- Follow .NET naming conventions:
  - PascalCase for classes, methods, constants
  - camelCase for locals and parameters
  - Member variables prefixed with `_`
- Comment classes, functions, etc with XML comment blocks describing usage, parameters, exceptions, etc.
- Use minimal in-code comments unless describing something that is subtle or unobvious.
- Use async where possible and appropriate.
- Enums should be in their own .cs file, not sharing a file with a class defiition, unless they are
legitimately class memebers.


## Solution-wide prohibitions
- Do not implement logging or telemetry.
- Do not use unsafe code or pointers.
- Do not implement any functionality not explicitly requested.
- Do not add third-party dependencies without explicit permission.

