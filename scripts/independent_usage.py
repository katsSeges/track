"""Independent usage count straight from the raw agent logs (does not use Track's code).

Prints JSON: tokens per tool per local day and per local hour of today, counting only usage that
started before the cutoff (an ISO UTC instant, normally the start of the current hour).

Rules (re-derived from the log formats, see plans/track-tray-app.md Phase 2):
  Claude  : assistant lines with message.usage; first copy per (message.id, requestId); skip "<synthetic>";
            tokens = input + cache_creation_input + cache_read_input + output
  Codex   : per file, growth of info.total_token_usage.total_tokens between token_count events
            (a drop starts a new baseline and counts the new total)
  Copilot : session.usage_record; tokens = max(input, cacheRead + cacheWrite) + output
Usage: python -I independent_usage.py <home> <cutoff-utc-iso> <first-day YYYY-MM-DD>
"""
import datetime
import glob
import json
import os
import sys
from collections import defaultdict

home, cutoff_text, first_day_text = sys.argv[1], sys.argv[2], sys.argv[3]
cutoff = datetime.datetime.fromisoformat(cutoff_text.replace("Z", "+00:00"))
first_day = datetime.date.fromisoformat(first_day_text)
today = datetime.datetime.now().astimezone().date()

days = defaultdict(lambda: defaultdict(int))
hours = defaultdict(lambda: defaultdict(int))
yesterday_hours = defaultdict(lambda: defaultdict(int))


def when(text):
    try:
        return datetime.datetime.fromisoformat(text.replace("Z", "+00:00"))
    except (AttributeError, ValueError):
        return None


def count(tool, at, tokens):
    if at is None or tokens <= 0 or at >= cutoff:
        return
    local = at.astimezone()
    if local.date() >= first_day:
        days[str(local.date())][tool] += tokens
    if local.date() == today:
        hours[local.hour][tool] += tokens
    if local.date() == today - datetime.timedelta(days=1):
        yesterday_hours[local.hour][tool] += tokens


def lines(path):
    with open(path, encoding="utf8", errors="replace") as fh:
        for raw in fh:
            try:
                obj = json.loads(raw)
            except ValueError:
                continue
            if isinstance(obj, dict):
                yield obj


seen = set()
for root in [os.path.join(home, ".claude", "projects"), os.path.join(home, ".config", "claude", "projects")]:
    for path in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
        for o in lines(path):
            m = o.get("message")
            if not isinstance(m, dict) or not isinstance(m.get("usage"), dict) or not m.get("id") or m.get("model") in (None, "<synthetic>"):
                continue
            key = (m["id"], o.get("requestId"))
            if key in seen:
                continue
            u = m["usage"]
            tokens = sum(max(0, int(u.get(k) or 0)) for k in ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens"))
            if tokens > 0:
                seen.add(key)
            count("Claude", when(o.get("timestamp")), tokens)

for root in [os.path.join(home, ".codex", "sessions"), os.path.join(home, ".codex", "archived_sessions")]:
    for path in glob.glob(os.path.join(root, "**", "rollout-*.jsonl"), recursive=True):
        previous = 0
        for o in lines(path):
            p = o.get("payload")
            if o.get("type") != "event_msg" or not isinstance(p, dict) or p.get("type") != "token_count" or not isinstance(p.get("info"), dict):
                continue
            total = int((p["info"].get("total_token_usage") or {}).get("total_tokens") or 0)
            if total == previous:
                continue
            grown = total if total < previous else total - previous
            previous = total
            count("Codex", when(o.get("timestamp")), grown)

for path in glob.glob(os.path.join(home, ".copilot", "session-state", "*", "events.jsonl")):
    try:
        for o in lines(path):
            if o.get("type") != "session.usage_record" or not o.get("id"):
                continue
            u = (o.get("data") or {}).get("usage") or {}
            if not u.get("model"):
                continue
            n = lambda k: max(0, int(u.get(k) or 0))
            count("Copilot", when(o.get("timestamp")), max(n("inputTokens"), n("cacheReadTokens") + n("cacheWriteTokens")) + n("outputTokens"))
    except OSError:
        pass

# "Today vs yesterday at this time", measured like Track: whole hours that start before the cutoff's local time.
cut_hour = cutoff.astimezone().hour
current = sum(sum(v.values()) for h, v in hours.items() if h < cut_hour)
previous = sum(sum(v.values()) for h, v in yesterday_hours.items() if h < cut_hour)
percent = None
if previous:
    exact = (current - previous) * 100 / previous
    percent = int(exact + 0.5) if exact >= 0 else -int(-exact + 0.5)  # half away from zero

print(json.dumps({"days": {d: dict(v) for d, v in days.items()}, "hours": {str(h): dict(v) for h, v in hours.items()},
                  "comparison": {"current": current, "previous": previous, "percent": percent}}))
