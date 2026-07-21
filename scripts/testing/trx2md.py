#!/usr/bin/env python3
"""
trx2md — Generate markdown test reports from TRX files.

Parses Visual Studio TRX (Test Results XML) files produced by TUnit on
Microsoft.Testing.Platform and generates compact, configurable markdown reports.

Usage:
    trx2md.py [options] [path...]

    path    TRX file(s) or directory containing them. Default: *.trx in cwd.

Output:
    By default, writes one .md per .trx file (same name, .md extension).
    Use --stdout to print to terminal, or --output FILE to write a merged file.

Presets:
    default  Failed + skipped, full stacks, metadata, grouped (default)
    ai       Compact, token-efficient, inline class names
    ci       Failures only, minimal
    debug    Everything including passed, full stacks, no root stripping

Config:
    Pass --config FILE to load settings from a JSON file. CLI flags override
    config values, and config overrides the preset.

TRX format reference: .claude/context/project/trx-format.md
XSD schema: .claude/context/external/trx-schema/vstst.xsd
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass, field
from datetime import datetime, timedelta
from pathlib import Path
from typing import Optional

# =============================================================================
# Constants
# =============================================================================

TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# Emoji constants used throughout the report
EMOJI = {
    "passed": "✅",
    "failed": "❌",
    "skipped": "⚠️",
    "green": "🟢",
    "red": "🔴",
    "yellow": "🟡",
    "slow": "🐢",
}

# =============================================================================
# Presets
# =============================================================================

PRESETS: dict[str, dict] = {
    "default": dict(
        show_failed=True,
        show_skipped=True,
        show_passed=False,
        header_style="banner",
        use_emoji=True,
        show_metadata=True,
        group_by_namespace=True,
        class_name_mode="group",
        show_categories=True,
        show_skip_reasons=True,
        compress_stack=False,
        max_frames=20,
        strip_root=True,
        slowest=0,
    ),
    "ai": dict(
        show_failed=True,
        show_skipped=True,
        show_passed=False,
        header_style="compact",
        use_emoji=False,
        show_metadata=False,
        group_by_namespace=False,
        class_name_mode="inline",
        show_categories=False,
        show_skip_reasons=False,
        compress_stack=True,
        max_frames=5,
        strip_root=True,
        slowest=0,
    ),
    "ci": dict(
        show_failed=True,
        show_skipped=False,
        show_passed=False,
        header_style="compact",
        use_emoji=True,
        show_metadata=False,
        group_by_namespace=False,
        class_name_mode="inline",
        show_categories=False,
        show_skip_reasons=False,
        compress_stack=True,
        max_frames=3,
        strip_root=True,
        slowest=5,
    ),
    "debug": dict(
        show_failed=True,
        show_skipped=True,
        show_passed=True,
        header_style="table",
        use_emoji=True,
        show_metadata=True,
        group_by_namespace=True,
        class_name_mode="group",
        show_categories=True,
        show_skip_reasons=True,
        compress_stack=False,
        max_frames=20,
        strip_root=False,
        slowest=0,
    ),
}

# =============================================================================
# Data Model
# =============================================================================


@dataclass
class TestResult:
    """A single test result parsed from a TRX UnitTestResult + UnitTest pair."""

    name: str
    method_name: str
    class_name: str
    namespace: str
    fqn: str
    assembly: str
    outcome: str
    duration: timedelta
    start_time: Optional[datetime]
    end_time: Optional[datetime]
    categories: list[str]
    error_message: Optional[str] = None
    stack_trace: Optional[str] = None
    skip_reason: Optional[str] = None
    is_timeout: bool = False
    stdout: Optional[str] = None
    stderr: Optional[str] = None


@dataclass
class TestRun:
    """Aggregated test run from one or more TRX files."""

    title: str
    run_id: str
    machine: str
    start_time: datetime
    end_time: datetime
    duration: timedelta
    total: int
    passed: int
    failed: int
    skipped: int
    timeout: int
    outcome: str
    tunit_version: Optional[str]
    results: list[TestResult]
    assemblies: list[str]


@dataclass
class ReportConfig:
    """All configurable options for report generation."""

    # Sections
    show_failed: bool = True
    show_skipped: bool = True
    show_passed: bool = False
    # Header
    header_style: str = "compact"
    use_emoji: bool = True
    show_metadata: bool = False
    # Grouping
    group_by_namespace: bool = False
    class_name_mode: str = "inline"
    show_categories: bool = False
    show_skip_reasons: bool = False
    # Stack traces
    compress_stack: bool = True
    max_frames: int = 5
    strip_root: bool = True
    repo_root: Optional[str] = None
    # Extra
    slowest: int = 0


# =============================================================================
# TRX Parser
# =============================================================================


def parse_duration(raw: str) -> timedelta:
    """Parse TRX duration format 'hh:mm:ss.fffffff' into timedelta."""
    try:
        parts = raw.split(":")
        h, m = int(parts[0]), int(parts[1])
        s = float(parts[2])
        return timedelta(hours=h, minutes=m, seconds=s)
    except (ValueError, IndexError):
        return timedelta()


def parse_iso_time(raw: Optional[str]) -> Optional[datetime]:
    """Parse ISO-8601 timestamps from TRX, handling various formats."""
    if not raw:
        return None
    # Strip timezone offset for parsing — TRX uses UTC or offset formats
    # e.g. "2026-05-26T19:41:32.359933Z" or "2026-05-26T19:41:33.323+00:00"
    raw = raw.rstrip("Z")
    if "+" in raw[10:]:
        raw = raw[: raw.rindex("+")]
    elif raw.count("-") > 2:
        raw = raw[: raw.rindex("-")]
    try:
        return datetime.fromisoformat(raw)
    except ValueError:
        return None


def parse_trx(path: Path) -> TestRun:
    """Parse a single TRX file into a TestRun."""
    tree = ET.parse(path)
    root = tree.getroot()

    # --- Test definitions (keyed by testId) ---
    defs: dict[str, dict] = {}
    tunit_version = None

    for td in root.findall(".//t:UnitTest", TRX_NS):
        tid = td.get("id", "")
        storage = td.get("storage", "")
        assembly = Path(storage).stem if storage else ""

        tm = td.find(".//t:TestMethod", TRX_NS)
        fqn = tm.get("className", "") if tm is not None else ""
        method = tm.get("name", "") if tm is not None else ""

        # Extract TUnit version from first adapterTypeName
        if tunit_version is None and tm is not None:
            adapter = tm.get("adapterTypeName", "")
            # "executor://TUnitExtension/1.43.11.0" → "1.43.11.0"
            if "/" in adapter:
                tunit_version = adapter.rsplit("/", 1)[-1]

        # Split FQN into namespace and short class name
        parts = fqn.rsplit(".", 1)
        namespace = parts[0] if len(parts) > 1 else ""
        class_name = parts[1] if len(parts) > 1 else parts[0]

        # Categories — deduplicated
        categories = sorted(
            set(
                ci.get("TestCategory", "")
                for ci in td.findall(".//t:TestCategoryItem", TRX_NS)
                if ci.get("TestCategory")
            )
        )

        defs[tid] = {
            "assembly": assembly,
            "fqn": fqn,
            "namespace": namespace,
            "class_name": class_name,
            "method_name": method,
            "categories": categories,
        }

    # --- Results ---
    results: list[TestResult] = []

    for r in root.findall(".//t:UnitTestResult", TRX_NS):
        tid = r.get("testId", "")
        d = defs.get(tid, {})
        outcome = r.get("outcome", "Unknown")

        # Parse error info
        error_message = stack_trace = None
        ei = r.find(".//t:ErrorInfo", TRX_NS)
        if ei is not None:
            msg_el = ei.find("t:Message", TRX_NS)
            stk_el = ei.find("t:StackTrace", TRX_NS)
            if msg_el is not None and msg_el.text:
                error_message = msg_el.text.strip()
            if stk_el is not None and stk_el.text:
                stack_trace = stk_el.text.strip()

        # Parse skip reason from DebugTrace
        skip_reason = None
        output_el = r.find(".//t:Output", TRX_NS)
        stdout = stderr = None
        if output_el is not None:
            dt = output_el.find("t:DebugTrace", TRX_NS)
            if dt is not None and dt.text:
                reason = dt.text.strip()
                if reason.startswith("Skipped: "):
                    skip_reason = reason[9:]
                else:
                    skip_reason = reason
            so = output_el.find("t:StdOut", TRX_NS)
            if so is not None and so.text:
                stdout = so.text.strip()
            se = output_el.find("t:StdErr", TRX_NS)
            if se is not None and se.text:
                stderr = se.text.strip()

        # Detect timeout from error message prefix
        is_timeout = bool(
            error_message and error_message.startswith("[Timeout]")
        )

        results.append(
            TestResult(
                name=r.get("testName", ""),
                method_name=d.get("method_name", ""),
                class_name=d.get("class_name", ""),
                namespace=d.get("namespace", ""),
                fqn=d.get("fqn", ""),
                assembly=d.get("assembly", ""),
                outcome=outcome,
                duration=parse_duration(r.get("duration", "00:00:00")),
                start_time=parse_iso_time(r.get("startTime")),
                end_time=parse_iso_time(r.get("endTime")),
                categories=d.get("categories", []),
                error_message=error_message,
                stack_trace=stack_trace,
                skip_reason=skip_reason,
                is_timeout=is_timeout,
                stdout=stdout,
                stderr=stderr,
            )
        )

    # --- Summary ---
    counters = root.find(".//t:ResultSummary/t:Counters", TRX_NS)
    rs = root.find(".//t:ResultSummary", TRX_NS)
    times = root.find(".//t:Times", TRX_NS)

    total = int(counters.get("total", 0)) if counters is not None else len(results)
    passed = int(counters.get("passed", 0)) if counters is not None else 0
    failed_count = int(counters.get("failed", 0)) if counters is not None else 0
    timeout_count = int(counters.get("timeout", 0)) if counters is not None else 0
    skipped_count = int(counters.get("notExecuted", 0)) if counters is not None else 0
    outcome = rs.get("outcome", "Unknown") if rs is not None else "Unknown"

    start_time = parse_iso_time(times.get("start") if times is not None else None) or datetime.now()
    end_time = parse_iso_time(times.get("finish") if times is not None else None) or datetime.now()
    duration = end_time - start_time

    # Machine name from first result
    machine = ""
    first_result = root.find(".//t:UnitTestResult", TRX_NS)
    if first_result is not None:
        machine = first_result.get("computerName", "")

    # Assembly name — storage is lowercased by MTP, recover original casing
    # from the className prefix (e.g., "Kurrent.Surge.Tests.Streams.ReadTests"
    # → first 3 segments match "kurrent.surge.tests" → "Kurrent.Surge.Tests")
    assembly_names_lower = sorted(set(r.assembly for r in results if r.assembly))
    assemblies: list[str] = []
    for asm_lower in assembly_names_lower:
        num_segments = asm_lower.count(".") + 1
        cased = asm_lower
        for r in results:
            if r.fqn:
                parts = r.fqn.split(".")
                candidate = ".".join(parts[:num_segments])
                if candidate.lower() == asm_lower:
                    cased = candidate
                    break
        assemblies.append(cased)
    title = assemblies[0] if len(assemblies) == 1 else "Test Results"

    # Run ID — from TestRun name attribute, format: "@Machine yyyy-MM-dd HH:mm:ss.fffffff"
    run_name = root.get("name", "")
    run_id = root.get("id", "")

    return TestRun(
        title=title,
        run_id=run_id,
        machine=machine,
        start_time=start_time,
        end_time=end_time,
        duration=duration,
        total=total,
        passed=passed,
        failed=failed_count + timeout_count,
        skipped=skipped_count,
        timeout=timeout_count,
        outcome=outcome,
        tunit_version=tunit_version,
        results=results,
        assemblies=assemblies,
    )


# =============================================================================
# Multi-TRX Merger
# =============================================================================


def merge_runs(runs: list[TestRun]) -> TestRun:
    """Merge multiple TestRuns (one per assembly) into a single combined run."""
    if len(runs) == 1:
        return runs[0]

    all_results: list[TestResult] = []
    all_assemblies: list[str] = []
    total = passed = failed = skipped = timeout = 0

    earliest_start = runs[0].start_time
    latest_end = runs[0].end_time

    for run in runs:
        all_results.extend(run.results)
        all_assemblies.extend(run.assemblies)
        total += run.total
        passed += run.passed
        failed += run.failed
        skipped += run.skipped
        timeout += run.timeout
        if run.start_time < earliest_start:
            earliest_start = run.start_time
        if run.end_time > latest_end:
            latest_end = run.end_time

    assemblies = sorted(set(all_assemblies))
    has_failure = any(r.outcome in ("Failed",) for r in runs)

    return TestRun(
        title=assemblies[0] if len(assemblies) == 1 else "Test Results",
        run_id=runs[0].run_id,
        machine=runs[0].machine,
        start_time=earliest_start,
        end_time=latest_end,
        duration=latest_end - earliest_start,
        total=total,
        passed=passed,
        failed=failed,
        skipped=skipped,
        timeout=timeout,
        outcome="Failed" if has_failure else "Completed",
        tunit_version=runs[0].tunit_version,
        results=all_results,
        assemblies=assemblies,
    )


# =============================================================================
# Config Loading
# =============================================================================


def load_config(args: argparse.Namespace) -> ReportConfig:
    """Build ReportConfig from defaults -> preset -> config file -> CLI flags."""
    cfg = ReportConfig()

    # Apply preset (default when not specified)
    preset_name = getattr(args, "preset", None) or "default"
    if preset_name in PRESETS:
        for k, v in PRESETS[preset_name].items():
            if hasattr(cfg, k):
                setattr(cfg, k, v)

    # Apply config file
    config_path = getattr(args, "config", None)
    if config_path:
        with open(config_path) as f:
            file_cfg = json.load(f)
        _apply_config_dict(cfg, file_cfg)

    # Apply CLI overrides (only if explicitly set)
    cli_overrides = {
        "show_failed": "failed",
        "show_skipped": "skipped",
        "show_passed": "passed",
        "header_style": "header_style",
        "use_emoji": "emoji",
        "show_metadata": "metadata",
        "group_by_namespace": "group_by_namespace",
        "class_name_mode": "class_name",
        "show_categories": "categories",
        "show_skip_reasons": "skip_reasons",
        "compress_stack": "compress_stack",
        "max_frames": "max_frames",
        "strip_root": "strip_root",
        "repo_root": "repo_root",
        "slowest": "slowest",
    }
    for cfg_key, arg_key in cli_overrides.items():
        val = getattr(args, arg_key, None)
        if val is not None:
            setattr(cfg, cfg_key, val)

    return cfg


def _apply_config_dict(cfg: ReportConfig, d: dict) -> None:
    """Apply a nested JSON config dict to a ReportConfig."""
    flat = {}
    # "sections": {"failed": true, ...}
    for section_key, field_map in {
        "sections": {"failed": "show_failed", "skipped": "show_skipped", "passed": "show_passed"},
        "header": {"style": "header_style", "emoji": "use_emoji", "metadata": "show_metadata"},
        "grouping": {"namespace": "group_by_namespace", "class_name": "class_name_mode", "categories": "show_categories", "skip_reasons": "show_skip_reasons"},
        "stack": {"compress": "compress_stack", "max_frames": "max_frames", "strip_root": "strip_root", "repo_root": "repo_root"},
    }.items():
        section = d.get(section_key, {})
        for json_key, cfg_attr in field_map.items():
            if json_key in section:
                flat[cfg_attr] = section[json_key]
    # Top-level keys
    if "slowest" in d:
        flat["slowest"] = d["slowest"]
    if "preset" in d and d["preset"] in PRESETS:
        for k, v in PRESETS[d["preset"]].items():
            if k not in flat:
                flat[k] = v
    for k, v in flat.items():
        if hasattr(cfg, k):
            setattr(cfg, k, v)


# =============================================================================
# Duration Formatting
# =============================================================================


def fmt_duration(td: timedelta) -> str:
    """Format a timedelta as a human-readable string: '31.007s', '890ms', '1.2s'."""
    total_ms = td.total_seconds() * 1000
    if total_ms < 1:
        return "0ms"
    if total_ms < 1000:
        return f"{int(total_ms)}ms"
    secs = td.total_seconds()
    if secs < 60:
        # Show 1 decimal for seconds under 10, integer for 10+
        return f"{secs:.1f}s" if secs < 10 else f"{secs:.0f}s"
    mins = secs / 60
    return f"{mins:.1f}m"


def fmt_time(dt: Optional[datetime]) -> str:
    """Format a datetime as HH:MM:SS for display."""
    if dt is None:
        return "?"
    return dt.strftime("%H:%M:%S")


# =============================================================================
# Stack Trace Processing
# =============================================================================


def compress_frame(frame: str, repo_root: Optional[str] = None) -> str:
    """Extract just file:line from a .NET stack frame.

    Input:  'at Namespace.Class.Method(params) in /full/path/File.cs:line 42'
    Output: 'src/Project/File.cs:42'
    """
    m = re.search(r"in (.+):line (\d+)", frame)
    if m:
        path = m.group(1)
        if repo_root:
            path = path.replace(repo_root, "")
        return f"{path}:{m.group(2)}"
    # Fallback: strip 'at ' prefix
    cleaned = re.sub(r"^\s*at\s+", "", frame)
    if repo_root:
        cleaned = cleaned.replace(repo_root, "")
    return cleaned


def process_frame(frame: str, repo_root: Optional[str] = None) -> str:
    """Process a full stack frame, optionally stripping the repo root."""
    result = re.sub(r"^\s*at\s+", "at ", frame)
    if repo_root:
        result = result.replace(repo_root, "")
    return result


def detect_repo_root(results: list[TestResult]) -> Optional[str]:
    """Auto-detect the repo root from stack trace file paths.

    Finds the longest common directory prefix among all file paths in stack traces.
    """
    paths: list[str] = []
    for r in results:
        if r.stack_trace:
            for m in re.finditer(r"in (.+):line \d+", r.stack_trace):
                paths.append(m.group(1))

    if not paths:
        return None

    # Find common prefix at directory level
    prefix = os.path.commonpath([os.path.dirname(p) for p in paths])
    if prefix and not prefix.endswith("/"):
        prefix += "/"
    return prefix if prefix else None


# =============================================================================
# Grouping Helpers
# =============================================================================


def group_by(items: list, key: str) -> dict[str, list]:
    """Group items by an attribute value, preserving insertion order."""
    groups: dict[str, list] = {}
    for item in items:
        k = getattr(item, key, "") or ""
        groups.setdefault(k, []).append(item)
    return groups


# =============================================================================
# Markdown Generator
# =============================================================================


def generate_report(
    run: TestRun,
    cfg: ReportConfig,
    subtitle: Optional[str] = None,
    extra_metadata: Optional[list[str]] = None,
) -> str:
    """Generate the full markdown report from a TestRun."""
    lines: list[str] = []
    ei = cfg.use_emoji
    has_fail = run.failed > 0
    status = "FAILED" if has_fail else "PASSED"
    pass_rate = round(run.passed / run.total * 100) if run.total > 0 else 100

    # Resolve repo root for stack trace stripping
    repo_root = cfg.repo_root
    if cfg.strip_root and not repo_root:
        repo_root = detect_repo_root(run.results)

    # === TITLE ===
    lines.append(f"# {run.title}")
    if subtitle:
        lines.append(f"*{subtitle}*")
    lines.append("")

    # === METADATA ===
    if cfg.show_metadata:
        chips = []
        if run.machine:
            chips.append(f"`{run.machine}`")
        if run.tunit_version:
            chips.append(f"`TUnit {run.tunit_version}`")
        if extra_metadata:
            chips.extend(f"`{v}`" for v in extra_metadata)
        if chips:
            lines.append(" · ".join(chips))
            lines.append("")

    # === SUMMARY ===
    if cfg.header_style == "banner":
        ico = (EMOJI["failed"] if has_fail else EMOJI["passed"]) if ei else ""
        parts = []
        parts.append(f"**{pass_rate}%** pass rate")
        parts.append(f"**{run.total}** total")
        parts.append(f"{EMOJI['green'] + ' ' if ei else ''}**{run.passed}** passed")
        if run.failed > 0:
            parts.append(f"{EMOJI['red'] + ' ' if ei else ''}**{run.failed}** failed")
        if run.skipped > 0:
            parts.append(f"{EMOJI['yellow'] + ' ' if ei else ''}**{run.skipped}** skipped")
        parts.append(f"**{fmt_duration(run.duration)}**")
        lines.append(f"{ico} **{status}** · {' · '.join(parts)}")
        lines.append("")
        lines.append(f"Run `{run.run_id[:12]}` · {fmt_time(run.start_time)} — {fmt_time(run.end_time)}")

    elif cfg.header_style == "table":
        ico_col = f"{EMOJI['failed'] if has_fail else EMOJI['passed']} " if ei else ""
        lines.append(f"| {ico_col}Result | Pass Rate | Total | {EMOJI['green'] + ' ' if ei else ''}Passed | {EMOJI['red'] + ' ' if ei else ''}Failed | {EMOJI['yellow'] + ' ' if ei else ''}Skipped | Duration |")
        lines.append("|--------|-----------|-------|--------|--------|---------|----------|")
        lines.append(f"| **{status}** | {pass_rate}% | {run.total} | {run.passed} | {run.failed} | {run.skipped} | {fmt_duration(run.duration)} |")
        lines.append("")
        lines.append(f"Run `{run.run_id[:12]}` · {fmt_time(run.start_time)} — {fmt_time(run.end_time)}")

    else:  # compact
        ico = (EMOJI["failed"] + " " if has_fail else EMOJI["passed"] + " ") if ei else ""
        parts = [f"**{status}**", f"{pass_rate}%", f"{run.passed}/{run.total} passed"]
        if run.failed > 0:
            parts.append(f"{EMOJI['red'] + ' ' if ei else ''}{run.failed} failed")
        if run.skipped > 0:
            parts.append(f"{EMOJI['yellow'] + ' ' if ei else ''}{run.skipped} skipped")
        parts.append(fmt_duration(run.duration))
        parts.append(f"`{run.run_id[:12]}`")
        lines.append(f"{ico}{' · '.join(parts)}")

    # If no sections are enabled, return just the summary
    if not cfg.show_failed and not cfg.show_skipped and not cfg.show_passed:
        return "\n".join(lines)

    # --- Helpers ---

    def cat_tags(t: TestResult) -> str:
        if not cfg.show_categories or not t.categories:
            return ""
        return "\n  " + " ".join(f"`{c}`" for c in t.categories)

    def test_line(t: TestResult, icon: str) -> str:
        name = f"{t.class_name}.{t.name}" if cfg.class_name_mode == "inline" else t.name
        dur = f" **({fmt_duration(t.duration)})**"
        prefix = f"- {icon} " if ei else "- "
        return f"{prefix}{name}{dur}{cat_tags(t)}"

    def render_list(tests: list[TestResult], icon: str) -> None:
        def render_items(items: list[TestResult]) -> None:
            if cfg.class_name_mode == "group":
                for cls, cls_items in group_by(items, "class_name").items():
                    lines.append("")
                    lines.append(f"#### {cls}")
                    # Class timing from first/last test
                    starts = [t.start_time for t in cls_items if t.start_time]
                    ends = [t.end_time for t in cls_items if t.end_time]
                    if starts and ends:
                        lines.append("")
                        cls_dur = max(ends) - min(starts)
                        lines.append(f"*Started {fmt_time(min(starts))} · Ended {fmt_time(max(ends))} · Duration {fmt_duration(cls_dur)}*")
                    lines.append("")
                    for t in cls_items:
                        lines.append(test_line(t, icon))
            else:
                for t in items:
                    lines.append(test_line(t, icon))

        if cfg.group_by_namespace:
            for ns, ns_items in group_by(tests, "namespace").items():
                lines.append("")
                lines.append(f"### {ns}")
                lines.append("")
                render_items(ns_items)
        else:
            lines.append("")
            render_items(tests)

    # === FAILED (always first) ===
    failed_results = [r for r in run.results if r.outcome == "Failed"]
    if cfg.show_failed and failed_results:
        lines.append("")
        lines.append(f"## {EMOJI['failed'] + ' ' if ei else ''}Failed ({len(failed_results)})")

        def render_failed(items: list[TestResult]) -> None:
            for f in items:
                cn = f"{f.class_name}." if cfg.class_name_mode not in ("hidden", "group") else ""
                timeout_tag = " `timed out`" if f.is_timeout else ""
                cats = cat_tags(f).strip()

                lines.append("")
                lines.append(f"#### {EMOJI['red'] + ' ' if ei else ''}{cn}{f.name} **({fmt_duration(f.duration)})**{timeout_tag}")
                if cats:
                    lines.append("")
                    lines.append(cats)
                lines.append("")
                if f.error_message:
                    lines.append(f"> {f.error_message}")
                    lines.append("")
                if f.stack_trace:
                    frames = [l.strip() for l in f.stack_trace.split("\n") if l.strip()]
                    max_f = min(cfg.max_frames, len(frames))
                    lines.append("```")
                    for frame in frames[:max_f]:
                        if cfg.compress_stack:
                            lines.append(compress_frame(frame, repo_root))
                        else:
                            lines.append(process_frame(frame, repo_root))
                    if len(frames) > max_f:
                        lines.append(f"... ({len(frames) - max_f} more)")
                    lines.append("```")

        if cfg.group_by_namespace:
            for ns, ns_items in group_by(failed_results, "namespace").items():
                lines.append("")
                lines.append(f"### {ns}")
                if cfg.class_name_mode == "group":
                    for cls, cls_items in group_by(ns_items, "class_name").items():
                        lines.append("")
                        lines.append(f"**{cls}**")
                        render_failed(cls_items)
                else:
                    render_failed(ns_items)
        elif cfg.class_name_mode == "group":
            for cls, cls_items in group_by(failed_results, "class_name").items():
                lines.append("")
                lines.append(f"### {cls}")
                render_failed(cls_items)
        else:
            render_failed(failed_results)

    # === SLOWEST ===
    if cfg.slowest > 0:
        all_by_duration = sorted(run.results, key=lambda t: t.duration, reverse=True)
        top_slow = all_by_duration[: cfg.slowest]
        if top_slow:
            lines.append("")
            lines.append(f"## {EMOJI['slow'] + ' ' if ei else ''}Slowest Tests")
            lines.append("")
            lines.append("| # | Test | Duration |")
            lines.append("|---|------|----------|")
            for i, t in enumerate(top_slow):
                name = f"{t.class_name}.{t.name}" if cfg.class_name_mode != "hidden" else t.name
                lines.append(f"| {i + 1} | {name} | {fmt_duration(t.duration)} |")

    # === SKIPPED ===
    skipped_results = [r for r in run.results if r.outcome == "NotExecuted"]
    if cfg.show_skipped and skipped_results:
        lines.append("")
        lines.append(f"## {EMOJI['skipped'] + ' ' if ei else ''}Skipped ({len(skipped_results)})")

        if cfg.show_skip_reasons:
            # Group by reason
            by_reason = group_by(skipped_results, "skip_reason")
            for reason, tests in by_reason.items():
                lines.append("")
                lines.append(f"> {reason or '(no reason)'} *({len(tests)} tests)*")
                lines.append("")

                if cfg.class_name_mode == "group":
                    for cls, cls_items in group_by(tests, "class_name").items():
                        lines.append(f"#### {cls}")
                        lines.append("")
                        for t in cls_items:
                            lines.append(test_line(t, EMOJI["yellow"] if ei else "-"))
                        lines.append("")
                else:
                    for t in tests:
                        lines.append(test_line(t, EMOJI["yellow"] if ei else "-"))
        else:
            render_list(skipped_results, EMOJI["yellow"] if ei else "-")

    # === PASSED ===
    passed_results = [r for r in run.results if r.outcome == "Passed"]
    if cfg.show_passed and passed_results:
        lines.append("")
        lines.append(f"## {EMOJI['passed'] + ' ' if ei else ''}Passed ({len(passed_results)})")
        render_list(passed_results, EMOJI["green"] if ei else "-")

    return "\n".join(lines)


# =============================================================================
# CLI
# =============================================================================


def parse_args(argv: Optional[list[str]] = None) -> argparse.Namespace:
    """Parse command-line arguments."""
    p = argparse.ArgumentParser(
        prog="trx2md",
        description="Generate markdown test reports from TRX files.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
presets:
  default  Failed + skipped, full stacks, metadata, grouped (default)
  ai       Compact, token-efficient, inline class names
  ci       Failures only, minimal
  debug    Everything including passed, full stacks, no root stripping

examples:
  %(prog)s                              # one .md per .trx in cwd
  %(prog)s --output test-report.md *.trx  # merged report to single file
  %(prog)s --stdout --preset ai *.trx   # compact summary to terminal
  %(prog)s --config report.json         # use config file
  %(prog)s --preset ci --slowest 5      # ci preset + top 5 slowest
""",
    )

    p.add_argument("paths", nargs="*", metavar="path", help="TRX file(s) or directory (default: *.trx in cwd)")

    # Output
    out = p.add_mutually_exclusive_group()
    out.add_argument("--stdout", action="store_true", help="print to stdout instead of writing files")
    out.add_argument("--output", metavar="FILE", help="write to a specific file")


    # Config
    p.add_argument("--preset", choices=PRESETS.keys(), help="apply a preset (default: default)")
    p.add_argument("--config", metavar="FILE", help="load config from JSON file")
    p.add_argument("--subtitle", help="subtitle shown below the assembly title (e.g., 'integration tests')")
    p.add_argument("--extra", action="append", metavar="VALUE", help="extra metadata values shown in header (repeatable)")

    # Section toggles
    p.add_argument("--failed", dest="failed", action="store_true", default=None)
    p.add_argument("--no-failed", dest="failed", action="store_false")
    p.add_argument("--skipped", dest="skipped", action="store_true", default=None)
    p.add_argument("--no-skipped", dest="skipped", action="store_false")
    p.add_argument("--passed", dest="passed", action="store_true", default=None)
    p.add_argument("--no-passed", dest="passed", action="store_false")

    # Header
    p.add_argument("--header-style", choices=["banner", "table", "compact"], default=None)
    p.add_argument("--emoji", dest="emoji", action="store_true", default=None)
    p.add_argument("--no-emoji", dest="emoji", action="store_false")
    p.add_argument("--metadata", dest="metadata", action="store_true", default=None)

    # Grouping
    p.add_argument("--group-by-namespace", dest="group_by_namespace", action="store_true", default=None)
    p.add_argument("--class-name", choices=["inline", "group", "hidden"], default=None)
    p.add_argument("--categories", dest="categories", action="store_true", default=None)
    p.add_argument("--skip-reasons", dest="skip_reasons", action="store_true", default=None)

    # Stack
    p.add_argument("--compress-stack", dest="compress_stack", action="store_true", default=None)
    p.add_argument("--no-compress-stack", dest="compress_stack", action="store_false")
    p.add_argument("--max-frames", type=int, default=None)
    p.add_argument("--strip-root", dest="strip_root", action="store_true", default=None)
    p.add_argument("--no-strip-root", dest="strip_root", action="store_false")
    p.add_argument("--repo-root", default=None)

    # Extra
    p.add_argument("--slowest", type=int, default=None, metavar="N", help="show top N slowest tests (0=off)")

    return p.parse_args(argv)


