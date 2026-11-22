"""Read-only equivalence check for two *finished* schema 7/8 benchmark snapshots."""
import argparse
import sqlite3
from contextlib import closing
from itertools import zip_longest
from pathlib import Path


TABLES = ("snapshots", "physical_providers", "plugins", "records", "strings", "record_contents",
          "engine_game_settings", "post_plugin_game_settings", "loose_content_files", "loose_content_entries")


def connect(path):
    db = sqlite3.connect(Path(path).resolve().as_uri() + "?mode=ro", uri=True)
    db.execute("PRAGMA query_only=ON")
    db.execute("PRAGMA temp_store=MEMORY")
    return db


def compare(control, experiment):
    with closing(connect(control)) as a, closing(connect(experiment)) as b:
        for db, version in ((a, 7), (b, 8)):
            if (db.execute("SELECT version FROM schema_info").fetchall() != [(version,)]
                    or db.execute("PRAGMA quick_check").fetchall() != [("ok",)]
                    or db.execute("PRAGMA foreign_key_check").fetchone() is not None):
                raise ValueError(f"Schema or integrity check failed for version {version}")
        for table in TABLES:
            left, right = ([r[1] for r in db.execute(f"PRAGMA table_info({table})")] for db in (a, b))
            if left != right:
                raise ValueError(f"Different logical columns: {table}")
            if table == "snapshots":
                left.remove("created_utc")
                i = left.index("backend_name")
                left[i] = "replace(backend_name,'|schema=8|','|schema=7|')"
            query = f"SELECT {','.join(left)} FROM {table} ORDER BY id"
            count = 0
            for row_a, row_b in zip_longest(a.execute(query), b.execute(query)):
                if row_a != row_b:
                    raise ValueError(f"Different logical row: {table}, ordinal {count + 1}")
                count += 1
            print(f"{table}: {count:,} identical rows", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("control")
    parser.add_argument("experiment")
    args = parser.parse_args()
    compare(args.control, args.experiment)
