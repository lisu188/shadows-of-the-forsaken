"""Validate the design graph, not Unity scene geometry or runtime gameplay."""
from __future__ import annotations

import argparse
from collections import defaultdict, deque
import hashlib
import json
from pathlib import Path
import re
import sys
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
State = tuple[str, frozenset[str]]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def keys(value: Any, expected: set[str], context: str) -> None:
    require(isinstance(value, dict) and set(value) == expected,
            f"{context}: expected fields {sorted(expected)}")


def ids(value: Any, context: str) -> set[str]:
    require(isinstance(value, list) and all(isinstance(x, str) for x in value),
            f"{context}: expected a list of identifiers")
    require(len(value) == len(set(value)), f"{context}: duplicate identifier")
    require(all(re.fullmatch(r"[a-z][a-z0-9_]*", x) for x in value),
            f"{context}: invalid identifier")
    return set(value)


def validate_schema(contract: dict[str, Any]) -> None:
    keys(contract, {"schema_version", "source", "start", "exit", "required_events",
                    "rooms", "events", "connections"}, "contract")
    require(type(contract["schema_version"]) is int and contract["schema_version"] == 1,
            "unsupported schema_version")
    keys(contract["source"], {"path", "git_blob_sha"}, "source")
    require(contract["source"]["path"] == "Shadows of the Forsaken.docx", "unexpected source path")
    sha = contract["source"]["git_blob_sha"]
    require(isinstance(sha, str) and re.fullmatch(r"[0-9a-f]{40}", sha) is not None,
            "source: invalid git_blob_sha")
    fields = {
        "rooms": {"id", "optional", "requires"},
        "events": {"id", "room", "optional", "requires"},
        "connections": {"from", "to", "requires"},
    }
    limits = {"rooms": 32, "events": 12, "connections": 128}
    for group, expected in fields.items():
        entries = contract[group]
        require(isinstance(entries, list) and 0 < len(entries) <= limits[group],
                f"{group}: expected 1..{limits[group]} entries")
        for item in entries:
            keys(item, expected, group)
            ids(item["requires"], f"{group}.requires")
            if group != "connections":
                require(type(item["optional"]) is bool, f"{group}: optional must be boolean")
    rooms = ids([r["id"] for r in contract["rooms"]], "rooms")
    events = ids([e["id"] for e in contract["events"]], "events")
    for field in ("start", "exit"):
        require(isinstance(contract[field], str) and contract[field] in rooms,
                f"unknown {field} room")
    require(contract["start"] != contract["exit"], "start and exit must differ")
    required = ids(contract["required_events"], "required_events")
    require(required == {e["id"] for e in contract["events"] if not e["optional"]},
            "required_events must match non-optional events")
    require(bool(required), "at least one required event is needed")
    for room in contract["rooms"]:
        if room["id"] in (contract["start"], contract["exit"]):
            require(not room["optional"], "start/exit cannot be optional")
    pairs = set()
    for group in fields:
        for item in contract[group]:
            require(set(item["requires"]) <= events, f"{group}: unknown required event")
            if group == "events":
                require(isinstance(item["room"], str) and item["room"] in rooms, "event: unknown room")
                require(item["id"] not in item["requires"], "event cannot require itself")
            if group == "connections":
                require(all(isinstance(item[k], str) and item[k] in rooms for k in ("from", "to")),
                        "connection: unknown room")
                pair = frozenset((item["from"], item["to"]))
                require(len(pair) == 2 and pair not in pairs, "duplicate or self connection")
                pairs.add(pair)


def reachable(contract: dict[str, Any], include_optional: bool = True) -> dict[State, set[State]]:
    """Enumerate monotone events and bidirectional gates; exit is terminal."""
    validate_schema(contract)
    optional_rooms = {r["id"] for r in contract["rooms"] if r["optional"]}
    start = (contract["start"], frozenset())
    graph: dict[State, set[State]] = {start: set()}
    pending = deque([start])
    while pending:
        state = pending.popleft()
        room, completed = state
        if room == contract["exit"]:
            continue
        next_states = set()
        for event in contract["events"]:
            if (event["room"] == room and event["id"] not in completed
                    and (include_optional or not event["optional"])
                    and set(event["requires"]) <= completed):
                next_states.add((room, completed | {event["id"]}))
        for connection in contract["connections"]:
            if room not in (connection["from"], connection["to"]):
                continue
            other = connection["to"] if room == connection["from"] else connection["from"]
            if ((include_optional or other not in optional_rooms)
                    and set(connection["requires"]) <= completed):
                next_states.add((other, completed))
        graph[state] = next_states
        for candidate in next_states:
            if candidate not in graph:
                graph[candidate] = set()
                pending.append(candidate)
    return graph


def validate(contract: dict[str, Any]) -> dict[str, int]:
    validate_schema(contract)
    counts = {}
    for include_optional in (False, True):
        graph = reachable(contract, include_optional)
        counts["with_secret" if include_optional else "without_secret"] = len(graph)
        for room in contract["rooms"]:
            states = [s for s in graph if s[0] == room["id"]]
            if include_optional or not room["optional"]:
                require(bool(states), f"unreachable room: {room['id']}")
            require(all(set(room["requires"]) <= s[1] for s in states),
                    f"bypassed entry requirement: {room['id']}")
        completed = set().union(*(state[1] for state in graph))
        expected = {e["id"] for e in contract["events"] if include_optional or not e["optional"]}
        require(expected <= completed, f"unreachable events: {sorted(expected - completed)}")
        winners = {s for s in graph if s[0] == contract["exit"]}
        require(bool(winners), "exit cannot be reached")
        require(all(set(contract["required_events"]) <= s[1] for s in winners),
                "exit bypasses a required event")
        reverse = defaultdict(set)
        for state, next_states in graph.items():
            for candidate in next_states:
                reverse[candidate].add(state)
        can_finish = set(winners)
        pending = deque(winners)
        while pending:
            for previous in reverse[pending.popleft()] - can_finish:
                can_finish.add(previous)
                pending.append(previous)
        require(can_finish == set(graph), "reachable state cannot reach completion (softlock)")
    return counts


def load(path: Path) -> dict[str, Any]:
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, f"duplicate JSON key: {key}")
            result[key] = value
        return result
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)


def verify_source(contract: dict[str, Any], root: Path) -> None:
    validate_schema(contract)
    data = (root / contract["source"]["path"]).read_bytes()
    digest = hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()
    require(digest == contract["source"]["git_blob_sha"], "DOCX changed: review the design contract")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", nargs="?", type=Path, default=ROOT / "docs/level-contract.json")
    args = parser.parse_args()
    try:
        contract = load(args.path)
        counts = validate(contract)
        verify_source(contract, ROOT)
    except (OSError, ValueError) as error:
        print(f"Invalid level contract: {error}", file=sys.stderr)
        return 1
    print(f"Valid design contract: {json.dumps(counts, sort_keys=True)} reachable states.")
    print("Source DOCX unchanged. Unity scenes, combat and timing were not tested.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