# =============================================================================
# Main
# =============================================================================


def resolve_trx_files(paths: list[str]) -> list[Path]:
    """Resolve paths to a list of TRX files."""
    if not paths:
        # Default: *.trx in current directory
        found = sorted(Path.cwd().glob("*.trx"))
        if not found:
            print("No .trx files found in current directory.", file=sys.stderr)
            sys.exit(1)
        return found

    result: list[Path] = []
    for p in paths:
        path = Path(p)
        if path.is_dir():
            found = sorted(path.glob("*.trx"))
            if not found:
                print(f"No .trx files found in {path}", file=sys.stderr)
            result.extend(found)
        elif path.is_file() and path.suffix == ".trx":
            result.append(path)
        elif "*" in p or "?" in p:
            result.extend(Path(g) for g in sorted(glob.glob(p)))
        else:
            print(f"Not a .trx file or directory: {path}", file=sys.stderr)

    if not result:
        print("No .trx files found.", file=sys.stderr)
        sys.exit(1)

    return result


def main(argv: Optional[list[str]] = None) -> int:
    args = parse_args(argv)
    cfg = load_config(args)
    trx_files = resolve_trx_files(args.paths)

    # Parse all TRX files
    runs = [parse_trx(f) for f in trx_files]
    subtitle = getattr(args, "subtitle", None)
    extra = getattr(args, "extra", None)

    if args.stdout or args.output:
        merged = merge_runs(runs)
        report = generate_report(merged, cfg, subtitle, extra)
        if args.stdout:
            print(report)
        else:
            out_path = Path(args.output)
            out_path.write_text(report, encoding="utf-8")
            print(f"Report written to {out_path}", file=sys.stderr)
    else:
        # Default: one .md per .trx, same name
        for trx_file, run in zip(trx_files, runs):
            report = generate_report(run, cfg, subtitle, extra)
            out_path = trx_file.with_suffix(".md")
            out_path.write_text(report, encoding="utf-8")
            print(f"Report written to {out_path}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
