#!/usr/bin/env python3
"""Semantic validation report for an AI GIS Converter GeoJSON export.

Answers the ten questions Phase 2 verification asks, from the export itself
rather than from the code that produced it:

  1  which export was read
  2  per-field statistics for every semantic field
  3  the four fields every feature must carry
  4  duplicate UniqueIds
  5  duplicate ElementIds
  6  parameter serialisation failures
  7  the largest feature by property count
  8  the average property count
  9  export size, against a baseline when one is given
 10  export duration

Usage:
    python validate_export.py <export.geojson> [--baseline <older.geojson>]
                              [--log <aigis-YYYYMMDD.log>] [--json <out.json>]

Exit code is 1 when a required field is missing anywhere, or when a duplicate
identifier is found. Everything else is reported without failing, because a
sparse optional field is information and not a fault.
"""

import argparse
import collections
import json
import os
import re
import sys

REQUIRED_FIELDS = ["UniqueId", "ElementId", "Category", "BuiltInCategory"]

REPORTED_FIELDS = [
    "UniqueId", "ElementId", "Category", "BuiltInCategory", "Family", "Type",
    "Level", "Phase", "Workset", "DesignOption", "Mark", "Comments",
    "Description", "AssemblyCode", "OmniClassNumber", "TypeMark",
    "Material", "MaterialId", "MaterialCount",
    "HostId", "ParentId", "GroupName", "RoomName", "SpaceName",
    "BoundsHeight", "Thickness", "RotationRadians",
    "Area", "Volume", "Length",
    "GeometryStatus", "GeometryFailureReason",
]

INSTANCE_PREFIX = "p_"
TYPE_PREFIX = "tp_"


def load(path):
    with open(path, encoding="utf-8-sig") as handle:
        return json.load(handle)["features"]


def field_stats(features, name):
    """Present / missing / null / distinct for one field."""
    present = missing = null = 0
    values = set()

    for feature in features:
        properties = feature.get("properties") or {}

        if name not in properties:
            missing += 1
            continue

        value = properties[name]

        if value is None or value == "":
            null += 1
            continue

        present += 1
        values.add(str(value))

    return present, missing, null, len(values)


def duplicates(features, name):
    counts = collections.Counter()

    for feature in features:
        value = (feature.get("properties") or {}).get(name)

        if value is not None and value != "":
            counts[str(value)] += 1

    return {value: count for value, count in counts.items() if count > 1}


def read_log(path):
    """Pull skip counts, stage timings and the run duration out of a run log.

    The duration is matched against named lines rather than "the last thing that
    looked like a number of milliseconds". A total that silently picks up an
    unrelated line is worse than no total.
    """
    result = dict(skips={}, metadata={}, duration=None, source=None, stages={})

    if not path or not os.path.exists(path):
        return result

    with open(path, encoding="utf-8", errors="replace") as handle:
        text = handle.read()

    # Written by ReadSourceStage: "Reader metadata <key> = <value>".
    for key, value in re.findall(r"Reader metadata ([\w.:]+) = (.+)", text):
        value = value.strip()
        result["metadata"][key] = value

        if key.startswith("SemanticSkipped."):
            try:
                result["skips"][key[len("SemanticSkipped."):]] = int(value)
            except ValueError:
                result["skips"][key[len("SemanticSkipped."):]] = value

    for name, ms in re.findall(r"Stage (.+?) completed in ([\d.]+) ms", text):
        result["stages"][name] = float(ms) / 1000.0

    batch = re.findall(r"Batch finished in ([\d.]+) ms", text)

    if batch:
        result["duration"] = float(batch[-1]) / 1000.0
        result["source"] = "Batch finished"
    elif result["stages"]:
        result["duration"] = sum(result["stages"].values())
        result["source"] = "sum of %d stages" % len(result["stages"])

    return result


