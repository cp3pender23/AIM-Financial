# Repository Guidelines

## Project Structure & Module Organization
- Entry point: `bin/brew` (Bash wrapper for the `brew` command).
- Core Ruby logic: `Library/Homebrew/` (formula DSL, cask logic, OS extensions in `extend/os/`).
- Bundle support: `Library/Homebrew/bundle/`; completions/manpages live in `completions/` and `manpages/` (generated; do not hand-edit).
- Tests: `Library/Homebrew/test/` (RSpec). Avoid editing `package/` and other generated artifacts unless regenerating with the correct commands.

## Build, Test, and Development Commands
- `brew typecheck`: Sorbet type checking across the repository.
- `brew style --fix --changed`: RuboCop formatting for files you touched.
- `brew tests --online --changed`: RSpec suite for changed files; rerun flaky online cases if needed.
- Targeted runs: `brew tests --only=<path>` (e.g., `--only=cmd/reinstall`) and `brew style --fix <path>` for focused work.

## Coding Style & Naming Conventions
- Ruby first-class; favor idiomatic Ruby and Bash. New Ruby files: add `# typed: strict` and Sorbet `sig`s where appropriate (never in specs).
- Two-space indentation, snake_case for methods/variables, CamelCase for classes/modules. Keep strings, variable names, and method names self-explanatory; prefer minimal comments.
- Respect existing structure; avoid DRY violations and unnecessary abstractions. Generated files should be recreated with the proper commands, not manually edited.

## Testing Guidelines
- RSpec with one `expect` per example; limit to one `:integration_test` per file for speed.
- Prefer unit-level coverage near the change. Keep fixtures and stubs local to the spec to avoid cross-test coupling.
- Run `brew typecheck`, `brew style --fix --changed`, and `brew tests --online --changed` before opening a PR.

## Sandbox & Cache Setup
- When running in a sandbox (e.g., CI or Codex), copy `$(brew --cache)/api/` into a writable repo path such as `tmp/cache/api/` and `export HOMEBREW_CACHE=tmp/cache`. This avoids permission errors when tests write API cache or logs.

## Commit & Pull Request Guidelines
- Commit messages: concise, imperative summaries (e.g., “Fix keg relocation logic”). Group related changes; keep diffs minimal.
- PRs: describe the change, link related issues, and note testing performed (`typecheck`, `style`, `tests`). Include reproduction steps or screenshots when UI-facing (rare).
- Avoid touching unrelated files; preserve existing behavior unless explicitly refactoring. Prefer documentation updates in `docs/` when changing user-facing behavior.
