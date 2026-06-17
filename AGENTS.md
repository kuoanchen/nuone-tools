# Agent Notes

Before modifying this repo, read `CODEMAP.md` first. It is the compact map for where features live and how to avoid broad token-expensive searches.

Keep changes narrow, follow existing WinUI partial-class patterns, and do not revert unrelated dirty files. The user prefers not to run full builds in this repo unless they ask or verification truly requires it.

When a bug is intermittent, cross-thread, async, remote-path, SSH, watcher, or otherwise hard to reproduce, add focused diagnostic logging first so failures are captured in a log instead of relying on guesswork alone.
