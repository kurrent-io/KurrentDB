# TypeScript Declarations for KurrentDB User-defined Projections

TypeScript declarations for KurrentDB's [user-defined projections](https://docs.kurrent.io/server/latest/features/projections/custom.html).

## Usage

In your IDE, create a JavaScript file for your projection. At the top of the JS file, add a [triple-slash directive](https://www.typescriptlang.org/docs/handbook/triple-slash-directives.html#-reference-path-) to reference the declaration file. For example:

```ts
/// <reference path="path/to/projections.d.ts" />
```

Your IDE's auto-complete should now pick up the declarations.
