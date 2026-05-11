## Best practices review — findings

Overall the codebase is in excellent shape: clean architecture, good separation of concerns, consistent use of `init`-only properties, solid XML doc coverage. Here are the issues I found, grouped by severity.

---

### 🔴 Correctness / API surface concerns

**1. `SaveRecordParser.RecordSize` is `internal` but declared with a `public` accessor modifier**

```csharp
// SaveRecordParser.cs
internal const int RecordSize = 2560;  // comment says "internal, not private"
public static List<SaveRecord> ReadAll(Stream stream)  // but this is public on an internal class
```

`SaveRecordParser` is `internal static`, so `public` methods on it are effectively `internal` — no bug, but the `public` on `ReadAll` is misleading. Everything in an `internal` class should be `internal` or `private` consistently.

**2. Dead comment scaffolding left in `XgFileReader.ReadStream`**

```csharp
using var decompressed = XgDecompressor.Decompress(stream);
//var rolloutStreamLen = decompressed.RolloutContexts.Length;
//if (rolloutStreamLen != 0)
//    rolloutStreamLen += 0;
// set a breakpoint here or log it
```

Three lines of commented-out debug scaffolding with a dangling comment. Should be removed.

**3. `ParseMatchInfoFromRecord` has a duplicate/conflicting seek**

```csharp
sub.Position = 1142;   // ← dead line, immediately overwritten
sub.Position = 880;
```

The first seek to 1142 is a leftover from an earlier iteration. The comment above it also refers to offset 1142 which contradicts the correct value of 880. Both the dead seek and the stale comment should be removed.

**4. `XgFileReader.ReadGameHeaders` sets `state.GameInfo` but never resets it between games**

The directory-level iterators reset `state.GameInfo = null` at file boundaries, but `ReadGameHeaders` only sets `state.GameInfo` — it never nulls it before each game. If the caller relies on `GameInfo` being `null` until set, this is fine, but it's inconsistent with how `IterateXgDirectory` behaves. Minor but worth making explicit.

---

### 🟡 Robustness / maintainability

**5. `MatchContext` constructor walks `records` twice** — once in the constructor to find `MatchHeaderRecord`, and then the full record loop in `Iterate` calls `context.Update` on every record. This means for every `Iterate` call the record list is scanned twice (once for init, once for updates). For the fast path this is invisible, but it's worth noting in case the pattern is copied.

**6. `CubeLog2` uses `Math.Round(Math.Log2(...))` — fragile for exact powers of 2**

```csharp
private static int CubeLog2(int cubeValue) =>
    cubeValue <= 1 ? 0 : (int)Math.Round(Math.Log2(Math.Max(1, cubeValue)));
```

`Math.Log2` on integer powers of 2 is exact in IEEE 754, so `Math.Round` is safe here in practice. But this is defensive code that hides what should be a hard invariant (cube values are always powers of 2). A `BitOperations.Log2` or a simple `while (v > 1) { v >>= 1; n++; }` would be clearer and eliminate the float entirely.

**7. Duplicate cube-value computation in three places**

```csharp
int cubeActual = cube.CubeValue == 0 ? 1 : (int)Math.Pow(2, Math.Abs(cube.CubeValue));
```

This expression appears in `BuildCubeRows`, `MatchContext.Update` (the `CubeRecord` branch), and implicitly in the `MoveRecord` branch of `Update`. It should be a private static helper (e.g. `CubeValueActual(int raw)`) — this is a "duplicating code is a sin" violation.

**8. `IsUsable` sentinel value -999**

```csharp
private static bool IsUsable(float v) =>
    !float.IsNaN(v) && !float.IsInfinity(v) && v != 0f && v > -999f;
```

The `v != 0f` guard means a legitimate equity of exactly 0.0 (centered position in money) will be treated as unusable and silently replaced with `0f` anyway — which happens to produce the right output but is logically wrong. Consider whether `v != 0f` belongs here or should be a separate concern.

**9. `MatchScore` Crawford suffix is `"C"` with no delimiter**

```csharp
return $"{away1}a{away2}a{crawford}";  // e.g. "1a1aC" or "1a1a"
```

The parsing in tests does `TrimEnd('C')` which works, but a score like "10a1a" and "10a1aC" could be ambiguous if the caller isn't careful. Adding a separator (e.g. `"/C"`) or a separate `IsCrawford` field on `DecisionRow` would be more robust downstream.

---

### 🟢 Minor / style

**10. `RolloutContextParser.ReadOne` has several unused-read variables**

```csharp
int met  = r.ReadInteger(); // unused
int fixed0 = r.ReadInteger(); // unused
```

Assigning to named variables (`met`, `fixed0`) is better than `_ = r.ReadInteger()` here because the names document what's being skipped. These are fine as-is. No change needed.

**11. `SaveRecordParser.ReadAll` captures `start` but never uses it**

```csharp
long start = stream.Position;  // set but never referenced below
```

`recordStart` is the variable actually used. `start` can be deleted.

**12. `GameInfo_AdvanceNextGame_SkipsEntireGame` test has dead first-pass code**

The test iterates the enumerator once into `collected` (unused result), then repeats with `collected2` and the real assertion. The first pass and its variables (`collected`, `state`, `lastGame`) should be removed.

*Resolved by 2026-05-11 callbacks redesign:* test rewritten and renamed to `SkipGameAt_SkipsEntireGameBeforeAnyYield`. The new test uses the `SkipGameAt` predicate, which skips the game before any row yields — no reference pass needed.

---

### Summary table

| # | File | Severity | Issue |
|---|------|----------|-------|
| 1 | SaveRecordParser.cs | ✅ | `public` on internal-class method |
| 2 | XgFileReader.cs | ✅ | Dead debug comments in `ReadStream` |
| 3 | XgFileReader.cs | ✅ | Dead seek + stale comment in `ParseMatchInfoFromRecord` |
| 4 | XgFileReader.cs | ✅ | `GameInfo` not reset between games in `ReadGameHeaders` |
| 5 | XgDecisionIterator.cs | ✅ | `MatchContext` constructor double-scans records |
| 6 | XgidEncoder.cs | ✅ | `Math.Round(Math.Log2(...))` should use integer log |
| 7 | XgDecisionIterator.cs | ✅ | Cube value expression duplicated 3×; extract helper |
| 8 | XgDecisionIterator.cs | ✅ | `IsUsable` `v != 0f` guard is semantically wrong |
| 9 | XgDecisionIterator.cs | ❌ | Crawford suffix in MatchScore format is ambiguous | dropped — not a real issue |
| 10 | SaveRecordParser.cs | ✅ | `start` variable unused in `ReadAll` |
| 11 | XgDecisionIteratorTests.cs | ✅ | Dead first-pass code in `GameInfo_AdvanceNextGame_SkipsEntireGame` (test renamed to `SkipGameAt_SkipsEntireGameBeforeAnyYield` in 2026-05-11 callbacks redesign) |

