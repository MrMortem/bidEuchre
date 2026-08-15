"""Crash-safe, multi-process experience storage and atomic model checkpoints."""

from __future__ import annotations

import json
import os
from pathlib import Path
import sqlite3
import tempfile
import time
from typing import Any, Iterator
from contextlib import contextmanager

SCHEMA_VERSION = 1


class LearningStore:
    """SQLite WAL journal shared safely by all engine seat processes."""

    def __init__(self, state_dir: Path) -> None:
        self.state_dir = state_dir.resolve()
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.database_path = self.state_dir / "experience.sqlite3"
        self.checkpoint_path = self.state_dir / "cfr-model.pt"
        self.lock_path = self.state_dir / "training.lock"
        self._initialize()

    def connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path, timeout=5.0)
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=FULL")
        connection.execute("PRAGMA busy_timeout=5000")
        return connection

    def _initialize(self) -> None:
        connection = self.connect()
        try:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS decisions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_utc REAL NOT NULL,
                    information_key TEXT NOT NULL,
                    features_json TEXT NOT NULL,
                    legal_actions_json TEXT NOT NULL,
                    action_id INTEGER NOT NULL,
                    strategy_json TEXT NOT NULL,
                    counterfactual_json TEXT NOT NULL,
                    reward REAL,
                    game_key TEXT NOT NULL DEFAULT '',
                    hand_number INTEGER NOT NULL,
                    seat INTEGER NOT NULL,
                    trained INTEGER NOT NULL DEFAULT 0,
                    reward_trained INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS decisions_untrained
                    ON decisions(trained, id);
                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS information_sets (
                    information_key TEXT PRIMARY KEY,
                    regrets_json TEXT NOT NULL,
                    strategy_sum_json TEXT NOT NULL,
                    visits INTEGER NOT NULL
                );
                """
            )
            columns = {
                row[1] for row in connection.execute("PRAGMA table_info(decisions)")
            }
            if "game_key" not in columns:
                connection.execute(
                    "ALTER TABLE decisions ADD COLUMN game_key TEXT NOT NULL DEFAULT ''"
                )
            if "reward_trained" not in columns:
                connection.execute(
                    "ALTER TABLE decisions ADD COLUMN reward_trained INTEGER NOT NULL DEFAULT 0"
                )
            connection.execute(
                "INSERT OR IGNORE INTO metadata(key, value) VALUES('schema_version', ?)",
                (str(SCHEMA_VERSION),),
            )
            connection.commit()
        finally:
            connection.close()

    def append_decision(
        self,
        *,
        information_key: str,
        features: list[float],
        legal_actions: list[int],
        action_id: int,
        strategy: list[float],
        counterfactual: list[float],
        hand_number: int,
        seat: int,
        game_key: str = "",
    ) -> int:
        connection = self.connect()
        try:
            cursor = connection.execute(
                """
                INSERT INTO decisions(
                    created_utc, information_key, features_json,
                    legal_actions_json, action_id, strategy_json,
                    counterfactual_json, game_key, hand_number, seat
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    time.time(),
                    information_key,
                    _compact(features),
                    _compact(legal_actions),
                    action_id,
                    _compact(strategy),
                    _compact(counterfactual),
                    game_key,
                    hand_number,
                    seat,
                ),
            )
            connection.commit()
            return int(cursor.lastrowid)
        finally:
            connection.close()

    def apply_terminal_reward(
        self, hand_number: int, seat: int, reward: float, game_key: str = ""
    ) -> int:
        connection = self.connect()
        try:
            cursor = connection.execute(
                """
                UPDATE decisions SET reward = ?
                WHERE game_key = ? AND hand_number = ? AND seat = ? AND reward IS NULL
                """,
                (reward, game_key, hand_number, seat),
            )
            connection.commit()
            return cursor.rowcount
        finally:
            connection.close()

    def untrained(self, limit: int = 128, *, through_id: int | None = None) -> list[dict[str, Any]]:
        connection = self.connect()
        try:
            where = "trained = 0" if through_id is None else "trained = 0 AND id <= ?"
            parameters = (limit,) if through_id is None else (through_id, limit)
            rows = connection.execute(
                f"""
                SELECT id, features_json, legal_actions_json, strategy_json,
                       counterfactual_json, reward
                FROM decisions WHERE {where} ORDER BY id LIMIT ?
                """,
                parameters,
            ).fetchall()
        finally:
            connection.close()
        return [
            {
                "id": row[0],
                "features": json.loads(row[1]),
                "legal_actions": json.loads(row[2]),
                "strategy": json.loads(row[3]),
                "counterfactual": json.loads(row[4]),
                "reward": row[5],
            }
            for row in rows
        ]

    def rewarded_untrained(self, limit: int = 128) -> list[dict[str, Any]]:
        connection = self.connect()
        try:
            rows = connection.execute(
                """
                SELECT id, features_json, legal_actions_json, strategy_json,
                       counterfactual_json, reward
                FROM decisions
                WHERE reward IS NOT NULL AND reward_trained = 0
                ORDER BY id LIMIT ?
                """,
                (limit,),
            ).fetchall()
        finally:
            connection.close()
        return [
            {
                "id": row[0],
                "features": json.loads(row[1]),
                "legal_actions": json.loads(row[2]),
                "strategy": json.loads(row[3]),
                "counterfactual": json.loads(row[4]),
                "reward": row[5],
            }
            for row in rows
        ]

    def mark_trained(self, ids: list[int]) -> None:
        if not ids:
            return
        connection = self.connect()
        try:
            connection.executemany(
                "UPDATE decisions SET trained = 1 WHERE id = ?",
                [(identifier,) for identifier in ids],
            )
            connection.commit()
        finally:
            connection.close()

    def mark_rewards_trained(self, ids: list[int]) -> None:
        if not ids:
            return
        connection = self.connect()
        try:
            connection.executemany(
                "UPDATE decisions SET reward_trained = 1 WHERE id = ?",
                [(identifier,) for identifier in ids],
            )
            connection.commit()
        finally:
            connection.close()

    def maximum_untrained_id(self) -> int | None:
        connection = self.connect()
        try:
            row = connection.execute("SELECT MAX(id) FROM decisions WHERE trained = 0").fetchone()
            return None if row[0] is None else int(row[0])
        finally:
            connection.close()

    def count_decisions(self) -> int:
        connection = self.connect()
        try:
            return int(connection.execute("SELECT COUNT(*) FROM decisions").fetchone()[0])
        finally:
            connection.close()

    def get_information_set(self, information_key: str, action_count: int) -> tuple[list[float], list[float], int]:
        connection = self.connect()
        try:
            row = connection.execute(
                "SELECT regrets_json, strategy_sum_json, visits FROM information_sets WHERE information_key = ?",
                (information_key,),
            ).fetchone()
        finally:
            connection.close()
        if row is None:
            return [0.0] * action_count, [0.0] * action_count, 0
        regrets = json.loads(row[0])
        strategy_sum = json.loads(row[1])
        if len(regrets) != action_count or len(strategy_sum) != action_count:
            return [0.0] * action_count, [0.0] * action_count, 0
        return regrets, strategy_sum, int(row[2])

    def update_information_set(
        self,
        information_key: str,
        regrets: list[float],
        strategy_sum: list[float],
        visits: int,
    ) -> None:
        connection = self.connect()
        try:
            connection.execute(
                """
                INSERT INTO information_sets(information_key, regrets_json, strategy_sum_json, visits)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(information_key) DO UPDATE SET
                    regrets_json = excluded.regrets_json,
                    strategy_sum_json = excluded.strategy_sum_json,
                    visits = excluded.visits
                """,
                (information_key, _compact(regrets), _compact(strategy_sum), visits),
            )
            connection.commit()
        finally:
            connection.close()

    @contextmanager
    def training_lock(self) -> Iterator[None]:
        """Advisory cross-process lock for checkpoint/model updates."""
        with self.lock_path.open("a+b") as lock_file:
            _lock_file(lock_file)
            try:
                yield
            finally:
                _unlock_file(lock_file)

    def atomic_torch_save(self, payload: dict[str, Any]) -> None:
        from .model import require_torch

        torch = require_torch()
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=".cfr-model-", suffix=".pt", dir=self.state_dir
        )
        os.close(descriptor)
        temporary = Path(temporary_name)
        try:
            torch.save(payload, temporary)
            with temporary.open("rb") as checkpoint:
                os.fsync(checkpoint.fileno())
            os.replace(temporary, self.checkpoint_path)
            if os.name != "nt":
                try:
                    directory_fd = os.open(self.state_dir, os.O_RDONLY)
                    try:
                        os.fsync(directory_fd)
                    finally:
                        os.close(directory_fd)
                except OSError:
                    # Atomic replace has already completed. Some filesystems do
                    # not support opening/fsyncing a directory.
                    pass
        finally:
            temporary.unlink(missing_ok=True)


def _compact(value: Any) -> str:
    return json.dumps(value, separators=(",", ":"), allow_nan=False)


def _lock_file(lock_file) -> None:
    if os.name == "nt":
        import msvcrt

        lock_file.seek(0)
        lock_file.write(b"\0")
        lock_file.flush()
        lock_file.seek(0)
        msvcrt.locking(lock_file.fileno(), msvcrt.LK_LOCK, 1)
    else:
        import fcntl

        fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX)


def _unlock_file(lock_file) -> None:
    if os.name == "nt":
        import msvcrt

        lock_file.seek(0)
        msvcrt.locking(lock_file.fileno(), msvcrt.LK_UNLCK, 1)
    else:
        import fcntl

        fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)
