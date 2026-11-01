---
title: "Absolute boxes in a plain container are positioned against the root, not the container"
author: Jaejoon
date: 2026-11-01
branch: HanJaeJoon/dogfood-r1
cost: { minutes: 17, tokens: 221121 }
id: l-6b94caff
scope: repo
status: active
src: agent
touches:
  - tests/TaffySharp.Tests/HandWritten/NullStyleTests.cs
  - docs/dogfooding/round1.md
  - src/TaffySharp/Style/Style.cs
  - src/TaffySharp/Tree/Layout.cs
---

## Watch out
Style.Position defaults to Static (CSS), not Relative like older taffy. A container without Position.Relative is not a containing block, so an absolute child is hoisted to the nearest positioned ancestor (often the root) and its Layout.Location is relative to that ancestor, not to its parent. Round 1 dogfooding first misread this as a right/bottom inset bug (tooltip at (282,222) instead of (232,192)); it disappeared once the card had Position.Relative. Check HoistedChildren before calling an OOF position a bug.

## Tried and ruled out
Probing nesting depth in-process: a StackOverflow kills the whole process (uncatchable), so depth probes must run as a separate process per depth. About 8 KB of stack per nesting level, about 120 levels on a 1 MB thread.

In the code: src/TaffySharp/Style/Style.cs (Position default), src/TaffySharp/Tree/Layout.cs (Layout.Location doc), docs/dogfooding/round1.md
