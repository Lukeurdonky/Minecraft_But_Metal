# Session Handoff

Run this whenever the user talks about ending the session or handing off the project.

1. Update documentation to reflect everything done this session:
   - `PROGRESS.md` — current state snapshot (what's implemented, what's not, architecture notes)
   - `TODO.md` — check off completed items, add any new ones discovered during the session
   - Any other doc under `documents/` that the session's work made outdated or incomplete (e.g. `performance/PERFORMANCE_REWORK_FINDINGS.md` for perf work, `engineering/generation_plan.md` for world-gen work)
2. Shut down the MCP server — call `editor_manage(op="quit")` to gracefully quit the Godot editor.
