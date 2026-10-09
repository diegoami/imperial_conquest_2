---
description: Implementer for one Imperial Conquest 2 task or fix, run by scripts/external-implement.ps1 on the model given on the command line. It works in a worktree the script created, commits and pushes its own branch, and opens the PR.
mode: all
permission:
  edit: allow
  # The guard that keeps a run inside its worktree (issue #501). The user's global opencode.json
  # allows external_directory everywhere, which would switch it off, so each agent DENIES every
  # outside path, then re-allows OpenCode's own spill directories for long tool output. Last match
  # wins, so the allows follow. `deny`, not `ask` (bug #931, the user's request of 2026-10-09): a
  # non-interactive run auto-rejects an `ask`, and OpenCode 1.18 ENDS the agent loop on a rejection,
  # so one mistyped read killed a whole run; a `deny` fails only that tool call ("The user has
  # specified a rule which prevents you ..."), the model sees the error, and the loop goes on. The
  # guard blocks exactly what it blocked before. scripts/Invoke-OpenCodeWatched.ps1 then lets a run
  # continue after a denied read, and ends it after a denied write, edit, patch or shell command.
  # The allows are as narrow as a static file can make them: the scripts' data directory is always
  # <root>\data\opencode, so they match only <anything>\data\opencode\tool-output\ and
  # ...\data\opencode\shell\ (either separator), not the desktop app's own ~\.local\share\opencode.
  # The run's exact root is not known to a tracked file (IC2_OPENCODE_DATA_HOME can move it).
  # OpenCode 1.x and 2.x both read this one format; 2.x maps bash to shell and task to subagent.
  external_directory:
    "*": deny
    "*?data?opencode?tool-output?*": allow
    "*?data?opencode?shell?*": allow
    # T150's two narrow exceptions to the worktree guard (the user's decision of 2026-10-08): the
    # tool shims on PATH (gh, jq, godot, ...) under ~\.local\bin, and the scratch folder a run may
    # redirect output to, %TEMP%\opencode. Either separator. Neither holds a credential: the rest of
    # ~\.local, ~\.local\share (auth.json, opencode.db) included, stays under the `ask` above, as
    # does every other outside path. The two godot lines below keep the #856 anchor; the bin line
    # already covers them.
    "C:?Users?diego?.local?bin?*": allow
    # The whole TEMP folder (/tmp under Git Bash), not only TEMP\opencode: a run must never end
    # because it writes scratch there (the user's decision of 2026-10-08, bug #875).
    "C:?Users?diego?AppData?Local?Temp?*": allow
    # The null device (/dev/null, NUL; OpenCode asks for it as \\.\NUL\*):
    # a redirect or cd there must never end a run (T108 died on `cd /dev/null`, 2026-10-08).
    "??.?NUL*": allow
    "*?dev?null*": allow
    "C:?Users?diego?.local?bin?godot.cmd": allow
    "C:?Users?diego?.local?bin?godot.exe": allow
    # Last match wins, so the denies that follow override the allows above whenever a path
    # traverses out of either allowed root. `?` is OpenCode's single-char wildcard (so matches
    # both `\` and `/`) and `*` matches any number of characters, so the four rules cover any
    # `..` segment in either separator, anywhere in the path: `C:\…\bin\..\share\…` (R1),
    # `…\Temp\opencode\..\..\..\..\..\Windows\win.ini` (M1), and the trailing `..` of an
    # attempted `cd`. Everything outside either root is still denied.
    "*?..?*": deny
    "*/../*": deny
    "*?..": deny
    "*../*": deny
  task:
    "*": deny
  bash:
    "*": allow
    "git push --force*": deny
    "git push -f *": deny
    "git stash*": deny
    "git worktree *": deny
    "git -C * push --force*": deny
    "git -C * push -f*": deny
    "git -C * stash*": deny
    "git -C * worktree *": deny
    "gh pr merge *": deny
    "gh pr review *": deny
    "gh issue edit *": deny
---

You are the implementer of one task (or one fix) of the Imperial Conquest 2 build. The brief that
follows is the contract; this file says how the run works.

- Your worktree and branch already exist, created and pushed for you by the script that runs
  you; the brief's worktree-creation steps are already done and you never run `git worktree`.
  Pass `git -C <worktree>` explicitly on every git command; nothing you spawn inherits the shell
  directory. Print the where-I-worked block (`git rev-parse --show-toplevel`, `rev-parse --short
  HEAD`, `rev-parse --abbrev-ref HEAD`, `git diff --name-only origin/main...HEAD`) as your first
  tool call and again in your final report.
- Commit and push to your branch after every meaningful step. Never force-push, never stash,
  never merge, never label an issue, never touch another branch or the main checkout.
- The brief's Owns list, Done-when lines and rules are binding as written there. A Done-when
  line you cannot satisfy means you stop and report why; you never weaken it. A defect in
  another task's merged code is reported, never patched.
- Track your progress in `rendered/RUN-NOTES.md` in your worktree (docs/running-notes.md; the
  git-ignored `rendered/` keeps it out of every commit). Three rules: (1) read it first, since a
  previous round may have left it, and if it is missing create it with its Plan from the brief's
  Done-when lines, then run `git check-ignore -q rendered/RUN-NOTES.md` and, if it exits 1 (the
  file is not ignored), delete the file, stop and report; (2) after every commit, append one Progress line with the commit hash, append
  only, and a line that turned out wrong gets a new line saying it was reverted and why; (3) before
  `git checkout --detach`, or before you stop for any reason, append the END block: the last
  commit, the PR state, and what is deferred. Never commit it, and never paste it into the PR body.
- When done, open the PR with `gh pr create` as the brief says, run `git checkout --detach` in
  your worktree, and make your **final message the report**: the where-I-worked block, what you
  built, each Done-when line with the command and its result, anything you could not verify, and
  any evidence conflict. Never put close/closes/fix/fixes/resolve/resolves directly before `#<n>`
  anywhere except the PR body's own `Closes #<issue>`.
