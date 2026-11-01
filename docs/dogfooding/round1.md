# TaffySharp round 1 dogfooding findings

Target: `TaffySharp 0.1.0` from this branch (`src/TaffySharp`), consumed through a `ProjectReference` by a
net8.0 console app in `%TEMP%\dogfood-TaffySharp` (`dotnet run -c Release -- [1..8]`).
The scenarios were written from the README, the XML docs and a reflection dump of the exported surface
only - the library source was opened after the scenarios were written, to attribute the findings.
Machine: Windows 11, .NET 8, Release build, single thread. Timings are medians of 5-20 runs after warm-up.

Focus: UI trees laid out with flexbox and grid, text measure functions, relayout after node changes,
and trees with thousands of nodes.

## Scenarios

- [x] 1. App shell: header / sidebar + `repeat(auto-fill, minmax(200px, 1fr))` card grid / footer, then a window resize
- [x] 2. Text measure: word-wrapping label in a flex row, measure call counts, min-/max-content roots, node context changes
- [x] 3. Relayout: 200 random insert/remove/restyle/reorder edits checked against a tree built from scratch; in-place style mutation; dirty flags
- [x] 4. Grid: named template areas, explicit lines + spans with auto-placement around them, `auto-fit` collapsing
- [x] 5. Degenerate input and misuse: zero/negative/NaN/infinite sizes, deep nesting, cycles, re-parenting, removed and foreign `NodeId`s, null style
- [x] 6. Performance and allocation: 1k and 10k node flex, grid and text trees (first, full, clean and one-leaf-dirty relayout)
- [x] 7. Public surface audit: exported types, XML doc coverage, names and signatures
- [x] 8. Rounding and absolute positioning: `1fr` thirds, tooltip anchored with right/bottom insets, percent insets

---

# Verdict: 0.1.0 can proceed. One bug was found and fixed; no open finding blocks the release.

Every layout result checked in scenarios 1-4 and 8 matched the hand-computed CSS answer exactly, and
the incremental relayout path agreed with a from-scratch layout on all 67 snapshots of a 200-edit random
sequence. The open findings are a stack depth limit that should be documented before release, allocation
volume on full relayouts, and surface/README polish. The P1 items below are worth doing before tagging
because they are documentation only, except the public-surface trim, which is a decision for the maintainer.

## Bug found (fixed in this round)

### 1. [bug] A null `Style` was accepted and failed much later inside `ComputeLayout`

`NewLeaf(null)`, `NewLeafWithContext(null, ctx)`, `NewWithChildren(null, ...)` and `SetStyle(node, null)`
stored the null silently; the next `ComputeLayout` threw a `NullReferenceException` from deep inside the
algorithm with no hint of which node was at fault. Rust cannot express this, so it is a C#-only gap at the
API boundary. All four now throw `ArgumentNullException` before touching the tree (`SetStyle` keeps the
old style). Test: `tests/TaffySharp.Tests/HandWritten/NullStyleTests.cs`.

## Resolved after round 1

- **P1-1, P1-2, P1-3:** README "Things to know" section plus XML docs on `ComputeLayout`/`ComputeLayoutWithMeasure`
  (depth limit), `Style.Position` (static default, containing block, `HoistedChildren`) and
  `NewLeaf`/`NewLeafWithContext`/`NewWithChildren`/`SetStyle` (styles stored by reference).
- **P1-4:** checked against taffy v0.15.1 and kept public. Upstream exports `FloatContext`, `BfcSlot`, `ContentSlot`
  and `FloatIntrinsicWidthCalculator` (`taffy::compute`, `float_layout` is a default feature), `MinMax` (`taffy::geometry`)
  and `NamedGridLine` (`taffy::style`). `PlacedFloatedBox` is not nameable upstream but is returned by the public
  `FloatContext::left_floats`/`right_floats`, so it has to stay public while `FloatContext` is.
