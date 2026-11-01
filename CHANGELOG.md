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
