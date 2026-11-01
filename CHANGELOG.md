# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial C# port of [taffy](https://github.com/DioxusLabs/taffy) v0.15.1: `TaffyTree` with Flexbox
  (including `flex-wrap: balance`), CSS Grid (named lines and areas, auto-placement, `repeat()`, `minmax()`,
  `fit-content()`), block layout with margin collapsing and floats, absolute/fixed positioning, rounding,
  layout caching, scrollable overflow and detailed grid info, plus the low-level `Compute.*` functions
  for custom trees.
- `TaffyTree.NewLeaf`, `NewLeafWithContext`, `NewWithChildren` and `SetStyle` throw `ArgumentNullException`
  for a null `Style` instead of failing later inside `ComputeLayout`.
- `Rect.LengthPercentageFromLength`/`FromPercent`, `Rect.LengthPercentageAutoFromLength`/`FromPercent` and
  `Size.LengthPercentageFromLengths`/`FromPercent` helpers for `Padding`, `Border`, `Margin`, `Inset` and `Gap`
  (the existing `Rect.FromLength` and `Size.FromLengths` only build `Dimension` values).
- README and XML docs: the nesting depth limit (about 8 KB of stack per level), the `Position.Static` default and
  containing-block-relative locations of out-of-flow boxes, and that `Style` instances are stored by reference.

### Changed

- `TaffyTree` parameter names: `NewLeaf`, `NewLeafWithContext` and `NewWithChildren` take `style` (was `layout`),
  `SetNodeContext` takes `context` (was `measure`).