- **P1-6:** `Rect.LengthPercentageFromLength`/`FromPercent`, `Rect.LengthPercentageAutoFromLength`/`FromPercent` and
  `Size.LengthPercentageFromLengths`/`FromPercent`, with XML docs on these and the existing `Dimension` helpers
  (separate names because C# cannot overload on the return type). Test: `HandWritten/GeometryHelpersTests.cs`.
- **P2-5:** parameters renamed to `style` (`NewLeaf`, `NewLeafWithContext`, `NewWithChildren`) and `context`
  (`SetNodeContext`).

Still open: P1-5 (allocation volume), P2-1 to P2-4 and P2-6.

## What was verified as working

**App shell (scenario 1).** Header `(0,0 1280x60)`, body `(0,60 1280x700)`, footer `(0,760 1280x40)`,
sidebar `240x700`, main `(240,0 1040x700)`. `auto-fill` produced 4 columns of 240 for the 1008 px content
box and placed 7 cards on 2 rows at the exact offsets; `DetailedLayoutInfo` reports 4 explicit columns and
2 implicit rows. Resizing the root to 700 wide via `SetStyle` reflowed to 2 columns of 206.

**Text measure (scenario 2).** A 43-character label next to a 24 px icon in a 200 px row shrinks to the
remaining 168 px and wraps to 3 lines (48 px), and the row grows to match. The first layout called the
measure function 4 times; an unchanged relayout called it 0 times. `SetNodeContext` invalidates the node
(new text re-measured to `40x16`). Max-content root = one line (`344x16`), min-content root = longest word
(`40x144`).

**Relayout (scenario 3).** 200 random edits (`InsertChildAtIndex`, `RemoveChildAtIndex` + `Remove`,
`SetStyle`, in-place mutation + `MarkDirty`, `SetChildren` reorder) on a wrapping flex row: **0 mismatches**
against a freshly built tree in 67 snapshots. `MarkDirty` on a leaf dirties every ancestor.

**Grid (scenario 4).** `head/nav/main/foot` template areas land exactly (`0,0 1000x50`, `0,50 200x520`,
`200,50 800x520`, `0,570 1000x30`) even with children in reverse source order. A 2x2 span at column 2
leaves the 5 auto-placed items in the holes around it. `auto-fit` with 2 items stretches both to 300.

**Rounding and absolute positioning (scenario 8).** Three `flex-grow: 1` items in 100 px round to
`33/34/33` with no gaps; `UnroundedLayout` keeps `33.333336`; `DisableRounding` switches `Layout()` to
unrounded values. A tooltip with `right: 8; bottom: 8` inside a `position: relative` card lands at
`(232,192)`; percent insets resolve against the containing block (`20,25 160x50`).

**Misuse (scenario 5).** Removed, foreign and default `NodeId`s throw `KeyNotFoundException` naming the
id; ids are generational and are not reused after `Remove`. Out-of-range child indexes throw
`ArgumentOutOfRangeException` with the child count. Negative sizes clamp to 0, a NaN width resolves to 0
without leaking NaN into the parent, `Display.None` subtrees take neither space nor a gap.

## Numbers

### Performance (scenario 6)

Tree: 10 flex-column sections of rows; each row is a flex (or 3-track grid) row with a growing label and
two fixed leaves. "full" dirties every label (so every row and section relays out), "clean" changes
nothing, "1 leaf" dirties one random label.

| tree | nodes | first (incl. JIT) | full relayout | full alloc | clean | clean alloc | 1 leaf dirty | 1 leaf alloc |
|---|---|---|---|---|---|---|---|---|
| flex | 1,011 | 20-37 ms | 13 ms | 3.1 MB | 0.25 ms | 356 B | 0.5-0.7 ms | 60 KB |
| flex | 10,011 | 160-200 ms | 38-42 ms | 30.7 MB | 0.43-0.56 ms | 356 B | 0.7-1.1 ms | 415 KB |
| grid | 1,011 | 19-45 ms | 13-17 ms | 9.4 MB | 0.04 ms | 356 B | 0.1-0.15 ms | 86 KB |
| grid | 10,011 | 110-190 ms | 81-87 ms | 93.8 MB | 0.46-0.52 ms | 356 B | 0.8-1.2 ms | 441 KB |
| text measure | 10,011 | 44-76 ms | 25-32 ms | 30.7 MB | 0.42-0.52 ms | 356 B | 0.7 ms | 415 KB |

Incremental relayout is where it should be (about 1 ms for one change in 10k nodes, and a clean pass is
almost free in allocations). Full relayouts allocate about 3 KB per node for flex and about 9.6 KB per node
for grid, which is the main performance gap (see P1).

### Nesting depth (scenario 5)

A chain of nested containers, each with one child, laid out on a thread with the given stack size:

| display | 1 MB thread | 8 MB thread | main thread |
|---|---|---|---|
| flex | ok at 120, **crash at 130** | ok at 800, crash at 1200 | ok at 175, crash at 200 |
| block / grid | **crash at 100-120** | - | - |

That is roughly **8 KB of stack per nesting level**. The failure is a `StackOverflow` that kills the process
(it cannot be caught). Upstream taffy is recursive too, so a limit is expected, but ~100 levels on a 1 MB
thread is low enough for deep generated UIs (nested comment threads, tree views, markdown) to reach.

## Recommended changes

### P0 (before 0.1.0)

None open. (The null-style bug above was the only P0-class finding and is fixed.)

### P1 (before 0.1.0 if cheap, otherwise next release)

1. **[docs] Document the nesting depth limit.** README: about 8 KB of stack per level, about 100-120 levels
   on a 1 MB thread; run layout on a thread with a larger stack (`new Thread(..., 16 * 1024 * 1024)`) for
   deep trees. Reducing the per-level frame size (large struct locals and closures across
   `ComputeChildLayout -> ComputeCachedLayout -> ComputeFlexboxLayout -> ComputePreliminary -> MeasureChildSize`)
   is a later performance task.
2. **[docs] README: `Position` defaults to `Static` and out-of-flow `Location` is relative to the containing
   block.** Both are CSS-correct and stated in the XML docs (`Style`, `Layout.Location`), but a consumer coming
   from older taffy (where `position: relative` was the default) or walking the tree and summing parent
   offsets to draw will place an absolute box wrongly: in scenario 8 a tooltip inside a card without
   `Position.Relative` came back at `(282,222)` relative to the root instead of `(232,192)` in the card.
   A renderer needs `HoistedChildren` to know which node an absolute box is relative to; the README should say so.
3. **[docs] Style objects are stored by reference.** `NewLeaf`/`SetStyle` keep the caller's instance, so one
   `Style` shared by two nodes aliases them: mutating it through `Style(a)` plus `MarkDirty(a)` also changed
   `b` (`b` became `30x30`) without dirtying `b`. Say "one `Style` instance per node; use `Clone()`" in the
   XML docs of `NewLeaf`/`SetStyle` and in the README. (Copying on input would change the documented
   mutate-in-place workflow, so it is not recommended.)
4. **[surface, needs a decision] Trim exported algorithm internals before the surface is published.** 116
   exported types. Not reachable from any public signature: `FloatContext` (which in turn pulls in `BfcSlot`,
   `ContentSlot`, `PlacedFloatedBox`), `FloatIntrinsicWidthCalculator`, `MinMax<,>`, `NamedGridLine`. Check each
   against upstream's public `taffy::` exports and make the ones upstream keeps private `internal`. This is a
   public API removal, so it is listed here rather than done.
5. **[perf] Full relayout allocation volume.** 31 MB (flex) and 94 MB (grid) per full relayout of 10k nodes
   (3-10 KB per node) means a full relayout at 60 fps produces 2-6 GB/s of garbage. Profile the per-node
   allocations (item lists, closures, `List<T>` per container) in a later round; incremental layout is
   already cheap.
6. **[friction] `Rect.FromLength`/`FromPercent` and `Size.FromLengths` only build `Dimension` values.**
   `Padding`/`Border` are `Rect<LengthPercentage>`, `Margin`/`Inset` are `Rect<LengthPercentageAuto>` and `Gap`
   is `Size<LengthPercentage>`, so the helpers do not compile for the most common uses (`Padding =
   Rect.FromLength(16, 16, 16, 16)` is CS0029); every consumer writes their own helper. Upstream's
   `Rect::length`/`Size::length` are generic over `FromLength`, so overloads for the other two length types
   stay within the port's scope. These helpers also have no XML docs.

### P2 (later)

1. **[upstream parity] Rounding loses precision past 2^23 px.** `MathHelpers.Round` is upstream's
   `(value + 0.5).floor()` in `float`, so at an offset of 1e7 a 5 px box rounds to 6 px (8 px at 1e8). Only
   reachable with very long virtualized content; computing the `+ 0.5` in `double` would fix it without
   changing any result below 2^23.
2. **[upstream parity] Tree edits are not validated.** `AddChild` of a node that already has a parent leaves
   it in the old parent's child list too (two parents), and `AddChild(child, ancestor)` creates a cycle that
   later kills the process with a `StackOverflow` in `ComputeLayout`. Upstream behaves the same; a cycle check
   (O(depth) per `AddChild`) and detaching from the old parent would be beyond the port.
3. **[friction] Measure functions need boilerplate.** Every measure callback must wrap its size function in
   `Compute.ComputeLeafLayout(inputs, style, (_, _) => 0f, ...)`, including a calc resolver most consumers do
   not use. A `Func<Size<float?>, Size<AvailableSpace>, NodeId, TNodeContext, Style, Size<float>>` overload of
   `ComputeLayoutWithMeasure` would remove that, but it is a new feature, so it is only recorded here.
4. **[friction] A value-type `TNodeContext` cannot tell "no context" from `default`.** With `TaffyTree<int>` the
   measure function sees `0` for a leaf without context and for a leaf whose context is `0`. Document that
   consumers should use a reference type or `Nullable<T>`.
5. **[naming, needs a decision] Parameter names carried over from Rust.** `SetNodeContext(node, measure)` (the
   value is a context, not a measure function) and `NewLeaf(Style layout)` / `NewWithChildren(Style layout, ...)`
   (the value is a style). Renaming parameters breaks named-argument callers, so it is listed rather than done.
6. **[docs] XML doc coverage.** Approximate count from reflection: 656 of 927 public members (71%) have no XML
   doc, including 11 public types (`*Extensions`, `GridPlacementKind`, `RepetitionCountKind`). Intentional
   per the csproj comment (main entry points only), but the factory helpers in P1-6 and the grid helpers are
   entry points too. Separately, 291 of the 689 XML entries describe non-exported types and only inflate the
   shipped `TaffySharp.xml`.
