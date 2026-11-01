## TaffySharp

[![Build](https://github.com/HanJaeJoon/TaffySharp/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/HanJaeJoon/TaffySharp/actions/workflows/nuget-publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

TaffySharp is a UI layout engine implementing CSS Flexbox, CSS Grid and block layout (with floats).
It is a pure C# port of [taffy](https://github.com/DioxusLabs/taffy) (Rust, MIT), ported from
[v0.15.1](https://github.com/DioxusLabs/taffy/tree/v0.15.1) (commit `0064bd5`). The port is released under the MIT license.

[Chorizite/TaffySharp](https://github.com/Chorizite/TaffySharp) on GitHub is a native binding to the Rust library;
this project is an unrelated, fully managed port with no native dependency.

Targets `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net10.0`, so it can be used from .NET Framework 4.6.2+, .NET Core 2.0+, .NET 5+, Unity 2021.2+, and Mono.

## Installation

``` bash
dotnet add package TaffySharp
```

## Usage

``` c#
using TaffySharp;

TaffyTree tree = new();

NodeId child = tree.NewLeaf(new Style
{
    Size = new Size<Dimension>(Dimension.Percent(0.5f), Dimension.Auto),
});

NodeId root = tree.NewWithChildren(new Style
{
    Display = Display.Flex,
    Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Length(100f)),
    JustifyContent = AlignContent.Center,
}, child);

tree.ComputeLayout(root, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

Layout layout = tree.Layout(child); // Location = (25, 0), Size = 50 x 100
```

Leaves with intrinsic content (text, images) get a node context and a measure function:

``` c#
TaffyTree<string> tree = new();
NodeId text = tree.NewLeafWithContext(new Style(), "Hello");
tree.ComputeLayoutWithMeasure(text, Size.MaxContent, (inputs, node, context, style) =>
    Compute.ComputeLeafLayout(inputs, style, (_, _) => 0f, (known, available) =>
        new Size<float>(known.Width ?? context!.Length * 8f, known.Height ?? 16f)));
```

The API mirrors taffy's: `Style` has the same properties (in PascalCase), percentages are fractions (`0.5f` is 50%),
and the low-level `Compute.*` functions and tree interfaces let you run the algorithms on your own tree type.
Not ported: CSS string parsing (taffy's `parse` feature) and serde support.

## Credits

Algorithm and original implementation: [taffy](https://github.com/DioxusLabs/taffy) by the Taffy Authors and Visly Inc. (MIT).
The conformance tests in `tests/TaffySharp.Tests/Generated` are converted from taffy's Chrome-generated XML fixtures
by `tests/tools/convert_generated.py`.