def human(size):
    for unit in ["B", "KB", "MB", "GB"]:
        if size < 1024 or unit == "GB":
            return "%.1f %s" % (size, unit)
        size /= 1024.0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("export")
    parser.add_argument("--baseline")
    parser.add_argument("--log")
    parser.add_argument("--json")
    arguments = parser.parse_args()

    features = load(arguments.export)
    total = len(features)
    failed = False

    print("=" * 78)
    print("SEMANTIC VALIDATION REPORT")
    print("=" * 78)
    print("export : %s" % os.path.basename(arguments.export))
    print("features: %d" % total)
    print()

    # -- 3. required fields -------------------------------------------------
    print("-- required on every feature " + "-" * 49)
    print("%-20s %10s %10s %10s   %s" % ("field", "present", "missing", "null", "verdict"))

    for name in REQUIRED_FIELDS:
        present, missing, null, _ = field_stats(features, name)
        ok = missing == 0 and null == 0
        failed = failed or not ok
        print("%-20s %10d %10d %10d   %s"
              % (name, present, missing, null, "PASS" if ok else "FAIL"))
    print()

    # -- 2. per-field statistics -------------------------------------------
    print("-- semantic field statistics " + "-" * 48)
    print("%-22s %9s %9s %9s %9s %7s"
          % ("field", "present", "missing", "null", "distinct", "cover"))

    stats = {}

    for name in REPORTED_FIELDS:
        present, missing, null, distinct = field_stats(features, name)
        stats[name] = dict(present=present, missing=missing, null=null, distinct=distinct)
        coverage = (100.0 * present / total) if total else 0.0
        flag = "" if present else "   <- absent"
        print("%-22s %9d %9d %9d %9d %6.1f%%%s"
              % (name, present, missing, null, distinct, coverage, flag))
    print()

    # -- 4 & 5. duplicate identifiers --------------------------------------
    print("-- identifier uniqueness " + "-" * 52)

    for name in ["UniqueId", "ElementId"]:
        found = duplicates(features, name)
        failed = failed or bool(found)
        print("%-12s duplicates: %d" % (name, len(found)))

        for value, count in list(sorted(found.items(), key=lambda p: -p[1]))[:5]:
            print("      %s appears %d times" % (value, count))
    print()

    # -- 7 & 8. property counts --------------------------------------------
    counts = [len(feature.get("properties") or {}) for feature in features]
    average = sum(counts) / float(total) if total else 0.0
    largest = max(range(total), key=lambda i: counts[i]) if total else None

    print("-- property counts " + "-" * 58)
    print("average per feature : %.1f" % average)
    print("minimum             : %d" % (min(counts) if counts else 0))
    print("maximum             : %d" % (max(counts) if counts else 0))

    if largest is not None:
        properties = features[largest].get("properties") or {}
        instance = sum(1 for k in properties if k.startswith(INSTANCE_PREFIX))
        typed = sum(1 for k in properties if k.startswith(TYPE_PREFIX))
        print("largest feature     : %s" % features[largest].get("id"))
        print("                      %d properties (%d instance, %d type, %d named)"
              % (len(properties), instance, typed, len(properties) - instance - typed))
        print("                      Category=%s" % properties.get("Category"))
    print()

    # -- parameter dump coverage -------------------------------------------
    with_instance = sum(
        1 for f in features
        if any(k.startswith(INSTANCE_PREFIX) for k in (f.get("properties") or {})))
    with_type = sum(
        1 for f in features
        if any(k.startswith(TYPE_PREFIX) for k in (f.get("properties") or {})))

    print("-- parameter dump " + "-" * 59)
    print("features with instance parameters : %d (%.1f%%)"
          % (with_instance, 100.0 * with_instance / total if total else 0))
    print("features with type parameters     : %d (%.1f%%)"
          % (with_type, 100.0 * with_type / total if total else 0))

    distinct_keys = set()
    for feature in features:
        distinct_keys.update(feature.get("properties") or {})
    print("distinct property keys in export  : %d" % len(distinct_keys))
    print()

    # -- 6. serialisation failures -----------------------------------------
    log = read_log(arguments.log)
    skips = log["skips"]
    duration = log["duration"]

    print("-- parameter serialisation failures " + "-" * 41)
    if skips:
        for key in sorted(skips, key=lambda k: -skips[k] if isinstance(skips[k], int) else 0):
            print("   %-42s %s" % (key, skips[key]))
    elif log["metadata"]:
        print("   none - the reader reported no skipped parameters")
    elif arguments.log:
        print("   NOT REPORTED - the log carries no 'Reader metadata' lines.")
        print("   The reader records these; if they are absent the run predates the fix that")
        print("   surfaces them, or the reader did not write any.")
    else:
        print("   not available - pass --log")
    print()

    if log["metadata"]:
        print("-- reader metadata " + "-" * 58)
        for key in sorted(log["metadata"]):
            print("   %-42s %s" % (key, log["metadata"][key]))
        print()

    # -- 9 & 10. size and duration -----------------------------------------
    size = os.path.getsize(arguments.export)

    print("-- size and duration " + "-" * 56)
    print("export size          : %s (%d bytes)" % (human(size), size))

    if arguments.baseline and os.path.exists(arguments.baseline):
        base_features = load(arguments.baseline)
        base_size = os.path.getsize(arguments.baseline)
        base_counts = [len(f.get("properties") or {}) for f in base_features]
        base_average = sum(base_counts) / float(len(base_features)) if base_features else 0.0

        print("baseline             : %s (%s)"
              % (os.path.basename(arguments.baseline), human(base_size)))
        print("size change          : %+.1f%% (%s -> %s)"
              % (100.0 * (size - base_size) / base_size, human(base_size), human(size)))
        print("feature count        : %d -> %d  (%s)"
              % (len(base_features), total,
                 "unchanged" if len(base_features) == total else "CHANGED"))
        print("properties/feature   : %.1f -> %.1f  (%+.1fx)"
              % (base_average, average, average / base_average if base_average else 0))

        failed = failed or len(base_features) != total

    if duration is not None:
        print("export duration      : %.2f s   (from: %s)" % (duration, log["source"]))

        for name in sorted(log["stages"], key=lambda n: -log["stages"][n])[:5]:
            print("   %-28s %6.2f s" % (name, log["stages"][name]))
    else:
        print("export duration      : not available - pass --log")
    print()

    print("=" * 78)
    print("RESULT: %s" % ("DEFECTS FOUND" if failed else "all required checks passed"))
    print("=" * 78)

    if arguments.json:
        with open(arguments.json, "w", encoding="utf-8") as handle:
            json.dump({
                "export": os.path.basename(arguments.export),
                "features": total,
                "fields": stats,
                "averageProperties": average,
                "maximumProperties": max(counts) if counts else 0,
                "distinctKeys": len(distinct_keys),
                "sizeBytes": size,
                "skipped": skips,
                "durationSeconds": duration,
                "passed": not failed,
            }, handle, indent=2, ensure_ascii=False)
        print("wrote %s" % arguments.json)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
