"""Bounded online neural CFR updates."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
import pickle
from typing import Any

from .actions import ACTION_COUNT
from .encoding import ENCODER_VERSION, FEATURE_DIM
from .model import MODEL_SCHEMA_VERSION, build_model, require_torch


@dataclass
class TrainingState:
    updates: int = 0
    decisions: int = 0


class CFRTrainer:
    def __init__(self, store, *, learning_rate: float = 3e-4, seed: int = 1946) -> None:
        torch = require_torch()
        torch.set_num_threads(1)
        torch.manual_seed(seed)
        self.torch = torch
        self.store = store
        self.model = build_model()
        self.optimizer = torch.optim.AdamW(self.model.parameters(), lr=learning_rate)
        self.state = TrainingState()
        self._load_checkpoint()

    def inference(self, features: list[float]):
        torch = self.torch
        self.model.eval()
        with torch.no_grad():
            tensor = torch.tensor(features, dtype=torch.float32).unsqueeze(0)
            regrets, strategy_logits, value = self.model(tensor)
        return regrets[0], strategy_logits[0], float(value[0])

    def train_pending(self, maximum: int = 32, *, through_id: int | None = None) -> tuple[int, list[int]]:
        rows = self.store.untrained(maximum, through_id=through_id)
        if not rows:
            return 0, []

        self._train_rows(rows)
        self.state.updates += 1
        self.state.decisions += len(rows)
        return len(rows), [row["id"] for row in rows]

    def train_terminal_rewards(self, maximum: int = 128) -> tuple[int, list[int]]:
        rows = self.store.rewarded_untrained(maximum)
        if not rows:
            return 0, []
        self._train_rows(rows, reward_only=True)
        self.state.updates += 1
        return len(rows), [row["id"] for row in rows]

    def _train_rows(self, rows, *, reward_only: bool = False) -> None:

        torch = self.torch
        self.model.train()
        features = torch.tensor([row["features"] for row in rows], dtype=torch.float32)
        regret_targets = torch.zeros((len(rows), ACTION_COUNT), dtype=torch.float32)
        strategy_targets = torch.zeros((len(rows), ACTION_COUNT), dtype=torch.float32)
        masks = torch.zeros((len(rows), ACTION_COUNT), dtype=torch.bool)
        value_targets = torch.zeros(len(rows), dtype=torch.float32)

        for index, row in enumerate(rows):
            legal = row["legal_actions"]
            masks[index, legal] = True
            counterfactual = torch.tensor(row["counterfactual"], dtype=torch.float32)
            old_strategy = torch.tensor(row["strategy"], dtype=torch.float32)
            baseline = (counterfactual * old_strategy).sum()
            regret_targets[index] = torch.relu(counterfactual - baseline) * masks[index]
            strategy_targets[index] = old_strategy
            value_targets[index] = float(row["reward"] if row["reward"] is not None else baseline)

        regrets, logits, values = self.model(features)
        regret_loss = ((regrets - regret_targets) ** 2 * masks).sum() / masks.sum().clamp_min(1)
        log_probabilities = torch.log_softmax(logits.masked_fill(~masks, -1e9), dim=-1)
        strategy_loss = -(strategy_targets * log_probabilities).sum(dim=-1).mean()
        value_loss = torch.nn.functional.smooth_l1_loss(values, value_targets.tanh())
        loss = value_loss if reward_only else regret_loss + strategy_loss + 0.25 * value_loss

        self.optimizer.zero_grad(set_to_none=True)
        loss.backward()
        torch.nn.utils.clip_grad_norm_(self.model.parameters(), 5.0)
        self.optimizer.step()

    def save(self) -> None:
        self.store.atomic_torch_save(
            {
                "schema_version": MODEL_SCHEMA_VERSION,
                "encoder_version": ENCODER_VERSION,
                "feature_dim": FEATURE_DIM,
                "action_count": ACTION_COUNT,
                "model_state": self.model.state_dict(),
                "optimizer_state": self.optimizer.state_dict(),
                "updates": self.state.updates,
                "decisions": self.state.decisions,
            }
        )

    def _load_checkpoint(self) -> None:
        path = self.store.checkpoint_path
        if not path.exists():
            return
        try:
            payload: dict[str, Any] = self.torch.load(
                path, map_location="cpu", weights_only=True
            )
            if not isinstance(payload, dict):
                return
            if (
                payload.get("schema_version") != MODEL_SCHEMA_VERSION
                or payload.get("encoder_version") != ENCODER_VERSION
                or payload.get("feature_dim") != FEATURE_DIM
                or payload.get("action_count") != ACTION_COUNT
            ):
                return
            updates = payload.get("updates", 0)
            decisions = payload.get("decisions", 0)
            if any(type(value) is not int or value < 0 for value in (updates, decisions)):
                return
            # Loading a state dict can mutate some parameters before rejecting
            # another one. Commit the entire checkpoint only after validation.
            model, optimizer = deepcopy((self.model, self.optimizer))
            model.load_state_dict(payload["model_state"])
            optimizer.load_state_dict(payload["optimizer_state"])
            for parameter, state in optimizer.state.items():
                if not state:
                    continue
                for name in ("exp_avg", "exp_avg_sq"):
                    moment = state[name]
                    if (not isinstance(moment, self.torch.Tensor)
                            or moment.shape != parameter.shape
                            or not self.torch.isfinite(moment).all()):
                        return
                if "max_exp_avg_sq" in state:
                    moment = state["max_exp_avg_sq"]
                    if (not isinstance(moment, self.torch.Tensor)
                            or moment.shape != parameter.shape
                            or not self.torch.isfinite(moment).all()):
                        return
            # AdamW's loader accepts malformed hyperparameters and state that
            # fail only on step(). Exercise a disposable copy before committing
            # so recovery also guarantees the next real decision can train.
            probe_model, probe_optimizer = deepcopy((model, optimizer))
            for parameter in probe_model.parameters():
                parameter.grad = self.torch.ones_like(parameter)
            probe_optimizer.step()
            if any(not self.torch.isfinite(parameter).all()
                   for parameter in probe_model.parameters()):
                return
            self.model, self.optimizer = model, optimizer
            self.state = TrainingState(updates=updates, decisions=decisions)
        except (OSError, EOFError, pickle.UnpicklingError, RuntimeError,
                ValueError, KeyError, TypeError, AttributeError, IndexError,
                ArithmeticError, AssertionError):
            # A valid SQLite journal remains enough to rebuild after a bad file.
            return
