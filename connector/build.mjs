#!/usr/bin/env node
// Pack the Claude Desktop connector into connector.mcpb.
//
// router-launcher.mjs here is a git symlink to ../shared/router-launcher.mjs. On a
// Windows checkout without symlink support git materializes it as a tiny text stub
// holding the link target, which mcpb would then pack verbatim — producing a connector
// whose launcher is unparseable JavaScript (node: "Unexpected token '.'"). To stay
// correct on every platform we pack from a staging copy whose launcher is the real
// shared source, read straight from shared/ rather than from the local (maybe-stubbed)
// symlink.

import { mkdtempSync, cpSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const here = dirname(fileURLToPath(import.meta.url));
const sharedLauncher = join(here, "..", "shared", "router-launcher.mjs");
const out = join(here, "connector.mcpb");

const stage = mkdtempSync(join(tmpdir(), "rhino-connector-"));
try {
  cpSync(here, stage, {
    recursive: true,
    filter: src => src !== out && src !== join(here, "build.mjs"),
  });

  // Remove the copied symlink/stub, then write a fresh regular file from the canonical
  // source. (rmSync drops the link itself; writeFileSync would otherwise follow a live
  // symlink and clobber ../shared.)
  const staged = join(stage, "router-launcher.mjs");
  rmSync(staged, { force: true });
  writeFileSync(staged, readFileSync(sharedLauncher));

  const res = spawnSync("npx", ["--yes", "@anthropic-ai/mcpb", "pack", stage, out], {
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  process.exit(res.status ?? 1);
} finally {
  rmSync(stage, { recursive: true, force: true });
}
