# Result Pattern, Error & Exception Handling

A **from-scratch learning guide** for the MyApp backend. Read in order. Each part builds on the last: first *why*, then *what you build*, then *how the live code does it*.

| Part | File | You learn |
|---|---|---|
| 1 | [part1.md](part1.md) | The problem, two channels, `ErrorType`, `Error`, `Success` |
| 2 | [part2.md](part2.md) | Hand-built `ErrorOr<T>`, commands/queries, a real handler |
| 3 | [part3.md](part3.md) | Turning results into HTTP (`ToProblem`, `MatchOk` / `MatchCreated` / `MatchNoContent`) |
| 4 | [part4.md](part4.md) | FluentValidation + `ValidationBehavior` + the open-generic DI trap |
| 5 | [part5.md](part5.md) | Unexpected failures: one shared exception handler |
| 9–10 | [part9and10.md](part9and10.md) | How to test it, then a build-it-yourself checklist |

Code paths are under `MyApp/backend/src/`. Snippets labeled **Illustrative** are teaching-only; everything else matches the repo.

**Not covered yet (called out when relevant):** pagination helpers, a Domain / DDD layer. Those are deferred so this guide stays honest about what MyApp actually ships.

---

## How to use this guide

1. Skim part 1’s big picture once — you need the “two channels” idea before any code makes sense.
2. Follow parts 2 → 5 as if you were implementing Common → Application → Api yourself.
3. Use part 9 when you want to *prove* the design with tests; use part 10 as a checklist after a re-read.

You will **not** use the `ErrorOr` NuGet package. MyApp owns a small Result type in `Shared.Common.Errors` so HTTP mapping, multi-error lists, and `ServiceUnavailable` stay first-class.
